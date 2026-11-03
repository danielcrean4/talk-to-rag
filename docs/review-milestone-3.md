# Review: `milestone-3` (for Antigravity)

Reviewed `2b86bbd..e42fe50` in a clean checkout. **All 140 tests pass** (Python 54, .NET unit 85, integration 1).

**Already merged:** `2b86bbd` (worker crash fix, retry endpoint, sanitised failure reasons). It was fast-forwarded to `main` and pushed. Rebase `milestone-3` on `main`; that's a no-op, since `2b86bbd` is already its first commit.

**Not merged yet:** the other six commits. The structure is good. Notebooks and tags follow the existing patterns, ownership is checked everywhere, the RRF SQL and `hnsw.iterative_scan` work against real Postgres, and the UI makes the app testable end to end. But four bugs need fixing first, and the deslop pass (`.agents/skills/deslop`) wasn't run: no commit applies it.

Same rules as before: one commit per fix with a test, credit new dependencies, don't push or merge. **Local models only:** never add a hosted or paid model API (OpenAI, Gemini, Anthropic, etc.), not even as an option. A later cloud deploy would run open-weight models on infrastructure Dan controls, for a few trusted users.

## Fix before merge

1. **Search snippets are injected as raw HTML (XSS).** `wwwroot/app.js` `renderSearchResults` writes `${p.snippet}` into `innerHTML`. `ts_headline` doesn't escape the chunk text, and the fallback `CutAtWordBoundary` returns raw content. A Markdown note with `<img src=x onerror=...>` runs script in the page that holds the JWT in `localStorage`. Fix: escape the snippet, then restore only the highlight tags, e.g. `escapeHtml(p.snippet).replaceAll('&lt;mark&gt;', '<mark>').replaceAll('&lt;/mark&gt;', '</mark>')`. Add a test or a manual check with a `<b>`-containing Markdown file.

2. **Summary citations point at the wrong passage.** `SearchService` numbers citations in RRF order (`Number = index + 1` over all hits). The UI numbers cards in *grouped* order (`citationCounter` across source groups). Suppose hits come back A1, B1, A2. The server calls B1 `[2]`, but the UI labels A2 as `[2]`, so clicking `[2]` in the summary jumps to the wrong card. Fix: add `Number` to `PassageHitDto`, set it from the same index the citations use, and render that number in the UI. Add a unit test with two sources whose hits interleave.

3. **Query expansion is computed and then thrown away.** `SearchRepository.SearchAsync` uses only `QueryEmbeddings[0]`. With `expand: true` the AI service spends local compute on a gemma3:1b call plus 2–3 extra embeddings (several seconds on the 8 GB Mac), and the results are identical. Fix: run the semantic CTE once per embedding (e.g. `unnest` the vectors with an ordinality index, `row_number() OVER (PARTITION BY query_idx ...)`), then feed every list into the same RRF sum. This is the "multi-query" half of RRF, so explain it in the PR. Add an integration test where only the second query finds the chunk.

4. **Copy-protected PDFs are now rejected.** `pypdf_parser.py` raises "password-protected or encrypted" whenever `reader.is_encrypted` is true. Many publisher and textbook PDFs carry only an *owner* password (permissions), with an empty user password. pypdf opens and extracts these fine. Checked: `is_encrypted` is `True` and pages are readable. Fix: reject only when `reader.decrypt("")` returns `PasswordType.NOT_DECRYPTED`. Add a fixture built with `PdfWriter.encrypt(user_password="", owner_password="x")`.

5. **Move pipeline options off the search page and into Settings.** Dan wants retrieval to stay composable and configurable, but tuning knobs don't belong on the main screen. Remove the query-expansion toggle and the retrieval-mode dropdown (hybrid / semantic / keyword) from the search bar. Add a small Settings view (gear icon) that holds both, with defaults hybrid and expansion off. Store the choices in `localStorage` for now and keep sending them as request fields, so `POST /api/search` stays unchanged. The search page keeps only the query box, the mic button and the scope filter, which is about *what* to search, not *how*.

## Deslop (behaviour unchanged)

- `pypdf_parser.py`: drop the new broad `except Exception` fallback; review item 8 was about removing broad catches, not adding one. Also drop the `getattr(reader, "page_labels", None)` + `try/except` around it: `page_labels` is a normal pypdf property. The `except IngestionError: raise` dance goes away once the encryption check moves out of the `try`.
- `rag.py` `expand_query`: `except Exception` around the chat call should be `except ModelError`. A parsing bug should fail a test, not log quietly. `if lines:` before the loop is redundant. `lstrip("*-0123456789. ")` strips leading digits from real queries ("2024 tax rules" → "tax rules"); strip a list marker with a regex like `^\s*(?:[-*]|\d+[.)])\s+` instead.
- `rag.py` `answer_passages`: `try: int(raw) except ValueError` can't fail on a `\d+` match; `sorted(list(set))` → `sorted(set)`.
- `SearchService`: inject options (`IOptions<SearchOptions>`) rather than `IConfiguration` into the Application layer. Drop step-narrating comments (`// Resolve scope`, `// Save user query message`, `// Group hits by source`). Simplify the `Select(...null).Where(c => c != null).Select(c => c!)` chain with a dictionary lookup.
- `Program.cs`: remove the `AllowAnyOrigin` CORS policy. The UI is served from the same origin, so nothing needs it. Add a narrow policy for the Angular dev server when that exists.
- `index.html`: Google Fonts loads Inter and JetBrains Mono from Google on every page view. Either use a system font stack (simplest, and fully offline) or credit both fonts (SIL OFL 1.1) in `CREDITS.md`.

## Worth a look, not blocking

- `SearchService.SearchAsync` saves the user message before calling the AI service. If Ollama is down, history keeps a question with no results. Save after the search succeeds.
- `ConversationRepository.UpdateAsync` calls `db.Conversations.Update(...)` on a conversation loaded with all messages and citations. That marks the whole graph modified and rewrites every row. The entity is already tracked, so `SaveChangesAsync` alone is enough.
- Before adding more endpoints, check `EXPLAIN ANALYZE` on the semantic CTE with a book-sized table to confirm the HNSW index is used. The `row_number()` window plus the join to `sources` can push the planner into a sequential scan.

## Done means

Fixes 1–4 have tests, fix 5 is checked by hand, the deslop pass is one commit, everything passes in a clean checkout, and the UI check works: upload a Markdown file with inline HTML, search it, generate a summary, and click each citation.
