"""Adapter for any server that speaks the OpenAI chat and embeddings API.

That covers Ollama, mlx_lm.server, llama.cpp's llama-server and OpenAI itself.
"""

import os
from collections.abc import Iterator, Mapping, Sequence
from contextlib import contextmanager
from typing import Any

from talk_to_rag.llm.contracts import Message, ModelError

# Local servers ignore the key, but the client library insists on one.
_PLACEHOLDER_KEY = "not-needed"


class OpenAICompatibleChat:
    def __init__(self, client: Any, model: str, options: Mapping[str, Any]):
        self._client = client
        self._model = model
        self._options = dict(options)

    def complete(self, messages: Sequence[Message]) -> str:
        with _as_model_error(self._model):
            response = self._client.chat.completions.create(
                model=self._model,
                messages=[{"role": m.role, "content": m.content} for m in messages],
                **self._options,
            )
        return response.choices[0].message.content or ""

    def stream(self, messages: Sequence[Message]) -> Iterator[str]:
        with _as_model_error(self._model):
            response = self._client.chat.completions.create(
                model=self._model,
                messages=[{"role": m.role, "content": m.content} for m in messages],
                stream=True,
                **self._options,
            )
            for chunk in response:
                delta = chunk.choices[0].delta.content if chunk.choices else None
                if delta:
                    yield delta


class OpenAICompatibleEmbedder:
    def __init__(self, client: Any, model: str, options: Mapping[str, Any]):
        self._client = client
        self._model = model
        self._options = dict(options)
        self._document_prefix = str(self._options.pop("document_prefix", ""))
        self._query_prefix = str(self._options.pop("query_prefix", ""))

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        prefixed = [f"{self._document_prefix}{text}" for text in texts]
        return self._embed(prefixed)

    def embed_query(self, text: str) -> list[float]:
        prefixed = [f"{self._query_prefix}{text}"]
        vectors = self._embed(prefixed)
        return vectors[0]

    def _embed(self, texts: Sequence[str]) -> list[list[float]]:
        if not texts:
            return []
        with _as_model_error(self._model):
            response = self._client.embeddings.create(
                model=self._model, input=list(texts), **self._options
            )
        return [item.embedding for item in response.data]


@contextmanager
def _as_model_error(model: str) -> Iterator[None]:
    from openai import OpenAIError

    try:
        yield
    except OpenAIError as error:
        raise ModelError(f"{model}: {error}") from error


def _client(provider: Mapping[str, Any]) -> Any:
    from openai import OpenAI

    key_env = provider.get("api_key_env")
    api_key = os.environ.get(key_env) if key_env else _PLACEHOLDER_KEY
    if not api_key:
        raise RuntimeError(f"Set the {key_env} environment variable for this provider.")
    return OpenAI(base_url=base_url(provider), api_key=api_key)


def base_url(provider: Mapping[str, Any]) -> str:
    # A container reaches the host's Ollama at a different address, so the environment may override it.
    url_env = provider.get("base_url_env")
    return (os.environ.get(url_env) if url_env else None) or provider["base_url"]


def build_chat(provider: Mapping[str, Any], model: Mapping[str, Any]) -> OpenAICompatibleChat:
    return OpenAICompatibleChat(_client(provider), model["name"], model.get("options", {}))


def build_embedder(
    provider: Mapping[str, Any], model: Mapping[str, Any]
) -> OpenAICompatibleEmbedder:
    return OpenAICompatibleEmbedder(_client(provider), model["name"], model.get("options", {}))
