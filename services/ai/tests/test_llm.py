from pathlib import Path
from types import SimpleNamespace

import pytest

from talk_to_rag.llm import ChatModel, Embedder, Message, ModelError, chat_model, embedder, served_model
from talk_to_rag.llm.adapters.openai_compatible import (
    OpenAICompatibleChat,
    OpenAICompatibleEmbedder,
    base_url,
    build_chat,
)
from talk_to_rag.llm.config import ConfigError, _read, load, merge, parse
from talk_to_rag.llm.factory import resolve_name


def config_data(**overrides):
    data = {
        "defaults": {"chat": "chat", "embed": "embed"},
        "providers": {"offline": {"adapter": "fake"}},
        "models": {
            "chat": {"provider": "offline", "kind": "chat", "name": "fake"},
            "embed": {"provider": "offline", "kind": "embed", "name": "fake"},
        },
    }
    data.update(overrides)
    return data


def test_committed_config_is_valid():
    config = load("config/models.toml")
    assert config.models[config.defaults["chat"]]["kind"] == "chat"


def test_example_local_config_layers_over_the_committed_one():
    config = parse(merge(_read(Path("config/models.toml")), _read(Path("config/models.local.example.toml"))))
    assert config.models["remote-4b"]["provider"] == "remote"


def test_local_file_adds_to_and_replaces_base_entries(tmp_path, monkeypatch):
    (tmp_path / "config").mkdir()
    (tmp_path / "config/models.toml").write_text(
        '[defaults]\nchat = "chat"\nembed = "embed"\n'
        '[providers.offline]\nadapter = "fake"\n'
        '[models.chat]\nprovider = "offline"\nkind = "chat"\nname = "fake"\n'
        '[models.embed]\nprovider = "offline"\nkind = "embed"\nname = "fake"\n'
    )
    (tmp_path / "config/models.local.toml").write_text(
        '[defaults]\nchat = "private"\n[models.private]\nprovider = "offline"\nkind = "chat"\nname = "fake"\n'
    )
    monkeypatch.chdir(tmp_path)
    monkeypatch.delenv("TALK_TO_RAG_MODELS", raising=False)
    monkeypatch.delenv("TALK_TO_RAG_CONFIG_DIR", raising=False)

    config = load()

    assert config.defaults == {"chat": "private", "embed": "embed"}
    assert set(config.models) == {"chat", "embed", "private"}


