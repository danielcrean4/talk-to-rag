# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A local-first, sources-first RAG app: upload PDFs/Markdown/text, search them with hybrid retrieval, and get passages with page/section citations, plus an optional grounded summary. It is a polyglot monorepo with two services, Postgres and a read-only audit server (`services/mcp`, see Audit log):

- `services/api`: ASP.NET Core (.NET 10). It owns all state: users and JWT auth, notebooks, sources, tags, conversations, chunks and hybrid search SQL. It also serves the web UI.
- `services/ai`: Python FastAPI (uv). This service keeps no state. It parses, chunks, embeds, expands queries, writes cited answers and flashcards, and transcribes and speaks text. It never touches the database.
- Postgres 17 with pgvector (`pgvector/pgvector:pg17`).
- Ollama runs **natively on the host**, not in Docker, because Docker on macOS has no GPU access. Containers reach it at `host.docker.internal:11434`. Required models: `nomic-embed-text` (embeddings), `gemma3:1b` (query rewriting), and `hf.co/mistralai/Ministral-3-3B-Instruct-2512-GGUF:Q4_K_M` (`local-3b`: summaries, concept proposals, flashcards).

A sign-in token is only ever read from the `Authorization` header. A viewer tab cannot send headers, so `POST /api/sources/{id}/file-link` returns a URL for `GET /api/sources/{id}/file` with a token in `?access=` that opens that one file for 5 minutes (`Jwt:FileLinkMinutes`). It is signed with the same key for a different audience, so neither kind of token works as the other; the `SourceFileLink` scheme in `Api/Security` checks it names the requested source.

`docs/architecture.md` has the process diagram, sequence diagrams, endpoint list and database schema. Update it in the same change when a route, table or flow changes. `docs/roadmap.md` and the GitHub milestones say what is planned.

## Commands

Run these from the repo root:

```bash
make up        # write a random auditor password to .env if it has none, then docker compose up -d --build --wait (postgres, ai, api)
make migrate   # dotnet ef database update against the compose Postgres; only needed while writing a migration, since the API applies migrations when it starts under compose (`Database:MigrateOnStartup`)
make health    # curl both /health endpoints
make test      # test-ai + test-api + test-mcp
make down
make lint      # Ruff check and format check for services/ai and services/mcp, Prettier check for web/ (fix with `uvx ruff format`, `npm run format`)
make sample    # load the public-domain texts in sample/
make eval      # score retrieval on the sample texts in a throwaway stack (project talk-to-rag-eval, API on 5001), then remove it; fails when a sample fails to ingest or a score is under evaluation/minimum_scores.json, and CI runs it
```

Python AI service (`cd services/ai`):

```bash
uv run pytest -q                                   # all tests
uv run pytest tests/test_ingestion.py -q           # one file
uv run pytest tests/test_llm.py::test_name -q      # one test
uv run uvicorn talk_to_rag.api.app:create_app --factory --reload --port 8000
uv run python -m talk_to_rag.llm "question" [--model local-4b]   # ask a configured chat model
uv run python -m talk_to_rag.storage write|read <key> [file]     # poke blob storage
uv run python -m talk_to_rag.evaluation [--mode semantic --expand]  # hit@k/MRR vs the running API, using evaluation/sample_questions.json (run `make sample` first; scores in evaluation/README.md); add --email E --password P when accounts are on
```

The models config is loaded from `$TALK_TO_RAG_CONFIG_DIR` if set. Otherwise it uses `./config` when that folder has a `models.toml`, and falls back to `services/ai/config`. Tests rely on the `./config` step: they `chdir` into a temp directory that has its own config.

.NET API (`cd services/api`, solution `TalkToRag.slnx`). `make migrate` and `make test-api` use the local SDK when `dotnet` is on PATH and the SDK image otherwise. To run other commands without the SDK, use the image from the repo root, for example `docker run --rm -v "$PWD":/src -w /src/services/api mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/TalkToRag.UnitTests`. Integration tests in a container also need the Docker socket mounted. The bare commands below are for machines that have the SDK:

```bash
dotnet test tests/TalkToRag.UnitTests
dotnet test tests/TalkToRag.UnitTests --filter "FullyQualifiedName~SearchServiceTests"
dotnet test tests/TalkToRag.IntegrationTests       # needs a running Docker daemon (Testcontainers pgvector)
dotnet tool restore && dotnet ef migrations add <Name> \
  --project src/TalkToRag.Infrastructure --startup-project src/TalkToRag.Api
```

