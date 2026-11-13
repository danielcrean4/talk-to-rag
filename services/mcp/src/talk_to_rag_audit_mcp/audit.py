"""Runs one read-only statement against the audit schema and shapes the result to fit a tool reply."""

from __future__ import annotations

import json
import os
from dataclasses import dataclass
from typing import Any

import psycopg

DEFAULT_DSN = "postgresql://talk_to_rag_auditor:auditor-local-only@127.0.0.1:5432/talk_to_rag"
MAX_ROWS = 500
MAX_CELL_CHARACTERS = 200_000
STATEMENT_TIMEOUT = "10s"
# A tool reply much larger than this is cut off or refused by the client reading it.
REPLY_CHARACTERS = 60_000


class AuditError(Exception):
    """A problem the caller can act on; its message is returned as the tool's reply."""


@dataclass(frozen=True)
class Result:
    columns: list[str]
    rows: list[list[Any]]
    more_rows: bool
    shortened: list[str]

    def to_dict(self) -> dict[str, Any]:
        rows, size = [], 0
        for row in self.rows:
            size += len(json.dumps(row, ensure_ascii=False, default=str))
            if rows and size > REPLY_CHARACTERS:
                break
            rows.append(row)

        reply: dict[str, Any] = {"columns": self.columns, "rows": rows, "row_count": len(rows)}
        if len(rows) < len(self.rows):
            reply["more_rows"] = (
                f"Showing {len(rows)} of {len(self.rows)} fetched rows to keep the reply readable. "
                "Select fewer columns, lower max_cell_characters, or page with ORDER BY, LIMIT and OFFSET."
            )
        elif self.more_rows:
            reply["more_rows"] = "There are more rows than shown. Narrow the query, or page with ORDER BY, LIMIT and OFFSET."
        shortened = [note for note in self.shortened if int(note.split(",")[0].removeprefix("row ")) <= len(rows)]
        if shortened:
            reply["shortened"] = shortened
        return reply


def dsn() -> str:
    return os.environ.get("TALK_TO_RAG_AUDIT_DSN", DEFAULT_DSN)


def run(sql: str, max_rows: int = 50, max_cell_characters: int = 2000) -> Result:
    """Runs one statement in a read-only transaction that is always rolled back."""
    max_rows = max(1, min(max_rows, MAX_ROWS))
    max_cell_characters = max(20, min(max_cell_characters, MAX_CELL_CHARACTERS))
    try:
        # A connection per call, so nothing one call sets can reach the next.
        with psycopg.connect(dsn(), autocommit=True, connect_timeout=5) as connection:
            _refuse_a_privileged_login(connection)
            connection.execute("BEGIN READ ONLY")
            try:
                connection.execute(f"SET LOCAL statement_timeout = '{STATEMENT_TIMEOUT}'")
                # Preparing uses the extended protocol, which takes one statement only.
                cursor = connection.execute(sql.strip().rstrip(";"), prepare=True)
                if cursor.description is None:
                    raise AuditError("That statement returns no rows. Only SELECT is useful here.")
                columns = [column.name for column in cursor.description]
                fetched = cursor.fetchmany(max_rows + 1)
            finally:
                connection.execute("ROLLBACK")
    except psycopg.Error as error:
        raise AuditError(_describe(error)) from error

    shortened: list[str] = []
    rows = [
        [_fit(value, max_cell_characters, f"row {number}, {column}", shortened) for column, value in zip(columns, row, strict=True)]
        for number, row in enumerate(fetched[:max_rows], start=1)
    ]
    return Result(columns, rows, len(fetched) > max_rows, shortened)


def _refuse_a_privileged_login(connection: psycopg.Connection) -> None:
    """Stops a wrong connection string from handing the caller the real tables."""
    can_read_tables = connection.execute(
        "SELECT rolsuper OR has_table_privilege(current_user, 'public.source_chunks', 'SELECT') FROM pg_roles WHERE rolname = current_user"
    ).fetchone()
    if can_read_tables is None or can_read_tables[0]:
        raise AuditError(
            "This login can read the application tables, so the audit server will not use it. "
            "Point TALK_TO_RAG_AUDIT_DSN at the talk_to_rag_auditor role."
        )


def _describe(error: psycopg.Error) -> str:
    parts = [str(error).strip() or type(error).__name__]
    hint = getattr(error.diag, "message_hint", None)
    if hint:
        parts.append(f"Hint: {hint}")
    if isinstance(error, psycopg.errors.InsufficientPrivilege):
        parts.append("Only the views in the audit schema can be read; call describe to list them.")
    if isinstance(error, psycopg.errors.SyntaxError) and "multiple commands" in str(error):
        parts.append("Send one statement per call.")
    if isinstance(error, psycopg.OperationalError) and error.sqlstate is None:
        parts.append("Is the database running? Start it with `make up`.")
    return "\n".join(parts)


def _fit(value: Any, limit: int, where: str, shortened: list[str]) -> Any:
    """Makes a cell JSON-safe and cuts it to the limit, recording its full length when cut."""
    if value is None or isinstance(value, (bool, int, float)):
        return value
    if isinstance(value, (dict, list)):
        text = json.dumps(value, ensure_ascii=False, default=str)
        if len(text) <= limit:
            return json.loads(text)
    else:
        text = str(value)
        if len(text) <= limit:
            return text
    shortened.append(f"{where}: showing {limit} of {len(text)} characters")
    return text[:limit] + "…"
