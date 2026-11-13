"""The MCP tools: describe the audit schema, query it, and fetch one recorded call in full."""

from __future__ import annotations

import json
import uuid
from typing import Any

from mcp.server.mcpserver import MCPServer
from mcp.types import ToolAnnotations

from . import audit

INSTRUCTIONS = """\
Read-only access to what the talk-to-rag app recorded: every call to its local models, the
searches and answers, and the concept graph with practice attempts. Use it to find where the
local models go wrong. Nothing here can change the app.

Start with `describe`. Source text is withheld by design: passages appear as
"[passage 3 withheld, 912 characters]" and copied runs as "[quotes passage 3, 240 characters]",
with source, page and section given in their place. Do not try to reconstruct the text."""

READ_ONLY = ToolAnnotations(readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False)

server = MCPServer("talk-to-rag-audit", instructions=INSTRUCTIONS)


def _reply(build) -> str:
    try:
        return json.dumps(build(), ensure_ascii=False, default=str)
    except audit.AuditError as error:
        return f"Error: {error}"


@server.tool(annotations=READ_ONLY)
def describe() -> str:
    """Lists every view in the audit schema with its columns, types and row count. Call this first."""

    def build() -> dict[str, Any]:
        columns = audit.run(
            "SELECT table_name, column_name, data_type FROM information_schema.columns "
            "WHERE table_schema = 'audit' ORDER BY table_name, ordinal_position",
            max_rows=audit.MAX_ROWS,
        )
        views: dict[str, dict[str, Any]] = {}
        for view, column, data_type in columns.rows:
            views.setdefault(view, {"columns": {}})["columns"][column] = data_type
        for view in views:
            views[view]["rows"] = audit.run(f'SELECT count(*) FROM audit."{view}"').rows[0][0]
        return {
            "views": views,
            "notes": [
                "audit.ai_calls: one row per request to the AI service; request and response are jsonb.",
                "audit.model_calls: one row per model call inside those requests, with the messages sent and the completion.",
                "audit.answers: the question, answer and cited passage numbers of each summary.",
                "audit.messages, audit.questions: text a model wrote is given by length only; read it through the three views above.",
                "audit.citations: where each cited passage is (source, page, section), never its text.",
                "audit.sources: license and source_url are what the user recorded about a source's license and origin; either may be null.",
                "Mastery is not stored; derive it from audit.attempts (score runs from 0 to 1).",
            ],
        }

    return _reply(build)


@server.tool(annotations=READ_ONLY)
def query(sql: str, max_rows: int = 50, max_cell_characters: int = 2000) -> str:
    """Runs one SELECT against the audit schema (PostgreSQL 17) and returns columns and rows as JSON.

    One statement per call, in a read-only transaction with a 10 second limit. Results are capped
    at max_rows (up to 500) and each cell at max_cell_characters (up to 200000); the reply says
    when rows or cells were cut and how long a cut cell really is. jsonb operators work, for
    example `response->>'answer'` or `jsonb_array_length(request->'passages')`.
    """
    return _reply(lambda: audit.run(sql, max_rows, max_cell_characters).to_dict())


@server.tool(annotations=READ_ONLY)
def get_ai_call(ai_call_id: str) -> str:
    """Returns one row of audit.ai_calls in full, with the model calls made inside it."""

    def build() -> dict[str, Any]:
        try:
            call_id = uuid.UUID(ai_call_id)
        except ValueError as error:
            raise audit.AuditError(f"'{ai_call_id}' is not a uuid; take one from audit.ai_calls.id.") from error
        found = audit.run(f"SELECT * FROM audit.ai_calls WHERE id = '{call_id}'", max_rows=1, max_cell_characters=audit.MAX_CELL_CHARACTERS)
        if not found.rows:
            raise audit.AuditError(f"No recorded call has id {call_id}.")
        return dict(zip(found.columns, found.rows[0], strict=True)) | ({"shortened": found.shortened} if found.shortened else {})

    return _reply(build)


def main() -> None:
    server.run("stdio")


if __name__ == "__main__":
    main()
