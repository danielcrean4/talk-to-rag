"""Records the model calls made while serving one request, so the caller can keep an audit trail.

This service has no database. Each response carries its calls in a ``trace`` field,
and the API, which owns all state, stores them.
"""

import time
from collections.abc import Iterator, Sequence
from contextvars import ContextVar
from typing import Any

from talk_to_rag.llm.contracts import ChatModel, Message

_calls: ContextVar[list[dict[str, Any]] | None] = ContextVar("model_calls", default=None)


async def start_trace() -> None:
    """FastAPI dependency. It is async so the list is set in the request's own context, which worker threads copy."""
    _calls.set([])


def calls() -> list[dict[str, Any]]:
    """The model calls recorded so far for this request."""
    return list(_calls.get() or [])


class TracedChat:
    """Wraps a chat model and records each prompt, reply, duration and failure."""

    def __init__(self, inner: ChatModel, name: str, served_model: str):
        self._inner = inner
        self._name = name
        self._served_model = served_model

    def complete(self, messages: Sequence[Message]) -> str:
        started = time.perf_counter()
        try:
            completion = self._inner.complete(messages)
        except Exception as error:
            self._record(messages, None, started, error)
            raise
        self._record(messages, completion, started, None)
        return completion

    def stream(self, messages: Sequence[Message]) -> Iterator[str]:
        started = time.perf_counter()
        parts: list[str] = []
        try:
            for token in self._inner.stream(messages):
                parts.append(token)
                yield token
        except Exception as error:
            self._record(messages, "".join(parts), started, error)
            raise
        self._record(messages, "".join(parts), started, None)

    def _record(self, messages: Sequence[Message], completion: str | None, started: float, error: Exception | None) -> None:
        recorded = _calls.get()
        if recorded is None:
            return
        recorded.append(
            {
                "kind": "chat",
                "model": self._name,
                "served_model": self._served_model,
                "messages": [{"role": m.role, "content": m.content} for m in messages],
                "completion": completion,
                "duration_ms": round((time.perf_counter() - started) * 1000),
                "error": str(error) if error else None,
            }
        )
