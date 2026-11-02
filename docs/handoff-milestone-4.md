# Handoff: Milestone 4 — Frontend UI, Voice, Streaming & Next Steps

This handoff document is for Claude (and Dan) picking up `talk-to-rag` after the completion of **Milestone 3**.

---

## 1. Current State & Branch Summary

- **Branch**: `milestone-3` (clean working tree, ready for review/merge to `main`).
- **Tests**: **140 tests passing** in clean runs:
  - Python AI Service: **54 passed** (`cd services/ai && uv run pytest -q`)
  - .NET Unit Tests: **85 passed** (`dotnet test services/api/tests/TalkToRag.UnitTests`)
  - .NET Integration Tests: **1 passed** (`dotnet test services/api/tests/TalkToRag.IntegrationTests`, running against real `pgvector/pgvector:pg17` via Testcontainers)
- **Compose Stack**: Fully running and healthy (`make health` returns `200 OK` for both `talk-to-rag-ai` and `talk-to-rag-api`).
- **Live UI**: An interactive Web UI is served directly at **`http://localhost:5000/`** (static files hosted by ASP.NET Core Kestrel).

### Git Commits on `milestone-3` (One commit per logical step)
1. `2b86bbd` — *Wrap worker in resilient try/catch and implement source retry endpoint* (Items 13 & 14)
2. `67d8104` — *Add source_chunks.page_label migration, PDF page labels, and line-break hyphen rejoining* (Items 6 & 7)
3. `5cf78df` — *Implement Notebooks and Tags CRUD and source-linking endpoints*
4. `b8eb263` — *Add query expansion and grounded cited answer endpoints to AI service*
5. `cf94d77` — *Implement ISearchRepository RRF SQL with HNSW iterative scans and Testcontainers integration tests*
6. `730f231` — *Implement Hybrid Search API, Conversation History, and Cited Summary Generation in .NET*
7. `f64e2c5` — *Add interactive web UI for document ingestion, hybrid search, and cited summaries*

---

## 2. What Was Built in Milestone 3

### A. Code Review Follow-ups from Milestone 2 (Items 13, 14, 6, 7)
- **Item 13 (Worker Resilience)**: `SourceIngestionWorker.ExecuteAsync` now wraps each per-job execution in a resilient top-level `try/catch`. Database connectivity drops or repository errors are logged without terminating the `BackgroundService` host process.
- **Item 14 (Retry Endpoint & Error Sanitization)**: Added `POST /api/sources/{id}/retry`. It enforces owner-only access, resets `Failed` sources back to `Pending`, clears prior `FailureReason`, and re-enqueues the job into the ingestion channel. Generic unhandled exceptions sanitize `FailureReason` to `"Ingestion failed due to an unexpected error. Please retry."` while logging raw internal details.
- **Item 6 (Page Labels)**: Added `PageLabel` to `SourceChunk` entity with EF Core migration `20261102214008_AddSourceChunkPageLabel`. In `pypdf_parser.py`, `reader.page_labels` extracts printed page numbers (e.g. `xii`, `120`) while preserving `page_number` as the 1-based physical index for viewers.
- **Item 7 (Hyphen Rejoining)**: Added regex rejoining `re.sub(r"-\r?\n\s*([a-z])", r"\1", text)` in `pypdf_parser.py` so words broken across lines (like `boot-\nstrapping`) are rejoined into `bootstrapping` for vector embedding and FTS indexing.

### B. Notebooks & Tags (`TalkToRag.Application.Notebooks` & `TalkToRag.Application.Tags`)
- Full CRUD endpoints:
  - `POST /api/notebooks`, `GET /api/notebooks`, `GET /api/notebooks/{id}`, `PATCH /api/notebooks/{id}`, `DELETE /api/notebooks/{id}`
  - `PUT /api/notebooks/{id}/sources/{sourceId}`, `DELETE /api/notebooks/{id}/sources/{sourceId}`
  - `POST /api/tags`, `GET /api/tags`, `DELETE /api/tags/{id}`, `PUT /api/sources/{id}/tags/{tagId}`, `DELETE /api/sources/{id}/tags/{tagId}`
