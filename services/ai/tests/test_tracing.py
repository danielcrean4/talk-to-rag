import contextlib

from fastapi.testclient import TestClient

from talk_to_rag import tracing
from talk_to_rag.api.app import create_app
from talk_to_rag.llm import Message, chat_model


def _passages():
    return [{"n": 1, "content": "TD learning bootstraps from the next estimate."}]


def test_answer_response_carries_the_prompt_reply_and_model(monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_CHAT", "fake-chat")
    monkeypatch.setattr("talk_to_rag.api.routes.rag.chat_model", lambda _: chat_model("fake-chat"))

    response = TestClient(create_app()).post("/v1/rag/answer", json={"question": "What is TD?", "passages": _passages()})

    call = response.json()["trace"][0]
    assert (call["kind"], call["model"], call["served_model"], call["error"]) == ("chat", "fake-chat", "fake", None)
    assert [m["role"] for m in call["messages"]] == ["system", "user"]
    assert "TD learning bootstraps" in call["messages"][1]["content"]
    assert call["completion"] == response.json()["answer"]
    assert call["duration_ms"] >= 0


def test_streamed_answer_sends_its_trace_before_the_done_event(monkeypatch):
    monkeypatch.setattr("talk_to_rag.api.routes.rag.chat_model", lambda _: chat_model("fake-chat"))

    response = TestClient(create_app()).post("/v1/rag/answer/stream", json={"question": "What is TD?", "passages": _passages()})

    lines = [line for line in response.text.splitlines() if line]
    assert lines[-2].startswith('data: {"trace": [{"kind": "chat"')
    assert lines[-1].startswith('data: {"done": true')


def test_traces_do_not_leak_between_requests(monkeypatch):
    monkeypatch.setattr("talk_to_rag.api.routes.rag.chat_model", lambda _: chat_model("fake-chat"))
    client = TestClient(create_app())
    body = {"question": "What is TD?", "passages": _passages()}

    client.post("/v1/rag/answer", json=body)

    assert len(client.post("/v1/rag/answer", json=body).json()["trace"]) == 1


def test_a_failed_call_is_recorded_with_its_error():
    class Failing:
        def complete(self, messages):
            raise RuntimeError("model is down")

    async def collect():
        await tracing.start_trace()
        with contextlib.suppress(RuntimeError):
            tracing.TracedChat(Failing(), "local-3b", "ministral").complete([Message(role="user", content="hi")])
        return tracing.calls()

    import asyncio

    call = asyncio.run(collect())[0]
    assert (call["error"], call["completion"]) == ("model is down", None)
