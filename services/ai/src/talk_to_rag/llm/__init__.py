"""Model access for talk-to-rag. Import from here, never from an adapter."""

from talk_to_rag.llm.config import ConfigError
from talk_to_rag.llm.contracts import ChatModel, Embedder, Message, ModelError
from talk_to_rag.llm.factory import chat_model, embedder

__all__ = ["ChatModel", "ConfigError", "Embedder", "Message", "ModelError", "chat_model", "embedder"]
