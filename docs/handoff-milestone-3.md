# Handoff: Milestone 3 — hybrid search, results with locations, optional summary

For Antigravity, once Milestone 2 is merged. It has two parts:

1. **Code review of Milestone 2**: fix the "before merge" items on `milestone-2` first.
2. **Milestone 3 brief**: start it on a new branch `milestone-3` from `main` after the Milestone 2 merge.

Same working rules as [handoff-milestone-2.md](handoff-milestone-2.md):
- Dan is learning: explain each decision in plain English.
- Plan first and wait for approval.
- One commit per step, tests passing.
- Don't push, merge or touch `services/ai/config/models.local.toml`.
- Credit every new dependency in `CREDITS.md` in the same commit; no AGPL/GPL.

---

## Part 1 — Code review of Milestone 2

**Scope reviewed:** commits `96aa80e`..`02a8fac` on `milestone-2` (steps 1–5). These were tested in a clean checkout: **Python 4 failed / 39 passed, .NET 52 passed.** Step 6 (queue, `HttpAiClient`, `SourceIngestionWorker`) was uncommitted when this was written and is **not reviewed**; the "Step 6 checklist" below says what to make sure it does.

Overall this is solid work. The contracts-plus-adapters pattern is followed throughout, `embed_documents` / `embed_query` with prefixes in `models.toml` is exactly right, chunks never cross pages, ownership is enforced in every query, the blob key rules match Python's, and the `section` column came as a new migration. The findings are mostly about things that only show up with real books or a fresh clone.

### Fix before merging Milestone 2

1. **The PDF test fixtures aren't in git, so 4 Python tests fail on a fresh clone.** The root `.gitignore` has `*.pdf` (there to keep private books out), which also ignores `services/ai/tests/fixtures/sample.pdf` and `scanned_or_blank.pdf`. They exist on Dan's disk, so tests pass there but fail in CI or a new checkout. Fix: add `!services/ai/tests/fixtures/*.pdf` to `.gitignore`, commit the two fixtures, and check them with `git ls-files`. (The fixtures are credited as made for the project, which is right.)

2. **`GET /api/sources/{id}` loads every chunk, embeddings included, just to count them.** `SourceRepository.FindByIdForUserAsync` (line 13) does `.Include(s => s.Chunks)`, then `SourceService.GetByIdAsync` uses `source.Chunks.Count` (line 108). A 500-page book is ~2,000 chunks × 768 floats. That's megabytes per call, and the UI will poll this endpoint while ingestion runs. Fix: project to `SourceDto` with `Chunks.Count()` in SQL, as `ListForUserAsync` already does. Drop the `Include` from `FindByIdAsync` (line 18) too, unless the worker needs it; it shouldn't.

3. **Uploads over ~28.6 MB are rejected before the 50 MB check runs.** Kestrel's default `MaxRequestBodySize` is 30,000,000 bytes, so a 40 MB textbook gets a 413 from the server, not the friendly 400. Fix: `[RequestSizeLimit]` and `[RequestFormLimits(MultipartBodyLengthLimit = ...)]` on `SourcesController.Upload` (line 17), set a little above 50 MB. Add a test or a manual check.

4. **An unreachable Ollama becomes a bare 500 from the AI service.** `api/routes/ingestion.py` line 42 calls `embed_documents` without catching `ModelError`. Fix: map `ModelError` to **503** with the message. The .NET worker can then tell "the file is bad" (422 → `Failed`) from "the model server is down" (503 → leave `Pending` and retry later, or fail with a clear "embedding service unavailable" reason). Add a test using an embedder that raises.

5. **Headings inside Markdown code blocks are treated as section headings.** `markdown_parser.py` line 43 matches `#` lines everywhere, including inside ``` fences. A Python comment like `# load the data` in a code block becomes a section. Dan's notes and docs on Python and TypeScript will hit this. Fix: track fenced-code state (``` and ~~~) and skip heading detection inside fences. Add a fixture case.

### Fix soon (Milestone 3 is a fine time)

