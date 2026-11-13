import psycopg
import pytest

from talk_to_rag_audit_mcp import audit


def test_fit_keeps_small_values_and_reports_the_full_length_of_cut_ones():
    shortened: list[str] = []

    assert audit._fit(3, 20, "row 1, n", shortened) == 3
    assert audit._fit({"a": [1, 2]}, 20, "row 1, body", shortened) == {"a": [1, 2]}
    assert audit._fit("x" * 50, 20, "row 2, text", shortened) == "x" * 20 + "…"
    assert audit._fit({"answer": "y" * 50}, 20, "row 3, body", shortened).endswith("…")
    assert shortened == ["row 2, text: showing 20 of 50 characters", "row 3, body: showing 20 of 64 characters"]


def test_a_reply_too_large_to_read_is_trimmed_to_whole_rows_and_says_so():
    result = audit.Result(
        ["text"], [["x" * 25_000] for _ in range(5)], more_rows=False, shortened=["row 4, text: showing 5 of 9 characters"]
    )

    reply = result.to_dict()

    assert reply["row_count"] == 2
    assert reply["more_rows"].startswith("Showing 2 of 5 fetched rows")
    assert "shortened" not in reply


@pytest.fixture
def database():
    """These tests need the compose database with migrations applied; without it they are skipped."""
    try:
        psycopg.connect(audit.dsn(), connect_timeout=2).close()
    except psycopg.OperationalError:
        pytest.skip("the audit database is not running")


def test_reads_audit_views_and_serialises_rows(database):
    result = audit.run("SELECT table_name FROM information_schema.views WHERE table_schema = 'audit' ORDER BY 1")

    assert result.columns == ["table_name"]
    assert ["ai_calls"] in result.rows and ["model_calls"] in result.rows


def test_caps_rows_and_says_so(database):
    result = audit.run("SELECT generate_series(1, 10)", max_rows=3)

    assert result.rows == [[1], [2], [3]]
    assert "more_rows" in result.to_dict()


@pytest.mark.parametrize(
    "sql, expected",
    [
        ("SELECT count(*) FROM public.source_chunks", "permission denied"),
        ("SELECT count(*) FROM ai_calls", "permission denied"),
        ("SELECT 1; SELECT 2", "one statement per call"),
        ("CREATE TABLE audit.x (i int)", "read-only transaction"),
        ("CREATE TEMP TABLE x (i int)", "read-only transaction"),
        ("SELECT pg_sleep(30)", "statement timeout"),
        ("SELECT nope FROM audit.ai_calls", "does not exist"),
    ],
)
def test_refuses_everything_but_one_select_on_the_audit_schema(database, sql, expected, monkeypatch):
    monkeypatch.setattr(audit, "STATEMENT_TIMEOUT", "300ms")

    with pytest.raises(audit.AuditError) as error:
        audit.run(sql)

    assert expected in str(error.value).lower()


def test_a_setting_changed_in_one_call_does_not_reach_the_next(database):
    audit.run("SELECT set_config('default_transaction_read_only', 'off', false)")

    with pytest.raises(audit.AuditError):
        audit.run("CREATE TEMP TABLE x (i int)")


def test_refuses_a_login_that_can_read_the_tables(database, monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_AUDIT_DSN", "postgresql://talk_to_rag:talk_to_rag@127.0.0.1:5432/talk_to_rag")

    with pytest.raises(audit.AuditError, match="will not use it"):
        audit.run("SELECT 1")
