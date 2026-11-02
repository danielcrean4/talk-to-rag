"""Offline stand-ins for tests and for working without a model server."""

import hashlib
from collections.abc import Mapping, Sequence
from typing import Any

from talk_to_rag.llm.contracts import Message


class FakeChat:
    def complete(self, messages: Sequence[Message]) -> str:
        last_user = next((m.content for m in reversed(messages) if m.role == "user"), "")
        return f"(fake reply to: {last_user})"


class FakeEmbedder:
    def __init__(
        self,
        dimensions: int = 8,
        document_prefix: str = "",
        query_prefix: str = "",
    ):
        self._dimensions = dimensions
        self._document_prefix = document_prefix
        self._query_prefix = query_prefix

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        # Deterministic: the same text always gets the same vector.
        return [self._vector(f"{self._document_prefix}{text}") for text in texts]

    def embed_query(self, text: str) -> list[float]:
        return self._vector(f"{self._query_prefix}{text}")

    def _vector(self, text: str) -> list[float]:
        digest = hashlib.sha256(text.encode()).digest()
        return [byte / 255 for byte in digest[: self._dimensions]]


def build_chat(provider: Mapping[str, Any], model: Mapping[str, Any]) -> FakeChat:
    return FakeChat()


def build_embedder(provider: Mapping[str, Any], model: Mapping[str, Any]) -> FakeEmbedder:
    options = model.get("options", {})
    return FakeEmbedder(
        dimensions=options.get("dimensions", 8),
        document_prefix=options.get("document_prefix", ""),
        query_prefix=options.get("query_prefix", ""),
    )
