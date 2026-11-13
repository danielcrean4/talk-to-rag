# talk-to-rag

Search your own documents and see where the answer is: which source, which page, which section. Everything runs on your machine, including the models.

Upload PDFs, Markdown or text files. Ask a question by typing or by voice. You get the matching passages grouped by source, each with its page and section, and you can open the file at that page. A short summary that cites those passages is written after them.

**Status:** working towards 1.0. See the [roadmap](docs/roadmap.md).

## What it does

- **Ingestion.** Files are parsed, split into passages that keep their page number, printed page label and section heading, and embedded in the background.
- **Hybrid search.** Vector search and Postgres full-text search, merged with Reciprocal Rank Fusion. Notebooks and tags narrow a search to some of your sources.
- **Cited summaries.** A local model summarises the passages found and cites them by number. A citation that matches no passage is dropped.
- **Voice.** The mic is transcribed by a local Whisper model. The summary is read aloud by a local Kokoro model. Audio files are deleted once they have been used.
- **Concept graph and practice** (phase two, in progress). Concepts linked by prerequisites, flashcards written from linked passages, and a mastery level computed from your answers.
- **Audit log.** Every model call is recorded with its prompt and reply. A read-only MCP server lets an outside model inspect that log without seeing your documents' text.

No hosted model API is used anywhere.

## Run it

You need:

- [Docker](https://docs.docker.com/get-docker/) with Compose
- [Ollama](https://ollama.com), running on the host
- `make` and `curl`

Pull the models:

```bash
ollama pull nomic-embed-text      # embeddings
ollama pull gemma3:1b             # query rewriting
ollama pull hf.co/mistralai/Ministral-3-3B-Instruct-2512-GGUF:Q4_K_M   # summaries, concept graph, flashcards
```

Start the stack and load the sample documents:

```bash
make up        # build and start Postgres, the AI service and the API
make sample    # load four public-domain texts
```

Open http://127.0.0.1:5000 and try "who has the power of impeachment".

`make down` stops the stack and keeps your data in Docker volumes.

### One user or accounts

By default there is no sign-in. The app runs for one local user, which suits a tool on your own machine: every port binds to 127.0.0.1, so only this machine can reach it.

To have separate accounts with passwords, start it with `TALK_TO_RAG_SINGLE_USER=false make up`. Each account then sees only its own sources, notebooks and history.

Use `127.0.0.1`, not `localhost`. On macOS, `localhost` can resolve to `::1`, where the AirPlay Receiver listens on port 5000.

## How it is built

| Part | Technology | Role |
|---|---|---|
| `services/api` | ASP.NET Core (.NET 10), EF Core | Owns all state: accounts, sources, notebooks, tags, search history, the concept graph. Runs the hybrid search SQL and serves the web app. |
| `services/ai` | Python, FastAPI | Stateless. Parses, chunks, embeds, rewrites queries, writes summaries, transcribes and speaks text. Never touches the database. |
| Database | Postgres 17 with pgvector | Vectors (HNSW), full-text index, and everything else. |
| `services/mcp` | Python, MCP SDK | Read-only access to the audit log for an outside model. |
| `web/` | Angular 22 | The web app. The API image builds it, so running the stack needs no Node. |
| Models | Ollama on the host | Containers reach it at `host.docker.internal:11434`. |

The two services share a volume for files and pass each other keys, not bytes. Every port binds to 127.0.0.1 only.

[docs/architecture.md](docs/architecture.md) has the diagrams, the endpoint list and the database schema.

## Develop

```bash
make test      # Python, .NET and MCP server tests
make health    # both services answer
make web       # dev server for the Angular app on http://127.0.0.1:4201, reloads on change (needs Node)
make test-web  # Angular unit tests
make lint      # Python lint and formatting (Ruff), Angular formatting (Prettier)
make test-e2e  # browser tests (Playwright) against the app a throwaway stack serves; needs Node, Docker and Ollama
```

The Python tests need [uv](https://docs.astral.sh/uv/). The .NET commands use a local .NET 10 SDK if you have one and the SDK Docker image if you don't.

- API description: http://127.0.0.1:5000/openapi/v1.json
- AI service docs: http://127.0.0.1:8000/docs
- Measure retrieval on the sample documents: `make sample`, then `cd services/ai && uv run python -m talk_to_rag.evaluation` (add `--email E --password P` when accounts are on). `evaluation/README.md` has the question format and recorded scores.

Models are configured in `services/ai/config/models.toml`. To use a different model or a model server on another machine, copy `models.local.example.toml` to `models.local.toml`.

## Inspect the models with the audit log

Each request to the AI service is stored with the prompts and replies of the model calls inside it. The `audit` database schema shows a redacted copy: passage text is replaced by a placeholder that gives its length, and the source, page and section are given in its place.

`.mcp.json` registers the MCP server for clients that read it, such as Claude Code. Its tools are `describe`, `query` (one `SELECT` per call) and `get_ai_call`. It connects with a database role that can read the `audit` schema and nothing else.

## Roadmap

- **1.0**, phase one: the search app, with the Angular UI and voice in and out. [Milestone](https://github.com/danielcrean4/talk-to-rag/milestone/1).
- **2.0**, phase two: the learning tool. [Milestone](https://github.com/danielcrean4/talk-to-rag/milestone/2).

[docs/roadmap.md](docs/roadmap.md) explains the split and how issues are written.

## License

MIT. Third-party tools, libraries and models are credited in [CREDITS.md](CREDITS.md).