def test_service_config_is_found_from_any_directory(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    monkeypatch.delenv("TALK_TO_RAG_MODELS", raising=False)
    monkeypatch.delenv("TALK_TO_RAG_CONFIG_DIR", raising=False)

    assert "local-embed" in load().models


def test_config_dir_can_be_set_from_the_environment(tmp_path, monkeypatch):
    (tmp_path / "models.toml").write_text(
        '[defaults]\nchat = "chat"\nembed = "embed"\n'
        '[providers.offline]\nadapter = "fake"\n'
        '[models.chat]\nprovider = "offline"\nkind = "chat"\nname = "fake"\n'
        '[models.embed]\nprovider = "offline"\nkind = "embed"\nname = "fake"\n'
    )
    monkeypatch.delenv("TALK_TO_RAG_MODELS", raising=False)
    monkeypatch.setenv("TALK_TO_RAG_CONFIG_DIR", str(tmp_path))

    assert set(load().models) == {"chat", "embed"}


def test_factory_builds_objects_that_satisfy_the_contracts():
    config = parse(config_data())
    chat, embed = chat_model(config=config), embedder(config=config)

    assert isinstance(chat, ChatModel) and isinstance(embed, Embedder)
    assert chat.complete([Message("user", "hi")]) == "(fake reply to: hi)"
    assert embed.embed_documents(["a", "b"]) == embed.embed_documents(["a", "b"])
    assert embed.embed_query("a") == embed.embed_query("a")
    assert isinstance(embed.embed_query("a"), list)


def test_model_choice_is_argument_then_environment_then_default(monkeypatch):
    config = parse(config_data())
    assert resolve_name("chat", None, config) == "chat"

    monkeypatch.setenv("TALK_TO_RAG_CHAT", "from-env")
    assert resolve_name("chat", None, config) == "from-env"
    assert resolve_name("chat", "from-arg", config) == "from-arg"
    with pytest.raises(ConfigError, match="Unknown model 'from-env'"):
        chat_model(config=config)


def test_served_model_is_the_providers_id_for_the_chosen_model(monkeypatch):
    config = load("config/models.toml")
    monkeypatch.delenv("TALK_TO_RAG_EMBED", raising=False)
    assert served_model("embed", config=config) == "nomic-embed-text"

    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")
    assert served_model("embed", config=config) == "fake"
    assert served_model("chat", "local-3b", config) == "hf.co/mistralai/Ministral-3-3B-Instruct-2512-GGUF:Q4_K_M"


def test_asking_for_the_wrong_kind_fails():
    with pytest.raises(ConfigError, match="not chat"):
        chat_model("embed", config=parse(config_data()))


@pytest.mark.parametrize(
    "overrides, message",
    [
        ({"providers": {"offline": {"adapter": "nope"}}}, "unknown adapter"),
        ({"defaults": {"chat": "embed", "embed": "embed"}}, "defaults.chat points at 'embed'"),
        ({"defaults": {"embed": "embed"}}, "defaults.chat must name a model"),
    ],
)
def test_invalid_config_is_rejected(overrides, message):
    with pytest.raises(ConfigError, match=message):
        parse(config_data(**overrides))


def test_openai_compatible_adapter_maps_requests_and_responses():
    calls = []

    def create_chat(**kwargs):
        return SimpleNamespace(choices=[SimpleNamespace(message=SimpleNamespace(content="pong"))])

    def create_embeddings(**kwargs):
        calls.append(kwargs)
        return SimpleNamespace(data=[SimpleNamespace(embedding=[0.1, 0.2]) for _ in kwargs.get("input", [])])

    client = SimpleNamespace(
        chat=SimpleNamespace(completions=SimpleNamespace(create=create_chat)),
        embeddings=SimpleNamespace(create=create_embeddings),
    )

    reply = OpenAICompatibleChat(client, "m", {"temperature": 0}).complete([Message("user", "ping")])
    adapter = OpenAICompatibleEmbedder(client, "e", {"document_prefix": "doc: ", "query_prefix": "query: ", "user": "test-user"})
    doc_vectors = adapter.embed_documents(["text1", "text2"])
    query_vector = adapter.embed_query("question")

    assert reply == "pong"
    assert doc_vectors == [[0.1, 0.2], [0.1, 0.2]]
    assert calls[0] == {"model": "e", "input": ["doc: text1", "doc: text2"], "user": "test-user"}
    assert query_vector == [0.1, 0.2]
    assert calls[1] == {"model": "e", "input": ["query: question"], "user": "test-user"}


def test_fake_embedder_supports_prefixes():
    from talk_to_rag.llm.adapters.fake import FakeEmbedder

    raw = FakeEmbedder(dimensions=4)
    prefixed = FakeEmbedder(dimensions=4, document_prefix="doc: ", query_prefix="q: ")

    assert prefixed.embed_documents(["hello"]) == [raw.embed_documents(["doc: hello"])[0]]
    assert prefixed.embed_query("hello") == raw.embed_query("q: hello")


def test_base_url_can_be_overridden_from_the_environment(monkeypatch):
    provider = {"base_url": "http://localhost:11434/v1", "base_url_env": "OLLAMA_BASE_URL"}
    monkeypatch.delenv("OLLAMA_BASE_URL", raising=False)
    assert base_url(provider) == "http://localhost:11434/v1"

    monkeypatch.setenv("OLLAMA_BASE_URL", "http://host.docker.internal:11434/v1")
    assert base_url(provider) == "http://host.docker.internal:11434/v1"


def test_openai_compatible_adapter_hides_library_errors():
    from openai import OpenAIError

    def fail(**kwargs):
        raise OpenAIError("model not found")

    client = SimpleNamespace(chat=SimpleNamespace(completions=SimpleNamespace(create=fail)))

    with pytest.raises(ModelError, match="m: model not found"):
        OpenAICompatibleChat(client, "m", {}).complete([Message("user", "hi")])


def test_provider_that_needs_a_key_fails_clearly_without_one(monkeypatch):
    monkeypatch.delenv("LOCAL_SERVER_API_KEY", raising=False)
    provider = {"base_url": "http://localhost:8080/v1", "api_key_env": "LOCAL_SERVER_API_KEY"}

    with pytest.raises(ModelError, match="LOCAL_SERVER_API_KEY"):
        build_chat(provider, {"name": "some-model"})