6. **PDF page numbers are physical pages, not the book's printed page numbers.** `pypdf_parser.py` line 23 uses `index + 1`. In a textbook, printed page 120 is often PDF page 138 because of front matter, so citations will point to the wrong page. pypdf exposes `reader.page_labels`. Store the printed label for display, keep the physical index for "open at page" (PDF.js needs the physical one), and explain the difference to Dan. This needs a nullable `page_label` column. arXiv papers, likely Dan's first library, mostly match one to one, so it's less urgent there.
7. **Words hyphenated across PDF line ends stay split** ("boot-\nstrapping"). That hurts both keyword search and embeddings. Rejoin `-\n` followed by a lowercase letter before chunking.
8. **`except (PyPdfError, Exception)`** (`pypdf_parser.py` line 19) is just `except Exception`. Narrow it, or keep it broad with a comment saying why. Encrypted PDFs deserve their own readable message.
9. **Chunks can exceed the target size by the overlap.** The overlap is prepended (`paragraph_chunker.py` line 64) after the size check, so chunks reach ~1,150 characters. That's harmless for `nomic-embed-text`, but either document it as intended or account for the overlap in the size check.
10. **Upload writes the blob before inserting the row** (`SourceService.cs` line 49). If the insert fails, the file is orphaned. Insert first, or delete the blob on failure.
11. **ID style:** sources use `Guid.NewGuid()` (line 46) while users use `Guid.CreateVersion7()`. Prefer v7 everywhere: time-ordered IDs keep B-tree indexes compact.
12. **Path check:** `LocalBlobStorage.cs` line 75 uses `StartsWith(_root, OrdinalIgnoreCase)`. It's safe today because `..` is rejected first, but compare against `_root + Path.DirectorySeparatorChar` with `Ordinal`: Linux paths are case-sensitive, and a sibling folder like `blobs-old` shares the prefix.

### Step 6 checklist (not yet reviewed)

- One DI scope per job; set `Processing` before calling the AI service; `Ready` only after all chunks are saved.
- Insert chunks in one transaction, in batches. Saving thousands of rows one `SaveChanges` at a time will be slow; `AddRange` plus a single save, or Npgsql binary import.
- **Idempotent retries:** if a source is re-ingested after a crash, delete its existing chunks first, or the unique `(source_id, chunk_index)` index will fail.
- Restart re-enqueue of `Pending`/`Processing` sources, from `FindPendingOrProcessingIdsAsync`.
- 422 from the AI service → `Failed` with its `detail` as `failure_reason`; 503/timeout → handled per item 4; anything else → `Failed` with a short reason. The worker must never crash the host. Log and continue.
- `HttpAiClient` timeout long enough for a whole book on this Mac (minutes), set in config. `AiService__BaseUrl=http://ai:8000` in `compose.yaml`.
- Tests with fakes for: success, 422 failure, transient failure, re-ingest.

### Re-review after the fixes (`b9b849b`..`2ab8b5a`)

All five "before merge" items and the step 6 checklist were addressed, with tests. In a clean checkout, `make test` passes: **Python 45, .NET 61.** End-to-end on the compose stack:
- A PDF, a Markdown file and a scanned PDF uploaded with 202. The PDF and Markdown went `Ready` with 768-dim chunks, correct page numbers and heading paths. The scanned PDF went `Failed` with the OCR message.
- A `.docx` upload got 400, and another user got 404 on someone else's source.
- A meaning search for "why wait for the end of an episode?" ranked PDF page 2 (Monte Carlo) first.

Two things remain. Do them as the first step of Milestone 3 if Milestone 2 is merged as is:

13. **A database error at the start of a job stops the whole API.** In `SourceIngestionWorker.ProcessSourceAsync`, `FindByIdAsync` and the first `UpdateAsync` (status → `Processing`) run outside the `try`, and each `catch` calls `UpdateAsync` again. If Postgres restarts or drops a connection there, the exception escapes `ExecuteAsync`. Since .NET 6 an unhandled `BackgroundService` exception stops the host by default, so ingestion would take the API down. Fix: wrap each job in the loop with a catch-all that logs and continues, and make the failure-status update best-effort. Add a test where the repository throws on the first call and the worker keeps going.
14. **"Ollama wasn't running" becomes a permanent failure.** A 503 marks the source `Failed`, and the only way back is re-uploading. Forgetting to start Ollama is the most likely real failure on this Mac. Add `POST /api/sources/{id}/retry`, owner only: it re-enqueues `Failed` sources and leaves others alone. Optionally retry 503s automatically a few times with backoff before failing. Also, the generic `catch` stores `ex.Message` as `failure_reason`, which can expose internals such as host names. Store a short generic message and log the detail.