URLs:
- Web UI and API: http://127.0.0.1:5000 (OpenAPI at `/openapi/v1.json` in Development).
- AI service docs: http://127.0.0.1:8000/docs.

All ports bind to 127.0.0.1 only. Use `127.0.0.1`, not `localhost`: on macOS, `localhost` resolves to `::1`, where the AirPlay Receiver listens on port 5000 and returns 403.

## Architecture

### Blob-key handoff between services

The two services share a Docker volume mounted at `/var/talk-to-rag/blobs`. Bytes never travel over HTTP between them. The API writes the file through `IBlobStorage` (`LocalBlobStorage`) under a key like `sources/{sourceId}.pdf` and sends the AI service only the key. Python reads it through its own `BlobStorage` / `LocalBlobStorage`, with the root set by `TALK_TO_RAG_BLOB_ROOT`. Keep the C# and Python storage implementations' key semantics in sync.

### Ingestion flow (async)

1. `POST /api/sources` (multipart) writes the blob, inserts a `Source` row (status Pending) and enqueues the source ID on `ISourceIngestionQueue`. That queue is a `ChannelSourceIngestionQueue`, held in memory; on startup the worker re-queues every source still Pending or Processing.
2. `SourceIngestionWorker` (a `BackgroundService`) dequeues the ID and calls `IAiClient` / `HttpAiClient`, which posts to the AI service's `POST /v1/sources/ingest`.
3. Python picks a parser by content type (`ingestion.parser_for`: PDF, markdown, text; PDF text comes from PDFium, page labels from pypdf), chunks the pages (`paragraph_chunker`) and embeds them in batches of 32. It returns chunks with `page_number` (the 1-based physical page), `page_label` (the printed label, such as `xii`), `section` (a heading breadcrumb) and `embedding` (768-dimensional), plus `embedding_model`, the id the provider serves the embedding model under (`nomic-embed-text` for `local-embed`).
4. The worker replaces the source's `source_chunks` in one transaction (old rows deleted, new rows inserted), storing that `embedding_model` on each, and marks the source Ready, or Failed with a cleaned-up `FailureReason`. A response without `embedding_model` fails the job. A source that still has chunks from an earlier ingestion goes back to Ready with the reason set, so a failed re-ingest leaves it searchable with its old passages. Each job is wrapped in a try/catch so the host stays up. `POST /api/sources/{id}/retry` puts a Failed or Ready source back on the queue (409 while it is Pending or Processing); the source is out of search until the job finishes.

A source may carry a free-text `License` and a `SourceUrl` (where it came from; only absolute http or https URLs are accepted). The upload takes them as optional form fields and `PATCH /api/sources/{id}` changes them (null keeps a field, empty clears it); `SourceService` trims and validates both, and the PATCH writes them with a targeted update so it cannot overwrite a status the worker set.

`DELETE /api/sources/{id}` deletes the row and then the blob. Database cascades remove its chunks, citations, notebook and tag links and concept passages; questions keep their text with `source_id` set null. The worker writes status with targeted updates and skips a source deleted mid-ingestion.

### Time limits

`HttpAiClient` gives each call to the AI service its own limit through a linked `CancellationTokenSource`; the `HttpClient` itself has none. Ingestion gets `AiService:IngestTimeoutSeconds` (1800). Every other call is made while someone waits and gets `AiService:InteractiveTimeoutSeconds` (180), including the whole of a streamed summary. Running out throws `AiServiceUnavailableException` (503, "The model did not answer in time."), or, once a summary stream has started, ends it with a `data: {"error": …}` event.

### Search flow

- `POST /api/search` → `SearchService` first calls the AI service's `POST /v1/rag/expand-query`. Without expansion that returns the embedded query; with `expand: true`, gemma rewrites the question into 2–3 queries and each one is embedded.
- `SearchRepository` then runs one parameterized `NpgsqlCommand` on the EF connection, inside a transaction:
  - `SET LOCAL hnsw.iterative_scan = relaxed_order`, so narrow notebook/tag filters don't run out of HNSW candidates.
  - Semantic top 50 by cosine distance (`<=>`) for each expanded query, dropping hits farther than `Search:MaxCosineDistance` (0.6) or the request's `maxCosineDistance`.
  - Keyword top-N on the generated `search_vector` column: a passage matches on any word of the question and `ts_rank` orders them. A query with quotes or a leading minus goes through `websearch_to_tsquery` unchanged.
  - The lists are merged with Reciprocal Rank Fusion (k=60), and snippets come from `ts_headline`.
  - Vectors are passed as text parameters `@embedding_0`, `@embedding_1`, … and cast with `::vector`.
  - Every query filters on the current user, `Ready` status and the optional notebook/tag/source scope. Each kind of scope given narrows the search: a source must be in one of the notebooks, carry one of the tags and be one of the source ids, for whichever of the three the request sends.
