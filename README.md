# talk-to-rag

Ask questions about your own documents out loud and hear answers back, with citations to the page they came from.

**Status:** early. There is nothing to run yet; this README is the plan.

## Why

I'm working through deep learning, reinforcement learning, machine learning, statistics and probability over the next ten weeks. I want to quiz my textbooks and notes hands-free: ask "what's the difference between TD and Monte Carlo methods?" and get a short spoken answer that points to the chapter and page.

## How it will work

```
mic → speech-to-text → retrieve from your sources → answer with citations → text-to-speech → speaker
```

- **Sources are config.** Point it at local folders of PDFs, ebooks or notes, or at a list of URLs. Private sources live in a gitignored config and are never uploaded.
- **Ingest:** parse PDF, EPUB, HTML and Markdown, keeping equations as LaTeX where possible, and split into chunks tagged with document, chapter and page.
- **Retrieve:** embed chunks and search them; every answer cites its sources.
- **Voice:** speech-to-text in and text-to-speech out, with local models as the default so documents and audio stay on your machine.
- **Evaluate:** a labeled set of questions with known source pages, scored on hit@k and MRR, run automatically on every change.
- **Agents:** an MCP server so coding agents can search the same index.

## Where it runs

Local first. An optional cloud deployment on Google Cloud's free tier, defined in Terraform, is planned for public document sets.

## Roadmap

- [ ] Source config and ingestion for PDFs and Markdown
- [ ] Chunking with page citations
- [ ] Embedding and retrieval, with a CLI to ask questions by text
- [ ] Retrieval evaluation set and CI
- [ ] Voice in and out
- [ ] MCP server
- [ ] Terraform deployment on Google Cloud's free tier

## License

MIT
