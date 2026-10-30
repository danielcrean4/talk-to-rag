"""Ask the configured chat model one question: python -m talk_to_rag.llm "question"."""

import argparse
import sys

from talk_to_rag.llm import ConfigError, Message, ModelError, chat_model
from talk_to_rag.llm import config as models_config
from talk_to_rag.llm.factory import resolve_name


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("question")
    parser.add_argument("--model", help="a model name from the config; defaults to [defaults].chat")
    args = parser.parse_args()

    try:
        config = models_config.load()
        name = resolve_name("chat", args.model, config)
        reply = chat_model(name, config).complete([Message("user", args.question)])
    except (ConfigError, ModelError) as error:
        sys.exit(f"error: {error}")
    print(f"[{name}] {reply}")


if __name__ == "__main__":
    main()
