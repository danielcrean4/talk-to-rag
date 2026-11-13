"""Turn a model name from the config into an object that satisfies a contract.

The model is chosen by, in order: the name passed in, the TALK_TO_RAG_CHAT or
TALK_TO_RAG_EMBED environment variable, then [defaults] in the config.
"""

import importlib
import os
from typing import Any

from talk_to_rag.llm import config as models_config
from talk_to_rag.llm.adapters import ADAPTERS
from talk_to_rag.llm.config import ConfigError, ModelsConfig
from talk_to_rag.llm.contracts import ChatModel, Embedder


def chat_model(name: str | None = None, config: ModelsConfig | None = None) -> ChatModel:
    from talk_to_rag.tracing import TracedChat

    config = config or models_config.load()
    resolved = resolve_name("chat", name, config)
    return TracedChat(_build("chat", resolved, config), resolved, served_model("chat", resolved, config))


def embedder(name: str | None = None, config: ModelsConfig | None = None) -> Embedder:
    return _build("embed", name, config)


def served_model(kind: str, name: str | None = None, config: ModelsConfig | None = None) -> str:
    """The id the provider knows the model by, such as "nomic-embed-text" for "local-embed"."""
    config = config or models_config.load()
    resolved = resolve_name(kind, name, config)
    return config.models.get(resolved, {}).get("name", resolved)


def resolve_name(kind: str, name: str | None, config: ModelsConfig) -> str:
    return name or os.environ.get(f"TALK_TO_RAG_{kind.upper()}") or config.defaults[kind]


def _build(kind: str, name: str | None, config: ModelsConfig | None) -> Any:
    config = config or models_config.load()
    name = resolve_name(kind, name, config)

    model = config.models.get(name)
    if model is None:
        raise ConfigError(f"Unknown model '{name}'; configured: {sorted(config.models)}")
    if model["kind"] != kind:
        raise ConfigError(f"Model '{name}' is a {model['kind']} model, not {kind}")

    provider = config.providers[model["provider"]]
    adapter = importlib.import_module(ADAPTERS[provider["adapter"]])
    build = adapter.build_chat if kind == "chat" else adapter.build_embedder
    return build(provider, model)
