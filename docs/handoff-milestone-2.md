# Handoff: Milestone 2 — source upload and ingestion

You're picking up talk-to-rag after Milestone 1 (merged to `main` as `027a6ce`, PR #1). This file is everything you need to build Milestone 2. Read it with [`docs/architecture.md`](architecture.md), [`CREDITS.md`](../CREDITS.md) and the [README](../README.md).

## Who this is for and how to work

- **Dan is learning RAG by building this.** Don't just produce code: for each decision below (chunk size, overlap, prefixes, page handling), write one or two plain-English sentences on *why* in the PR description, and point out trade-offs he should notice. Keep jargon explained.
- **Small, reviewable steps.** Work on a branch `milestone-2` off `main`. One commit per step in the order below, each with passing tests. Stop and summarise after the plan and after each step so Dan can review.
- **Don't commit to `main`, don't push without being asked, don't merge.** Open a PR only when Dan asks.
- **Credit everything.** Any new library, tool, model, test fixture or text source gets an entry in `CREDITS.md` in the same commit (version, licence, where used, attribution). Prefer permissive licences; **avoid AGPL/GPL dependencies** (e.g. PyMuPDF is AGPL — don't use it).
- **Never touch** `services/ai/config/models.local.toml` (private, gitignored) or put private hostnames in committed files.

## The product, in one paragraph

A hands-free study tool: Dan uploads open-licensed books and notes, and the app tells him **which sources cover a topic and where** (source, page, section, snippet). Retrieval comes first; a model-written summary is optional. So in this milestone, **preserving locations (page numbers, headings) matters as much as the text itself** — every chunk must be able to say where it came from.

## Environment

- MacBook Air M1, **8 GB RAM**, ~12 GB free disk. Keep models small.
- **Ollama runs natively on the Mac** (Docker on macOS has no GPU). Models present: `nomic-embed-text` (768-dim embeddings), `gemma3:1b`. Containers reach Ollama at `http://host.docker.internal:11434/v1` via `OLLAMA_BASE_URL`.
- Homebrew has no prebuilt bottles for this macOS (14): **don't `brew install` things that compile from source** (it tried to build .NET for hours). Use Docker images, uv, or NuGet instead.
- Commands (repo root): `make up` (build + start Postgres/pgvector, AI service, API), `make migrate`, `make health`, `make test`, `make down`.

## What exists (Milestone 1)

**Python AI service — `services/ai`** (uv, FastAPI, Python ≥3.11; image uses 3.13)
- `talk_to_rag/llm/`: `contracts.py` (`ChatModel`, `Embedder` Protocols, `Message`, `ModelError`), `config.py` (TOML loader; `config/models.toml` + gitignored `models.local.toml` overlay), `factory.py` (`chat_model()`, `embedder()`), `adapters/openai_compatible.py` (Ollama via the OpenAI client; `base_url_env` override), `adapters/fake.py` (offline fakes for tests).
- `talk_to_rag/storage/`: `BlobStorage` Protocol (`read_bytes`, `write_bytes`, `exists`), `LocalBlobStorage` rooted at `TALK_TO_RAG_BLOB_ROOT` (default `/var/talk-to-rag/blobs`), rejects keys that escape the root, atomic writes.
- `talk_to_rag/api/`: `create_app()` factory, one router per capability, `GET /health`, typed Pydantic schemas in `schemas.py`.
- Pattern to follow everywhere: **`contracts.py` (Protocols) + `adapters/`**, callers import only from the package root. Tests in `services/ai/tests` (23 passing).

**.NET API — `services/api`** (.NET 10, `TalkToRag.slnx`, Clean Architecture)
- `Domain`: 12 entities incl. `Source` (`BlobKey`, `Status`: Pending/Processing/Ready/Failed, `FailureReason`) and `SourceChunk` (`PageNumber`, `ChunkIndex`, `Content`, `float[] Embedding`, `EmbeddingDimensions = 768`).
- `Application`: interfaces in `Common/Interfaces` (`ITokenGenerator`, `ICurrentUser`, `IUserRepository`), exceptions in `Common/Exceptions` (`RequestValidationException`→400, `AuthenticationFailedException`→401, `NotFoundException`→404, `ConflictException`→409), `Auth/` feature (contracts, `IAuthService`, `AuthService`), `DependencyInjection.AddApplication()`.
- `Infrastructure`: `Persistence/TalkToRagDbContext` + one `IEntityTypeConfiguration` per entity, snake_case naming, `InitialSchema` migration (pgvector extension, `source_chunks.embedding vector(768)` with HNSW cosine index, generated `search_vector tsvector` with GIN index), `UserRepository`, `Security/JwtTokenGenerator`, `DependencyInjection.AddInfrastructure()`.
- `Api`: `HealthController`, `AuthController` (`/api/auth/register|login|me`, JWT bearer), `Security/ClaimsCurrentUser`, `Errors/ApplicationExceptionHandler` (problem+json).
- Tests in `tests/TalkToRag.UnitTests` (31 passing; xUnit; fakes rather than a database; `SchemaTests` checks generated SQL).

**Stack — `compose.yaml`**: `postgres` (pgvector:pg17), `ai`, `api`, volumes `pgdata` and `blobs` (shared; both images run as uid 1654), ports bound to 127.0.0.1 only.

## Milestone 2 scope

Upload a **PDF, Markdown or plain-text** file → the API stores it and returns immediately → a background worker asks the AI service to parse, chunk and embed it → chunks with locations and embeddings land in `source_chunks` → the source becomes `Ready` (or `Failed` with a readable reason).

```
POST /api/sources (multipart) ─► IBlobStorage.WriteAsync("sources/{id}.pdf")
                               ─► INSERT sources (Pending) ─► ISourceIngestionQueue.Enqueue(id) ─► 202 {id, status}
SourceIngestionWorker ─► status Processing ─► IAiClient: POST ai:8000/v1/sources/ingest {source_id, blob_key, content_type}
                      ◄─ {chunks: [{page_number, section, chunk_index, content, embedding[768]}]}
                      ─► bulk INSERT source_chunks, status Ready   (on error: Failed + failure_reason)
```

**Out of scope:** search endpoints and RRF (Milestone 3), conversations, notebook/tag CRUD, the Angular UI, OCR for scanned PDFs, EPUB, speech, Redis, cloud deployment.

## Decisions

Settled — follow these:
1. **Python stays stateless**: it never touches Postgres. It reads the file by blob key and returns chunks; .NET owns all writes.
2. **Chunks never cross a page boundary**, so every chunk has one exact page number to cite.
3. **Embedding prefixes are the adapter's job, not the caller's.** `nomic-embed-text` expects `search_document: ` on indexed text and `search_query: ` on questions. Change the `Embedder` contract to two methods, `embed_documents(texts) -> list[list[float]]` and `embed_query(text) -> list[float]`, and put the prefixes in `models.toml` as model options (e.g. `document_prefix`, `query_prefix`). Update the fake adapter and tests.
4. **Embed in batches** (e.g. 32 texts per request) rather than one call per chunk.
5. **Restart safety**: the in-memory `Channel` queue is lost on restart, so on startup the worker re-enqueues sources still `Pending` or `Processing`.
6. **Ownership**: every source query filters on `ICurrentUser.UserId`; another user's source is a 404, not a 403.
7. **Readable failures**: a PDF with no extractable text (scanned images) fails with `failure_reason` like "No text layer found. This looks like a scanned PDF and needs OCR." The AI service returns 422 with that message; the worker stores it.

Recommended — confirm with Dan in your plan, then proceed:
- **PDF parsing with `pypdf`** (BSD-3-Clause): page-by-page text. Equations will come out as plain text; preserving LaTeX is a later problem — note it, don't solve it.
- **Chunk size ~1,000 characters (~250 tokens) with ~150 characters of overlap**, split on paragraph then sentence boundaries, never mid-word. Explain to Dan why smaller chunks give sharper matches but less context, and what overlap protects against.
- **Markdown has no pages**: add a nullable `section` text column to `source_chunks` (new EF migration, not an edit to `InitialSchema`) holding the heading path, e.g. `Bellman equations > Optimality`; use `page_number = 1` for single-page text. Fill `section` for PDFs too if the PDF outline makes it cheap; otherwise leave it null.
- **Upload limits**: allow `application/pdf`, `text/markdown`, `text/plain` (check extension too); max 50 MB; title defaults to the file name without extension.

## Contracts to build

**AI service** — `POST /v1/sources/ingest`
- Request: `{ "source_id": "uuid", "blob_key": "sources/{id}.pdf", "content_type": "application/pdf" }`
- 200: `{ "chunks": [ { "page_number": 12, "section": "6.1 TD prediction" | null, "chunk_index": 0, "content": "...", "embedding": [768 floats] } ] }`
- 422: `{ "detail": "No text layer found..." }` for unreadable files; 404 if the blob key doesn't exist.
- New module `talk_to_rag/ingestion/`: `contracts.py` (`SourceParser`, `TextChunker`, `ParsedPage(page_number, text, section)`, `ChunkPayload`, `IngestionError`), `adapters/` (`pypdf_parser.py`, `markdown_parser.py`, `text_parser.py`, `paragraph_chunker.py`), plus a route in `api/routes/ingestion.py` and schemas in `api/schemas.py`.

**.NET API**
- `POST /api/sources` — `[Authorize]`, multipart `file` → **202** `{ id, title, status: "Pending" }`; 400 for wrong type/size/empty file.
- `GET /api/sources` — the caller's sources, newest first: `{ id, title, fileName, contentType, sizeBytes, status, failureReason, chunkCount, createdAtUtc }`.
- `GET /api/sources/{id}` — one source (same shape); 404 if missing or not the caller's.
- Application: `Sources/SourceContracts.cs`, `ISourceService`, `SourceService`; interfaces `IBlobStorage` (`WriteAsync`, `OpenReadAsync`, `DeleteAsync` by key), `IAiClient` (`IngestSourceAsync`), `ISourceIngestionQueue`; a source repository interface if you need one (follow `IUserRepository`).
- Infrastructure: `Storage/LocalBlobStorage` (same key rules as Python: reject `..`, absolute paths, backslashes; root from config `BlobStorage:Root`, default `/var/talk-to-rag/blobs`), `Ai/HttpAiClient` (typed `HttpClient`, base URL from `AiService:BaseUrl`; set `http://ai:8000` in `compose.yaml`; generous timeout — embedding a book takes minutes on this Mac), `Ingestion/ChannelSourceIngestionQueue`, `Ingestion/SourceIngestionWorker : BackgroundService` (create a DI scope per job for the DbContext).

## Suggested step order (one commit each)

1. Embedder contract: `embed_documents` / `embed_query`, prefixes from `models.toml`, tests.
2. Python parsers + chunker with unit tests (tiny fixture files you create; credit them as made for the project).
3. `POST /v1/sources/ingest` route + schemas + tests using the fake embedder and a temp blob root.
4. `.NET`: `section` migration, `IBlobStorage` + `LocalBlobStorage` + tests.
5. `.NET`: `POST/GET /api/sources` with `SourceService`, validation, ownership + tests.
6. `.NET`: queue, `HttpAiClient`, `SourceIngestionWorker` (incl. restart re-enqueue and failure handling) + tests with fakes.
7. `compose.yaml` (`AiService__BaseUrl`), README, CREDITS, architecture doc updates; end-to-end check.

## Done means

- `make test` passes (Python + .NET), and new code has tests.
- `make up && make migrate`, then: register, upload a small text PDF and a Markdown file through `POST /api/sources`, poll `GET /api/sources/{id}` until `Ready`, and show in `psql` that `source_chunks` rows have correct page numbers/sections and 768-dim embeddings.
- A scanned/empty PDF ends `Failed` with the readable reason.
- One manual similarity query works, e.g. embed a question with `embed_query` and `ORDER BY embedding <=> $1 LIMIT 5` returns the expected page.
- `CREDITS.md`, `README.md` and `docs/architecture.md` reflect what changed (including the new `section` column in the ER diagram).
- PR description explains each retrieval decision in plain English for Dan.
