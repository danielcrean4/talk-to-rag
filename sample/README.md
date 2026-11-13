# Sample documents

Four short texts for trying the app without loading anything of your own. All were published in the
United States before 1900 and are in the public domain:

| File | Text | Date |
|---|---|---|
| `declaration-of-independence.md` | The Declaration of Independence | 1776 |
| `us-constitution.md` | The Constitution of the United States, without the amendments | 1787 |
| `gettysburg-address.md` | Abraham Lincoln, the Gettysburg Address | 1863 |
| `lincoln-second-inaugural.md` | Abraham Lincoln, Second Inaugural Address | 1865 |

No one holds copyright in these texts, and the files here carry no licence terms of their own: use
them as you like.

The transcriptions were taken from Project Gutenberg ebooks
[1](https://www.gutenberg.org/ebooks/1), [5](https://www.gutenberg.org/ebooks/5),
[4](https://www.gutenberg.org/ebooks/4) and [8](https://www.gutenberg.org/ebooks/8), each listed
there as "Public domain in the USA". Its notes, licence text and name were removed from the files,
and Markdown headings were added.

With the stack running (`make up`), load them:

```bash
make sample
```

By default the app has no sign-in, and the files go to the one local user. If accounts are turned on
(`TALK_TO_RAG_SINGLE_USER=false`), the script creates `sample@talk-to-rag.local` with the password
`sample-local-only` and loads them there. Files that are already loaded are skipped.
