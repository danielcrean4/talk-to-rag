"""Load and validate the models config (config/models.toml)."""

import os
import tomllib
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from talk_to_rag.llm.adapters import ADAPTERS

CONFIG_ENV = "TALK_TO_RAG_MODELS"
BASE_PATH = Path("config/models.toml")
# Gitignored entries for private endpoints, layered over the committed file.
LOCAL_PATH = Path("config/models.local.toml")
KINDS = ("chat", "embed")
SECTIONS = ("providers", "models", "defaults")


class ConfigError(ValueError):
    pass


@dataclass(frozen=True)
class ModelsConfig:
    providers: dict[str, dict[str, Any]]
    models: dict[str, dict[str, Any]]
    defaults: dict[str, str]


def load(path: str | Path | None = None) -> ModelsConfig:
    """Load one file if a path is given or set in the environment, else the base plus local layer."""
    path = path or os.environ.get(CONFIG_ENV)
    if path is not None:
        return parse(_read(Path(path)), source=str(path))

    if not BASE_PATH.exists():
        raise ConfigError(f"No models config found; create {BASE_PATH} or set {CONFIG_ENV}")
    data = _read(BASE_PATH)
    source = str(BASE_PATH)
    if LOCAL_PATH.exists():
        data = merge(data, _read(LOCAL_PATH))
        source += f" + {LOCAL_PATH}"
    return parse(data, source=source)


def merge(base: dict[str, Any], overlay: dict[str, Any]) -> dict[str, Any]:
    """Overlay entries add to or replace base entries, section by section."""
    return {section: {**base.get(section, {}), **overlay.get(section, {})} for section in SECTIONS}


def parse(data: dict[str, Any], source: str = "<config>") -> ModelsConfig:
    providers = data.get("providers", {})
    models = data.get("models", {})
    defaults = data.get("defaults", {})

    for name, provider in providers.items():
        adapter = provider.get("adapter")
        if adapter not in ADAPTERS:
            raise ConfigError(
                f"{source}: provider '{name}' has unknown adapter {adapter!r}; "
                f"choose one of {sorted(ADAPTERS)}"
            )

    for name, model in models.items():
        if model.get("provider") not in providers:
            raise ConfigError(f"{source}: model '{name}' uses unknown provider {model.get('provider')!r}")
        if model.get("kind") not in KINDS:
            raise ConfigError(f"{source}: model '{name}' needs kind = one of {KINDS}")
        if not isinstance(model.get("name"), str):
            raise ConfigError(f"{source}: model '{name}' needs a name, the id the server knows it by")

    for kind in KINDS:
        default = defaults.get(kind)
        if default not in models:
            raise ConfigError(f"{source}: defaults.{kind} must name a model in [models]")
        if models[default]["kind"] != kind:
            raise ConfigError(f"{source}: defaults.{kind} points at '{default}', which is a {models[default]['kind']} model")

    return ModelsConfig(providers=providers, models=models, defaults=defaults)


def _read(path: Path) -> dict[str, Any]:
    with path.open("rb") as f:
        return tomllib.load(f)