- Hits are grouped by source. The query is saved as a user `Message` in a `Conversation`, with one `MessageCitation` per hit that copies the passage text and location.
- `POST /api/search/{conversationId}/summary` sends numbered passages to the AI service's `POST /v1/rag/answer`, and `GET /api/search/{conversationId}/summary/stream` does the same through `POST /v1/rag/answer/stream` as server-sent events. Both use `local-3b`; the 1B model copied passages back instead of summarising. The model cites with `[n]`. Python parses those markers and drops any number that matches no passage, then the API saves the assistant `Message` and its `MessageCitation`s. Citation numbers are global ranks across all passage hits, not per-source indexes.

### Concept graph

`Concept`s are things to understand; `ConceptPrerequisite` edges ("understand B before A") make them a graph, kept free of loops by `ConceptService`. Concepts and edges carry a `ProposalState`: the model proposes (`POST /api/concepts/{id}/propose-prerequisites` → AI service `POST /v1/concepts/propose-prerequisites`, model `local-3b`), the user accepts, rejects, splits or relinks. Rejected rows are kept so they aren't proposed again. `ConceptPassage` links a concept to a place in a source by source, page and section, never by chunk id, because chunks get new ids on re-ingest. Passage suggestions search through `ISearchRepository` directly so they don't create a `Conversation`.

### Practice and mastery

A `Question` belongs to a concept and may carry the place in a source it came from. `POST /api/concepts/{id}/questions/generate` has `local-3b` write flashcards from the full text of the concept's linked passages. Every answer, from flashcards, a quiz or an outside game, is an `Attempt` (`POST /api/attempts`, with a `platform` name and a score from 0 to 1; a game without a stored question sends a `conceptId`). Mastery is never stored: `MasteryDto.From` derives New / Learning / Solid from a concept's attempts, and every `ConceptDto` carries it.

The review schedule is not stored either. `Fsrs` (`Application/Practice/Fsrs.cs`, FSRS-5 with its default parameters and a target retention of 0.9, ported from py-fsrs without learning steps or fuzzing) replays a question's own attempts in time order to give its `dueAtUtc` (null until first answered). A score below 0.25 is Again, below 0.75 Hard, otherwise Good, so the marks Missed, Almost and Got it are Again, Hard and Good. Concept-only attempts from games do not schedule any question. `GET /api/practice/due` lists every question due now, earliest first, then at most 10 never answered, with due and new counts per concept; "now" comes from `TimeProvider`.

### Audit log

Every exchange with the AI service is stored in `ai_calls` by `AiAuditHandler`, a `DelegatingHandler` on the AI `HttpClient`, so no call site can forget to log. A row belongs to the signed-in user; for ingestion, which runs in the background worker, the worker sets the source's owner with `BackgroundAuditUser` (an `AsyncLocal` scope) around the call, and the handler uses it when there is no signed-in user. Python adds a `trace` list to its responses (`tracing.py`: one entry per model call, with the messages sent and the completion), and the streamed summary sends it as a `data: {"trace": …}` line that `SearchService` skips, before the `done` event or, when the model fails, before the `error` event. Each row keeps the exchange twice: as sent, and redacted by `AuditJson`. Redaction replaces passage text with a placeholder giving its length. It does the same to earlier answers sent as context for a follow-up search, and to any run of 40 or more letters a model copied from either into its reply, whatever happened to the spacing, punctuation or capitals.

The `audit` schema is what an outside reader may see: views over the redacted columns and over sources, chunks, messages, citations, concepts, questions and attempts, none of which expose source text. `audit.sources` does show each source's license and origin URL, which describe the source rather than quote it. The `talk_to_rag_auditor` role can only `SELECT` from that schema. Text a model wrote from passages (assistant messages, generated questions) appears in the table views by length only; read it, redacted, through `audit.ai_calls`, `audit.model_calls` and `audit.answers`. When a table gains a column an auditor should see, add it to the view in a new migration.

