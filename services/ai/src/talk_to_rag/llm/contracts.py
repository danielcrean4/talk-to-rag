"""The only model types the rest of talk-to-rag may depend on.

These are Protocols, so any object with matching methods satisfies them;
adapters never need to inherit from anything.
"""

from collections.abc import Sequence
from dataclasses import dataclass
from typing import Literal, Protocol, runtime_checkable

Role = Literal["system", "user", "assistant"]


class ModelError(RuntimeError):
    """Any failure from a model backend. Adapters raise this, never their library's errors."""


@dataclass(frozen=True)
class Message:
    role: Role
    content: str


@runtime_checkable
class ChatModel(Protocol):
    def complete(self, messages: Sequence[Message]) -> str:
        """Return the assistant's reply to the conversation so far."""
        ...

    def stream(self, messages: Sequence[Message]):
        """Yield token strings as they arrive from the backend."""
        ...


@runtime_checkable
class Embedder(Protocol):
    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        """Return one vector per input document text, in the same order."""
        ...

    def embed_query(self, text: str) -> list[float]:
        """Return the vector for a search query text."""
        ...