---

## Part 2 — Milestone 3 brief

### Goal

Answer the product's core question, **"which of my sources cover this, and where?"** A search returns passages grouped by source, each with its page, section and a snippet, ranked by a fusion of keyword search and meaning search. A short summary that cites those passages is optional on top. This is the R in RAG; the summary is the G.

Out of scope: Angular UI (Milestone 4), speech, evaluation runner and CI (Milestone 5), RAPTOR summary trees and faithfulness evals (parked), cloud.

### What to build

**1. Notebooks and tags (prerequisite for scoped search)**
- `POST/GET /api/notebooks`, `PATCH/DELETE /api/notebooks/{id}`, `PUT/DELETE /api/notebooks/{id}/sources/{sourceId}`.
- The same for `/api/tags` and `/api/sources/{id}/tags/{tagId}`.
- Every query filters on the current user; names are unique per user (409 on clash, using the existing indexes); another user's ID is a 404.

**2. Query embedding endpoint in the AI service**
- `POST /v1/rag/expand-query`: request `{ "question": "...", "recent_messages": [...], "expand": false }` → `{ "queries": [ { "text": "...", "embedding": [768] } ] }`.
- With `expand: false`, return only the original question, embedded with `embed_query`. With `expand: true`, a small chat model (`local-small`) rewrites it into 2–3 standalone search queries, and each one is embedded. If the model's output can't be parsed, fall back to the original question; never fail the search.
- Expansion is **off by default** until Milestone 5 measures whether it helps. It adds latency, which matters for voice.

**3. Hybrid search in .NET — `POST /api/search`**
- Request: `{ "query": "...", "notebookIds": [], "tagIds": [], "sourceIds": [], "mode": "hybrid" | "keyword" | "semantic", "expand": false, "limit": 20, "conversationId": null }`
- Response, grouped as in the prototype's Results view:
  ```json
  { "conversationId": "uuid",
    "query": "...",
    "groups": [ { "source": { "id": "...", "title": "...", "contentType": "..." },
                  "passages": [ { "chunkId": "...", "pageNumber": 120, "section": "6.1 TD prediction",
                                  "snippet": "...<mark>temporal difference</mark>...",
                                  "score": 0.031, "matchedBy": ["keyword", "semantic"] } ] } ] }
  ```