`services/mcp` is a small MCP server (stdio, its own uv project) that gives a model outside the app, such as Claude, read access to the `audit` schema and nothing else. It is registered in `.mcp.json` and has three tools: `describe`, `query` (one `SELECT`, capped rows and cells) and `get_ai_call`. `audit.py` opens a connection per call as `talk_to_rag_auditor`, refuses a login that can read the tables, and runs the statement in a read-only transaction that it rolls back. It must stay read-only: no tool may write to the app or call a model. The migrations create the role without a password. The API gives it the one in `Audit:AuditorPassword` after every migration (an EF `UseSeeding` step in `AddInfrastructure`, so `make migrate` does it too); compose and `make migrate` take it from `TALK_TO_RAG_AUDITOR_PASSWORD`, which `make up` writes to the gitignored `.env`. The server reads the same variable (`.mcp.json` starts it with `--env-file .env`), and `TALK_TO_RAG_AUDIT_DSN` overrides the whole connection string. Its database tests need Postgres with the API's migrations applied: they are skipped when it cannot be reached and fail instead when `CI` is set. The CI `test` job starts a pgvector service container and runs `make migrate` against it before `make test`.

### .NET layering (Clean Architecture)

- **Domain** holds entities and enums.
- **Application** holds the feature modules (`Auth`, `Notebooks`, `Sources`, `Tags`, `Search`, `Conversations`, `Speech`, `Concepts`, `Practice`). Each module has `*Contracts.cs` (DTOs), `I*Service` and `*Service`. Ports live in `Common/Interfaces` (repositories, `IAiClient`, `IBlobStorage`, `ICurrentUser`, `ITokenGenerator`), and errors are in `Common/Exceptions/ApplicationErrors.cs`.
- **Infrastructure** implements the ports with EF Core (`TalkToRagDbContext`, `Persistence/Configurations`, `Migrations`), Npgsql/pgvector, JWT, the channel queue and the worker.
- **Api** holds thin controllers, `ApplicationExceptionHandler`, which maps application errors to ProblemDetails, and `ClaimsCurrentUser`.
- Signing key: `Jwt:SigningKey`, or else a random key the API writes once to `Jwt:SigningKeyFile` and reads back on later starts (compose keeps it in the API-only `keys` volume; `JWT_SIGNING_KEY` overrides it). With neither, the API refuses to start and says what to set.
- Sign-in: with `Auth:SingleUser` set (the compose default, `TALK_TO_RAG_SINGLE_USER`), `GET /api/auth/session` hands every client a token for one built-in user and `register`/`login` answer 404. Both UIs call it on load and skip the sign-in form. Everything after that is the same bearer-token path as with accounts, so no other code knows the mode. The setting defaults to off in code, which is what the tests use.
- Ownership rule: `Notebook`, `Source`, `Tag` and `Conversation` carry `UserId`. Accessing another user's resource returns **404**, not 403. A duplicate name for the same user returns 409.
- The pgvector extension is declared in EF via `HasPostgresExtension("vector")`. Schema changes go through EF migrations in `TalkToRag.Infrastructure/Persistence/Migrations`.

### Web app (`web/`)

The only web UI is an Angular 22 app (standalone components, signals, zoneless). It is served by the API:

- `services/api/Dockerfile` is built from the repo root (`compose.yaml` sets the context; the root `.dockerignore` sends only `services/api/src` and `web/`). Its Node stage runs the production `ng build` and copies `dist/web/browser` into the image's `wwwroot/`, so `make up` needs no Node on the host.
- `Program.cs` serves those files with `UseStaticFiles`. A GET that no endpoint matched, outside `/api` and without a file extension, is answered with `index.html` (sent `no-cache`), so the app's routes work as links. An unknown `/api/...` path stays a 404 and a wrong method a 405.
- The repo has no `wwwroot/`. The API run outside Docker serves only the API, and the Angular dev server is the UI.

The app has sign-in (and registration when accounts are on) and six routed screens: Sources (`/sources`, `src/app/sources`: upload, status, retry or process again, delete, license and origin link edited per source, notebooks, tags: create, delete, put on a source, filter the list; the page is split into `NotebookBar`, `TagEditor` and one `SourceCard` per source), Search (`/search`, `src/app/search`: notebook and tag scope, results grouped by source, streamed summary, hold-to-talk mic, read aloud, settings kept in the browser), History (`/history` and `/history/:id`, `src/app/history`), the concept graph (`/`, `src/app/graph`: each tile shows the concept's mastery level and how many cards are due; `ConceptCanvas` draws the graph and `ConceptPanel` shows and edits the selected concept), Practice (`/concepts/:id/practice`, `src/app/practice`: steps through a concept's flashcards, due ones first, posts each answer as an attempt with platform `flashcards`, and offers to generate cards when there are none) and Review (`/review`, `src/app/practice/review-page.ts`: steps through the cards `GET /api/practice/due` returns). Both card screens show each card with the `Flashcard` component. The current search lives in the `SearchSession` service, so it survives moving between screens. `src/app/api/` is the typed API client, one injectable per area (`SourcesApi`, `NotebooksApi`, `TagsApi`, `SearchApi`, `ConversationsApi`, `SpeechApi`, `AuthApi`, `ConceptsApi`, `PracticeApi`) with that area's types, `activity.ts` is the `Activity` service a page provides to share the running action's label and last error with its child components, `auth.ts` holds the token, signed-in user, interceptor and guard, `passages.ts` holds the pure helpers for snippets, `[n]` citations, page/section labels and file link fragments, `source-file-link.ts` is the `appSourceFile` link directive (it opens a tab on click, then sends it to a fresh file link), `tags.ts` holds the tag filter helpers, `graph-layout.ts` places concepts in rows by prerequisite depth, and `practice/practice-deck.ts` holds the card order, the due checks and the scores for each mark.

