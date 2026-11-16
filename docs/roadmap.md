# Roadmap

The work is split into milestones on GitHub. Every feature and bug is an issue on one of them, except the quality backlog, which is labelled instead. Ideas that are not designed yet are listed under Later.

## 1.0

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

## 1.1

The app as a learning tool: a graph of concepts and their prerequisites, flashcards and attempts with mastery derived from them, and an audit log of every model call that an outside model can read but not change.

Most of this is already in `1.0.0`: the concept graph, questions and attempts with mastery, the audit log and its MCP server. What 1.1 adds is practice in the web app, review scheduling, and records of where each source came from and which model embedded it.

- [What was built](https://github.com/danielcrean4/talk-to-rag/issues?q=milestone%3A1.1+is%3Aclosed)
- [What is left](https://github.com/danielcrean4/talk-to-rag/issues?q=milestone%3A1.1+is%3Aopen)

`1.1.0` is tagged. Bugs found in it are filed on the 1.1 milestone and released as `1.1.x`.

## 1.2

Phone-ready: the app usable from a phone on a private network. That needs the default secrets removed, sign-in hardened for accounts mode, short-lived file links instead of the sign-in token in the URL, and the web screens and concept graph laid out for small screens. The design is in [#51](https://github.com/danielcrean4/talk-to-rag/issues/51).

- [What was built](https://github.com/danielcrean4/talk-to-rag/issues?q=milestone%3A1.2+is%3Aclosed)
- [What is left](https://github.com/danielcrean4/talk-to-rag/issues?q=milestone%3A1.2+is%3Aopen)

## Quality backlog

Issues labelled `quality` came from a code review on 2026-11-16. They are on no milestone; one or two are picked up in each release.

- [Open](https://github.com/danielcrean4/talk-to-rag/issues?q=label%3Aquality+is%3Aopen)

## Later

Ideas that are on no milestone because they have not been designed yet. None at the moment.

## How issues are written

- One issue per feature or bug. A closed issue names the commits that did the work. Issues closed before `1.0.0` give dates only, because the history starts at that tag.
- A bug says when and how it was seen, or says that it was found by reading the code and has not been reproduced.
- No claim without something behind it: a commit, a test count, a query result. If something was not tested, the issue says so.
- Plain words. The cleanup rule in `.agents/skills/deslop/SKILL.md` applies to issues and docs as well as code.