- Enforces strict user ownership (other users' IDs return 404) and uniqueness per user (clashing names return 409 Conflict).

### C. AI Service RAG Capabilities (`services/ai/src/talk_to_rag/api/routes/rag.py`)
- `POST /v1/rag/expand-query`:
  - With `expand: false`: returns original query embedded with `embed_query`.
  - With `expand: true`: prompts `local-small` (gemma3:1b) to rewrite question into 2–3 retrieval search terms, each individually embedded. Gracefully falls back to original query if output parsing fails.
- `POST /v1/rag/answer`:
  - Enforces strict grounded answering from provided numbered passages.
  - Instructs model to cite sources using bracketed notation `[1]`, `[2]`.
  - Automatically parses bracket citations and drops hallucinated numbers not present in the input passages.

### D. Parameterized Hybrid Search & Reciprocal Rank Fusion (`SearchRepository.cs`)
- Raw parameterized SQL executed through `TalkToRagDbContext.Database.SqlQuery<T>`:
  - Executes `SET LOCAL hnsw.iterative_scan = relaxed_order;` inside the transaction to solve the classic HNSW post-filtering issue where narrow filters (by notebook or tag) prematurely exhaust nearest-neighbor candidates.
  - Computes top 50 semantic hits using pgvector cosine distance (`<=>`) against `source_chunks.embedding`.
  - Computes top 50 keyword hits using PostgreSQL full-text search (`c.search_vector @@ websearch_to_tsquery('english', @query)`) ranked by `ts_rank_cd`.
  - Merges rankings using Reciprocal Rank Fusion: $\sum \frac{1}{60 + \text{rank}}$.
  - Generates highlighted snippets using `ts_headline` on the final fused hits.
- **Pgvector Parameter Handling**: Vector parameters are cast via `@embedding::vector` with stringified vector text to ensure compatibility across all Npgsql/pgvector versions without `InvalidCastException`.
- **Integration Tests**: Tested against real PostgreSQL + `pgvector` container in `TalkToRag.IntegrationTests` using Testcontainers. Credited in `CREDITS.md`.

### E. Search & Conversation History API (`SearchController.cs`, `ConversationsController.cs`)
- `POST /api/search`:
  - Filters by current user, status `Ready`, and optional scopes (`notebookIds`, `tagIds`, `sourceIds`).
  - Returns passages grouped by source with page numbers, page labels, section headers, RRF scores, and matched-by tags (`["keyword", "semantic"]`).
  - Auto-creates or appends to a `Conversation`, recording the query and citations.
- `POST /api/search/{conversationId}/summary`:
  - Calls AI service `POST /v1/rag/answer` with top passages.
  - Verifies and persists the assistant message with citations.
- `GET /api/conversations` and `GET /api/conversations/{id}`:
  - Lists and retrieves full message history with cited passage snippets.

### F. Real Interactive Web UI (`services/api/src/TalkToRag.Api/wwwroot/`)
- Zero-build web frontend served directly by Kestrel (`http://localhost:5000/`):
  - **Auth**: Modal for registration and sign-in + "⚡ Quick Register / Demo User" button.
  - **Sources Tab**: Drag-and-drop document upload, live polling every 3s, retry button on failure, chunk counts, notebook pills filter, notebook creation modal, and source assignment dropdown.
  - **Search & Results Tab**: Search input with push-to-talk speech recognition (`webkitSpeechRecognition`), retrieval mode dropdown (`hybrid`, `semantic`, `keyword`), scope filter, query expansion toggle, passage hit cards with `<mark>` highlights and page badges.
  - **Grounded AI Study Summary**: "✨ Generate AI Summary" button, answer formatting with clickable citation badges `[1]` that smoothly scroll and flash-highlight the cited passage card, plus a "🔊 Read Aloud" text-to-speech button.
  - **History Tab**: View past queries, responses, and cited excerpts.

---

## 3. Review Notes for Merging `milestone-3`

When reviewing `milestone-3` before merging into `main`:
1. **Migration Check**: Migration `20261102214008_AddSourceChunkPageLabel` was generated and applied to the database container (`make migrate`).
2. **Dependencies**: `Testcontainers.PostgreSql` (4.16.0) added to `TalkToRag.IntegrationTests.csproj` and credited in `CREDITS.md`.
3. **Fixture Files**: `services/ai/tests/fixtures/*.pdf` are verified as tracked in git (`git ls-files services/ai/tests/fixtures`).
4. **No push/merge policy**: As required, `milestone-3` has not been pushed to remote or merged to `main`.

---

## 4. Suggested Scope for Milestone 4

According to `docs/architecture.md`, Milestone 4 focuses on advancing the frontend UI, voice pipeline, and streaming capabilities:

1. **Streaming SSE (Server-Sent Events) for Summaries**:
   - In `POST /api/search/{conversationId}/summary` (or `/stream`), stream LLM answer tokens in real-time to the browser rather than waiting for full completion.
   - FastAPI in `services/ai` can yield chunks from the OpenAI-compatible streaming client.
2. **Dedicated Voice Pipeline**:
   - The current Web UI uses the browser's native Web Speech API (`SpeechRecognition` and `SpeechSynthesis`) for zero-dependency push-to-talk and read-aloud.
   - For Milestone 4, evaluate integrating host-native voice services:
     - Speech-to-Text: Local Whisper (or `faster-whisper`) via audio blob upload (`POST /api/speech/transcribe`).
     - Text-to-Speech: Local Kokoro / Piper TTS for high-quality audio streaming.
3. **UI Enhancements / Angular or Framework Migration**:
   - The current zero-build vanilla JS UI at `http://localhost:5000/` provides full end-to-end functionality without npm/Node.
   - If Dan wants a formal Angular SPA (as originally outlined in the architecture doc), the API is fully prepared with OpenAPI specs and CORS. Alternatively, the existing clean UI can be expanded.
4. **PDF Viewer / Open-at-Page Integration**:
   - Integrate PDF.js to open documents directly at `page_number` when clicking passage citations.

---

## 5. Verification Commands

To verify everything on a clean run:
```bash
# 1. Run AI service tests
cd services/ai && uv run pytest -q

# 2. Run .NET unit tests
cd services/api && dotnet test tests/TalkToRag.UnitTests

# 3. Run .NET integration tests (requires Docker daemon)
cd services/api && dotnet test tests/TalkToRag.IntegrationTests

# 4. Check services health
make health

# 5. Open Web UI
open http://localhost:5000
```