- **Scope rule:** a source is in scope if it belongs to the user, is `Ready`, and (no filters given, or it's in any selected notebook, or has any selected tag, or is in `sourceIds`).
- **Fusion:** Reciprocal Rank Fusion, `score = Σ 1 / (60 + rank)` over each list a chunk appears in. Take the top ~50 from each list, fuse, return `limit`. With expansion on, every expanded query's semantic list joins the fusion. A sketch:
  ```sql
  WITH semantic AS (
    SELECT c.id, row_number() OVER (ORDER BY c.embedding <=> @q) AS rank
    FROM source_chunks c JOIN sources s ON s.id = c.source_id
    WHERE s.user_id = @user AND s.status = 'Ready' /* AND scope */
    ORDER BY c.embedding <=> @q LIMIT 50),
  keyword AS (
    SELECT c.id, row_number() OVER (ORDER BY ts_rank_cd(c.search_vector, q) DESC) AS rank
    FROM source_chunks c JOIN sources s ON s.id = c.source_id, websearch_to_tsquery('english', @text) q
    WHERE c.search_vector @@ q AND s.user_id = @user AND s.status = 'Ready' /* AND scope */
    ORDER BY rank LIMIT 50)
  SELECT id, sum(1.0 / (60 + rank)) AS score
  FROM (SELECT * FROM semantic UNION ALL SELECT * FROM keyword) r
  GROUP BY id ORDER BY score DESC LIMIT @limit;
  ```
- **Filtered vector search gotcha:** HNSW finds nearest neighbours first and filters afterwards, so a narrow scope can return fewer than 50 rows. pgvector 0.8 (we run 0.8.7) has iterative index scans for exactly this: `SET LOCAL hnsw.iterative_scan = relaxed_order` inside the search transaction. Explain this to Dan; it's a classic interview topic.
- **Snippets:** `ts_headline('english', content, q, 'StartSel=<mark>,StopSel=</mark>,MaxFragments=1,MaxWords=35')` for keyword hits, run **only on the final top results** (it's slow on big inputs). For meaning-only hits, use the first ~240 characters, cut at a word boundary.
- **Distance cutoff:** config `Search:MaxCosineDistance` (start around 0.6, or off). Semantic hits beyond it are dropped, so an off-topic question returns "nothing found" instead of the least-bad passage. The real value gets tuned in Milestone 5.
- Write the SQL as parameterised raw SQL through EF Core (`Database.SqlQuery<T>`) behind an `ISearchRepository` in Infrastructure; `SearchService` in Application stays SQL-free.

**4. History**
- Each search saves to a `Conversation`, created when `conversationId` is null and titled with the query. It saves a `User` message with the query (plus an `AudioBlobKey` later) and an `Assistant` message with the summary text (empty if none), with `MessageCitation` rows for the returned passages numbered by rank. Return the `conversationId`.
- `GET /api/conversations` (newest first) and `GET /api/conversations/{id}` (messages plus cited passages) power the History view.

**5. Optional cited summary — `POST /api/search/{conversationId}/summary`**
- .NET sends the top passages (numbered) to the AI service's `POST /v1/rag/answer` `{ "question": "...", "passages": [ { "n": 1, "content": "...", "source_title": "...", "page_number": 120 } ] }` → `{ "answer": "...", "cited": [1, 3] }`.
- Prompt rules: answer only from the passages, cite like `[1]`, and say plainly when the passages don't answer the question. Use `local-small` (gemma3:1b) by default; the model is configurable.
- Plain JSON for now; streaming (SSE) can wait for the UI in Milestone 4.
- Store `cited` as the assistant message's citations. Ignore numbers that weren't in the input, because small models invent them; count how often that happens and log it.

### Testing

- **Python:** expand-query with the fake chat and embedder (expansion on and off, unparsable output falls back); answer endpoint with a fake chat (citation parsing, invented numbers dropped).
- **.NET unit tests:** the RRF maths as a pure function, scope rules, ownership, citation numbering.
- **.NET integration tests against real Postgres:** the search SQL can't be trusted without them. Add a `TalkToRag.IntegrationTests` project using **Testcontainers for .NET** (`Testcontainers.PostgreSql`, MIT) with the `pgvector/pgvector:pg17` image. Seed a few chunks with known vectors and text, then assert keyword-only, semantic-only, fused order, scope filtering, other users' data never returned, and the distance cutoff. Docker is available locally; GitHub Actions runners have it too. Credit Testcontainers.
- **End-to-end:** `make up && make migrate`, upload two small sources, search, get grouped passages with correct pages and highlighted snippets, request a summary.

### Suggested step order

1. Milestone 2 follow-ups: items 13 and 14 first, then 6 and 7, then the other review items that fit naturally.
2. Notebooks and tags endpoints.
3. AI service `expand-query` (no expansion yet) and `answer` endpoints.
4. `ISearchRepository` + RRF SQL + integration tests.
5. `POST /api/search` with scope, snippets and cutoff.
6. History (conversations endpoints).
7. Optional query expansion behind the flag.
8. Summary endpoint.
9. README, `CREDITS.md`, `docs/architecture.md` (search sequence, any new columns).

### Things to teach Dan along the way

- Why RRF merges **ranks** rather than scores: cosine distances and `ts_rank` aren't on the same scale.
- What `websearch_to_tsquery` accepts (quotes, `or`, `-word`), and why `ts_rank_cd` rewards terms that appear close together.
- Why filtered approximate-nearest-neighbour search can under-return, and what iterative scans fix.
- Why a cutoff is needed at all: vector search always has a "nearest" result.
- Why the summary must refuse when the passages don't support an answer, and why invented citation numbers are a warning sign.
