---
name: deslop
description: Remove AI-generated code slop and clean up code style
---

# Remove AI code slop

Check the diff against main and remove AI-generated slop introduced in the branch.

## Focus Areas

- Extra comments that are unnecessary or inconsistent with local style
- Defensive checks or try/catch blocks that are abnormal for trusted code paths
- Casts to `any` used only to bypass type issues
- Deeply nested code that should be simplified with early returns
- Other patterns inconsistent with the file and surrounding codebase

## Prose: docs, issues, commit messages

- Every claim has something behind it: a commit, a test result, a query, a line of code. Remove what was never measured or checked, or say that it was not.
- Cut words that sell (robust, seamless, powerful, comprehensive) and say what the thing does.
- Say when and how a bug was seen. If it was found by reading the code, say that.
- One idea per sentence. Remove sentences that repeat the heading or the sentence before.

## Guardrails

- Keep behavior unchanged unless fixing a clear bug.
- Prefer minimal, focused edits over broad rewrites.
- Keep the final summary concise (1-3 sentences).
