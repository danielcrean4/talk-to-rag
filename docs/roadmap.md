# Roadmap

The work is split into two phases. Each is a GitHub milestone, and every feature and bug is an issue on one of them. Ideas that are not designed yet are listed under Later.

## Phase one: 1.0

Five steps:

1. Service skeletons, database schema, sign-in and the local Docker stack.
2. Upload and ingestion of PDFs, Markdown and text, with page and section kept for every passage.
3. Hybrid search, notebooks and tags, search history, and summaries that cite their passages.
4. The Angular UI for sources, results and history, with voice input and read-aloud by local models.
5. Retrieval evaluation (hit@k, MRR) and CI.

All five are done. The Angular app is the only web UI, served by the API. CI runs the tests and scores retrieval on a 26-question sample set, and fails when a score drops under its minimum.

- [What was built](https://github.com/danielcrean4/talk-to-rag/issues?q=milestone%3A1.0+is%3Aclosed)
- [What is left](https://github.com/danielcrean4/talk-to-rag/issues?q=milestone%3A1.0+is%3Aopen)
- [Acceptance checklist](https://github.com/danielcrean4/talk-to-rag/issues/22)

`1.0.0` is the first commit of this repository. Bugs found in it are filed on the 1.0 milestone and released as `1.0.x`.

## Phase two: 2.0

The app as a learning tool: a graph of concepts and their prerequisites, flashcards and attempts with mastery derived from them, and an audit log of every model call that an outside model can read but not change.

Some of phase two is already on `main`, because it was built before the phases were drawn.

- [What was built](https://github.com/danielcrean4/talk-to-rag/issues?q=milestone%3A2.0+is%3Aclosed)
- [What is left](https://github.com/danielcrean4/talk-to-rag/issues?q=milestone%3A2.0+is%3Aopen)

## Later

Ideas that are on neither milestone because they have not been designed yet.

- A mobile version of the app ([#51](https://github.com/danielcrean4/talk-to-rag/issues/51)).

## How issues are written

- One issue per feature or bug. A closed issue names the commits that did the work. Issues closed before `1.0.0` give dates only, because the history starts at that tag.
- A bug says when and how it was seen, or says that it was found by reading the code and has not been reproduced.
- No claim without something behind it: a commit, a test count, a query result. If something was not tested, the issue says so.
- Plain words. The cleanup rule in `.agents/skills/deslop/SKILL.md` applies to issues and docs as well as code.