The mic records with `MediaRecorder` and posts to `POST /api/speech/transcribe`, which hands the audio to the AI service's `POST /v1/speech/transcribe` (faster-whisper) by blob key and deletes it afterwards. While the button is held, the app posts the audio recorded so far to the same route, one request at a time, and shows the words in the question box; only the transcription of the whole recording, made on release, starts the search. Each of those requests is an `ai_calls` row. Read aloud posts the summary to `POST /api/speech/synthesize`. The API picks a blob key under `audio/{userId}/`, the AI service's `POST /v1/speech/synthesize` writes a WAV there with Kokoro-82M (onnxruntime, misaki for phonemes, no espeak-ng; selected by `TALK_TO_RAG_TTS`), and the API streams it back and deletes the blob once the response is sent. The text sent is withheld by length in the redacted audit copy. The app keeps the audio so it can resume or replay it, and falls back to the browser `speechSynthesis` only when the API answers 503, saying so next to the button.

Commands, all needing Node on the host: `make web` runs the dev server on **4201**, which proxies `/api` to `127.0.0.1:5000` and reloads on change. `make test-web` runs the Vitest specs. `make test-e2e` runs the Playwright tests in `web/e2e` (`*.e2e.ts`): `web/e2e/run.sh` starts compose project `talk-to-rag-e2e` (API on 5001, shared with `make eval`, so run one at a time), the tests drive the app that API serves at `http://127.0.0.1:5001`, and the stack is removed afterwards. The tests upload their own fixture and need only the embedding model; that stack is single-user, so registration is covered by the Vitest specs instead. None of the three is part of `make test`; CI runs the unit and browser tests in its `web` job.

### Python model layer

- `config/models.toml` maps logical model names (`local-small`, `local-3b`, `local-4b`, `local-phi`, `local-llama`, `local-embed`, `fake-chat`, `fake-embed`) to providers. Adapters are `openai_compatible` and `fake`.
- Entries in `config/models.local.toml` (gitignored) add to or replace the committed config, section by section.
- Model choice comes from the explicit argument, then the `TALK_TO_RAG_CHAT` / `TALK_TO_RAG_EMBED` environment variables, then `[defaults]`.
- `TALK_TO_RAG_MODELS` points at a single config file in place of the layered pair.
- A provider's `base_url_env` overrides its URL; compose sets `OLLAMA_BASE_URL`.
- A provider's `timeout_seconds` (default 120; for a stream, the longest wait between two pieces) and `max_retries` (default 0) go to the OpenAI client in place of the library's 600 seconds and 2 retries. The timeout stays under the API's interactive limit, so a stalled Ollama comes back as a 503 that names the model.
- nomic embeddings use the `search_document: ` / `search_query: ` prefixes, so call `embed_documents` and `embed_query` correctly.
- Tests use the `fake` adapter and `monkeypatch`, so they need neither Ollama nor Docker.

## Conventions

- Add new third-party dependencies, tools and models to `CREDITS.md`.
- Private documents (`*.pdf`, `*.epub`, `data/`, `library/`, `sources.local.yaml`) are gitignored. Only `services/ai/tests/fixtures/*.pdf` is tracked.
- `.agents/skills/deslop/SKILL.md` describes the cleanup pass: remove unnecessary comments, unusual defensive try/catch blocks and deep nesting, keep behavior unchanged, and hold docs and issues to claims that can be checked.
- The roadmap is two GitHub milestones, 1.0 (bugs, released as `1.0.x`) and 1.1; `docs/roadmap.md` explains the split and how issues are written. File new work and bugs as issues on one of them.
- Work happens on short branches, one per slice, merged to `main`.
- Local models only. Never add a hosted or paid model API, not even as an option.
