from fastapi.testclient import TestClient
import pytest

from talk_to_rag.api.app import create_app
from talk_to_rag.llm import Message, ModelError


def test_expand_query_no_expansion(monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")

    client = TestClient(create_app())
    response = client.post(
        "/v1/rag/expand-query",
        json={"question": "what is TD learning?", "expand": False},
    )

    assert response.status_code == 200
    data = response.json()
    assert len(data["queries"]) == 1
    assert data["queries"][0]["text"] == "what is TD learning?"
    assert len(data["queries"][0]["embedding"]) == 8


def test_expand_query_with_expansion(monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")

    class StubChat:
        def complete(self, messages):
            return "temporal difference definition\nBellman equation bootstrapping\nTD learning vs Monte Carlo"

    monkeypatch.setattr("talk_to_rag.api.routes.rag.chat_model", lambda _: StubChat())

    client = TestClient(create_app())
    response = client.post(
        "/v1/rag/expand-query",
        json={"question": "what is TD learning?", "expand": True},
    )

    assert response.status_code == 200
    data = response.json()
    queries = [q["text"] for q in data["queries"]]
    assert "what is TD learning?" in queries
    assert "temporal difference definition" in queries
    assert "Bellman equation bootstrapping" in queries
    assert len(data["queries"]) > 1


def test_expand_query_expansion_failure_falls_back(monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")

    class CrashingChat:
        def complete(self, messages):
            raise ModelError("Model generation failed")

    monkeypatch.setattr("talk_to_rag.api.routes.rag.chat_model", lambda _: CrashingChat())

    client = TestClient(create_app())
    response = client.post(
        "/v1/rag/expand-query",
        json={"question": "what is TD learning?", "expand": True},
    )

    assert response.status_code == 200
    data = response.json()
    assert len(data["queries"]) == 1
    assert data["queries"][0]["text"] == "what is TD learning?"


def test_expand_query_preserves_numeric_prefixes_in_search_terms(monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")

    class NumberedListChat:
        def complete(self, messages):
            return "1. 2024 tax rules update\n2. * IRS guidelines 2024\n3. 1040 deductions"

    monkeypatch.setattr("talk_to_rag.api.routes.rag.chat_model", lambda _: NumberedListChat())

    client = TestClient(create_app())
    response = client.post(
        "/v1/rag/expand-query",
        json={"question": "tax rules", "expand": True},
    )

    assert response.status_code == 200
    queries = [q["text"] for q in response.json()["queries"]]
    assert "2024 tax rules update" in queries
    assert "IRS guidelines 2024" in queries
    assert "1040 deductions" in queries


def test_expand_query_rejects_empty():
    client = TestClient(create_app())
    response = client.post("/v1/rag/expand-query", json={"question": "   "})
    assert response.status_code == 400


def test_answer_passages_generates_cited_answer(monkeypatch):
    class StubChat:
        def complete(self, messages):
            return "Temporal difference learning updates from estimates [1], whereas Monte Carlo waits until the end [2]."

    monkeypatch.setattr("talk_to_rag.api.routes.rag.chat_model", lambda _: StubChat())

    client = TestClient(create_app())
    response = client.post(
        "/v1/rag/answer",
        json={
            "question": "What is the difference between TD and MC?",
            "passages": [
                {"n": 1, "content": "TD updates from bootstrap estimates.", "page_number": 120},
                {"n": 2, "content": "Monte Carlo waits for episode termination.", "page_number": 135},
            ],
        },
    )

    assert response.status_code == 200
    data = response.json()
    assert "[1]" in data["answer"]
    assert data["cited"] == [1, 2]


def test_answer_passages_filters_invented_citations(monkeypatch):
    class StubChat:
        def complete(self, messages):
            # Model hallucinated citation [99] which was not in passages!
            return "TD learning bootstraps [1], and extra details [99]."

    monkeypatch.setattr("talk_to_rag.api.routes.rag.chat_model", lambda _: StubChat())

    client = TestClient(create_app())
    response = client.post(
        "/v1/rag/answer",
        json={
            "question": "Explain bootstrapping.",
            "passages": [
                {"n": 1, "content": "TD learning bootstraps.", "page_number": 120},
            ],
        },
    )

    assert response.status_code == 200
    data = response.json()
    assert data["cited"] == [1]  # 99 was omitted!


def test_answer_passages_empty_passages():
    client = TestClient(create_app())
    response = client.post(
        "/v1/rag/answer",
        json={"question": "Some query", "passages": []},
    )

    assert response.status_code == 200
    data = response.json()
    assert "No relevant passages" in data["answer"]
    assert data["cited"] == []


def test_rag_endpoints_are_in_openapi():
    paths = TestClient(create_app()).get("/openapi.json").json()["paths"]
    assert "/v1/rag/expand-query" in paths
    assert "/v1/rag/answer" in paths
