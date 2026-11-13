# Retrieval evaluation

`sample_questions.json` holds 26 questions about the four documents in `sample/`. Each one names the document that answers it and a phrase the answering passage contains. A search result counts as a hit when it comes from that document and contains the phrase, ignoring case, punctuation and line breaks.

```bash
make eval
```

This starts a second stack with an empty database (compose project `talk-to-rag-eval`, API on port 5001), loads the sample documents, scores hybrid, semantic and keyword search, and removes the stack and its data. The normal stack, its library and its search history are not touched. It needs Docker, Ollama with `nomic-embed-text`, `curl` and [uv](https://docs.astral.sh/uv/).

To score against a stack that is already running, with the sample documents loaded:

```bash
cd services/ai
uv run python -m talk_to_rag.evaluation --mode keyword   # or hybrid, semantic; add --expand for query rewriting
```

Each question is a real search, so this second way adds one entry per question to that stack's search history. The runner searches only the documents the questions name, so other documents in the library do not change the score. It prints the rank of the first hit for each question, then hit@1, hit@3, hit@5, hit@10 and the mean reciprocal rank (MRR).

## Scores

Top 10 results, no query expansion, `nomic-embed-text` embeddings.

| Date | Mode | hit@1 | hit@3 | hit@5 | hit@10 | MRR |
|---|---|---|---|---|---|---|
| 2026-11-12 | hybrid | 0.615 | 0.808 | 0.846 | 0.923 | 0.719 |
| 2026-11-12 | semantic | 0.654 | 0.769 | 0.846 | 0.923 | 0.733 |
| 2026-11-12 | keyword | 0.154 | 0.192 | 0.192 | 0.192 | 0.173 |
| 2026-11-12, keyword search matches any word | hybrid | 0.615 | 0.923 | 0.923 | 0.962 | 0.756 |
| 2026-11-12, keyword search matches any word | keyword | 0.500 | 0.885 | 0.885 | 0.885 | 0.673 |

The first three rows were measured before keyword search matched on any word. The last two come from `make eval`; semantic is unchanged at the values in row two. Passages with equal keyword rank are ordered by title and position, so a rerun gives the same numbers.

Add a row when a change to chunking, embedding or search moves the numbers.

`make eval` fails when hit@5 or MRR for a mode is under the value in `minimum_scores.json`. The minimums sit a little below the scores above, because embeddings differ slightly between machines. CI runs `make eval` on every push to `main` and on pull requests, with Ollama in a container on the runner.

## Writing questions

A question is an object with `id`, `question`, `target_source_title` (the file name without its extension) and at least one of:

- `target_text`: phrases copied from the document. Pick a phrase that occurs once and sits inside one paragraph, so it cannot fall across two passages.
- `target_pages`: page numbers, for PDFs.

`category` is a free label; the sample set uses `short`, `paraphrase` and `conversational`.
