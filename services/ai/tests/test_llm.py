from pathlib import Path
from types import SimpleNamespace

import pytest

from talk_to_rag.llm import ChatModel, Embedder, Message, ModelError, chat_model, embedder
from talk_to_rag.llm.adapters.openai_compatible import OpenAICompatibleChat, OpenAICompatibleEmbedder, base_url
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
        '[defaults]\nchat = "private"\n'
        '[models.private]\nprovider = "offline"\nkind = "chat"\nname = "fake"\n'
    )
    monkeypatch.chdir(tmp_path)
    monkeypatch.delenv("TALK_TO_RAG_MODELS", raising=False)

    config = load()

    assert config.defaults == {"chat": "private", "embed": "embed"}
    assert set(config.models) == {"chat", "embed", "private"}


def test_factory_builds_objects_that_satisfy_the_contracts():
    config = parse(config_data())
    chat, embed = chat_model(config=config), embedder(config=config)

    assert isinstance(chat, ChatModel) and isinstance(embed, Embedder)
    assert chat.complete([Message("user", "hi")]) == "(fake reply to: hi)"
    assert embed.embed(["a", "b"]) == embed.embed(["a", "b"])


def test_model_choice_is_argument_then_environment_then_default(monkeypatch):
    config = parse(config_data())
    assert resolve_name("chat", None, config) == "chat"

    monkeypatch.setenv("TALK_TO_RAG_CHAT", "from-env")
    assert resolve_name("chat", None, config) == "from-env"
    assert resolve_name("chat", "from-arg", config) == "from-arg"
    with pytest.raises(ConfigError, match="Unknown model 'from-env'"):
        chat_model(config=config)


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
    calls = {}

    def create_chat(**kwargs):
        calls["chat"] = kwargs
        return SimpleNamespace(choices=[SimpleNamespace(message=SimpleNamespace(content="pong"))])

    def create_embeddings(**kwargs):
        calls["embed"] = kwargs
        return SimpleNamespace(data=[SimpleNamespace(embedding=[0.1, 0.2])])

    client = SimpleNamespace(
        chat=SimpleNamespace(completions=SimpleNamespace(create=create_chat)),
        embeddings=SimpleNamespace(create=create_embeddings),
    )

    reply = OpenAICompatibleChat(client, "m", {"temperature": 0}).complete([Message("user", "ping")])
    vectors = OpenAICompatibleEmbedder(client, "e", {}).embed(["text"])

    assert reply == "pong"
    assert calls["chat"] == {"model": "m", "messages": [{"role": "user", "content": "ping"}], "temperature": 0}
    assert vectors == [[0.1, 0.2]] and calls["embed"] == {"model": "e", "input": ["text"]}


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
