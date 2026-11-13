from fastapi.testclient import TestClient

from talk_to_rag.api.app import create_app
from talk_to_rag.api.routes.concepts import parse_prerequisites, parse_questions
from talk_to_rag.llm import ModelError


def _client_with_reply(monkeypatch, reply, seen_prompts=None):
    class StubChat:
        def complete(self, messages):
            if seen_prompts is not None:
                seen_prompts.append(messages[-1].content)
            return reply

    monkeypatch.setattr("talk_to_rag.api.routes.concepts.chat_model", lambda _: StubChat())
    return TestClient(create_app())


def test_propose_prerequisites_returns_parsed_concepts_and_names_known_ones(monkeypatch):
    prompts = []
    reply = 'Sure:\n```json\n[{"name": "Matrix multiplication", "summary": "Combining two matrices."}, {"name": "Convolution"}]\n```'
    client = _client_with_reply(monkeypatch, reply, prompts)

    response = client.post(
        "/v1/concepts/propose-prerequisites",
        json={"concept": "Convolutional neural networks", "known_concepts": ["Matrix multiplication"]},
    )

    assert response.status_code == 200
    assert response.json()["prerequisites"] == [
        {"name": "Matrix multiplication", "summary": "Combining two matrices."},
        {"name": "Convolution", "summary": None},
    ]
    assert "- Matrix multiplication" in prompts[0]


def test_propose_prerequisites_rejects_empty_concept(monkeypatch):
    client = _client_with_reply(monkeypatch, "[]")

    assert client.post("/v1/concepts/propose-prerequisites", json={"concept": "  "}).status_code == 400


def test_propose_prerequisites_reports_model_outage(monkeypatch):
    class DownChat:
        def complete(self, messages):
            raise ModelError("local-3b: Connection error.")

    monkeypatch.setattr("talk_to_rag.api.routes.concepts.chat_model", lambda _: DownChat())

    response = TestClient(create_app()).post("/v1/concepts/propose-prerequisites", json={"concept": "Gradients"})

    assert response.status_code == 503


def test_parse_prerequisites_drops_self_duplicates_and_malformed_items():
    reply = '[{"name": "gradients"}, {"name": "Chain  rule"}, {"name": "chain rule"}, "junk", {"summary": "no name"}]'

    assert [p.name for p in parse_prerequisites(reply, "Gradients")] == ["Chain rule"]


def test_parse_prerequisites_returns_nothing_for_prose():
    assert parse_prerequisites("I think you should study maths.", "Gradients") == []


def test_generate_questions_sends_numbered_passages_and_returns_cards(monkeypatch):
    prompts = []
    reply = '[{"prompt": "What does pooling do?", "answer": "It downsamples feature maps.", "passage": 2}]'
    client = _client_with_reply(monkeypatch, reply, prompts)

    response = client.post(
        "/v1/concepts/generate-questions",
        json={"concept": "Pooling", "passages": [{"n": 2, "text": "Pooling downsamples feature maps."}], "count": 3},
    )

    assert response.status_code == 200
    assert response.json()["questions"] == [{"prompt": "What does pooling do?", "answer": "It downsamples feature maps.", "passage": 2}]
    assert "[2] Pooling downsamples feature maps." in prompts[0]


def test_generate_questions_needs_a_passage(monkeypatch):
    client = _client_with_reply(monkeypatch, "[]")

    response = client.post("/v1/concepts/generate-questions", json={"concept": "Pooling", "passages": []})

    assert response.status_code == 400


def test_parse_questions_drops_malformed_cards_and_unknown_passage_numbers():
    reply = '[{"prompt": "Q1", "answer": "A1", "passage": 9}, {"prompt": "Q2"}, {"prompt": " ", "answer": "A"}, "junk"]'

    questions = parse_questions(reply, {1, 2})

    assert [(q.prompt, q.passage) for q in questions] == [("Q1", None)]
