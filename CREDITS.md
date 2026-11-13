# Credits

talk-to-rag is built on other people's work. This file records every third-party tool, library, model and service it uses: what it is, the version in use, its licence, where it's used, and how to attribute it. Add an entry in the same change that introduces something new.

Versions are the ones locked or pinned when the entry was written; `services/ai/uv.lock` and the `.csproj` files are the source of truth.

## Shipped in the Docker images

### Database

| What | Version | Licence | Where | Attribution |
|---|---|---|---|---|
| [PostgreSQL](https://www.postgresql.org) | 17.11 | [PostgreSQL License](https://www.postgresql.org/about/licence/) | `postgres` service | © The PostgreSQL Global Development Group |
| [pgvector](https://github.com/pgvector/pgvector) | 0.8.7 (`pgvector/pgvector:pg17` image) | [PostgreSQL License](https://github.com/pgvector/pgvector/blob/master/LICENSE) | `vector` column type and HNSW index on `source_chunks` | © Andrew Kane |

### Python AI service (`services/ai`)

| What | Version | Licence | Where | Attribution |
|---|---|---|---|---|
| [Python](https://www.python.org) (`python:3.13-slim` image) | 3.13 | [PSF License](https://docs.python.org/3/license.html); image base is Debian (mixed free licences) | Runtime | © Python Software Foundation |
| [FastAPI](https://github.com/fastapi/fastapi) | 0.162.0 | MIT | HTTP app, OpenAPI contract | © Sebastián Ramírez |
| [Starlette](https://github.com/encode/starlette) (via FastAPI) | 1.10.0 | BSD-3-Clause | Web framework under FastAPI | © Encode OSS Ltd. |
| [Pydantic](https://github.com/pydantic/pydantic) (via FastAPI) | 2.14.2 | MIT | Request/response schemas | © Pydantic Services Inc. and contributors |
| [Uvicorn](https://github.com/encode/uvicorn) | 0.54.0 | BSD-3-Clause | ASGI server | © Encode OSS Ltd. |
| [OpenAI Python library](https://github.com/openai/openai-python) | 3.41.0 | Apache-2.0 | Client for any OpenAI-compatible server (Ollama) in `llm/adapters/openai_compatible.py` | © OpenAI |
| [pypdf](https://github.com/py-pdf/pypdf) | 6.21.4 | BSD-3-Clause | Page labels and the encryption check in `ingestion/adapters/pdf_parser.py` | © Mathieu Fenniak and contributors |
| [pypdfium2](https://github.com/pypdfium2-team/pypdfium2) | 5.15.2 | Apache-2.0 or BSD-3-Clause; bundles [PDFium](https://pdfium.googlesource.com/pdfium/) (BSD-3-Clause and Apache-2.0) | Page-by-page PDF text extraction in `ingestion/adapters/pdf_parser.py` | © pypdfium2 contributors; PDFium © The PDFium Authors |
| [faster-whisper](https://github.com/SYSTRAN/faster-whisper) (optional `speech` extra) | 1.2.1 | MIT | Local speech-to-text in `speech/adapters/faster_whisper.py` | © SYSTRAN |
| [CTranslate2](https://github.com/OpenNMT/CTranslate2), [PyAV](https://github.com/PyAV-Org/PyAV) (via faster-whisper) | see `uv.lock` | MIT, BSD-3-Clause | Whisper inference and audio decoding (PyAV pinned `<19`) | © OpenNMT, PyAV contributors |
| [Whisper `base`](https://github.com/openai/whisper) (CTranslate2 conversion [Systran/faster-whisper-base](https://huggingface.co/Systran/faster-whisper-base)) | base, about 141 MB | MIT | Speech model, downloaded on first use into the `models` volume | © OpenAI |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) (optional `speech` extra) | 1.31.1 | MIT | Runs the Kokoro model on CPU in `speech/adapters/kokoro.py` | © Microsoft Corporation |
| [misaki](https://github.com/hexgrad/misaki) (optional `speech` extra, without its `en` extra) | 0.9.4 | Apache-2.0 | Turns English text into phonemes for Kokoro; installed bare so GPL espeak-ng is not pulled in | hexgrad |
| [spaCy](https://github.com/explosion/spaCy) (via misaki) and its explosion.ai helpers (thinc, srsly, catalogue, confection, cymem, preshed, murmurhash, blis, wasabi, weasel) | 3.8.18; see `uv.lock` | MIT; blis BSD-3-Clause | Tokenising and tagging text before phonemes are looked up | © ExplosionAI GmbH |
| [en_core_web_sm](https://github.com/explosion/spacy-models/releases/tag/en_core_web_sm-3.8.0) | 3.8.0 | MIT (per the model's `meta.json`) | English pipeline misaki loads, installed from the release wheel at build time | © ExplosionAI GmbH |
| [num2words](https://github.com/savoirfairelinux/num2words) (optional `speech` extra) | 0.5.14 | LGPL-2.1 | Reads numbers as words for misaki, used unmodified as a library | © Savoir-faire Linux and contributors |
| [NumPy](https://github.com/numpy/numpy) (via onnxruntime) | 2.5.4 | BSD-3-Clause (bundled parts 0BSD, MIT, Zlib, CC0-1.0) | Audio samples and model inputs in `speech/adapters/kokoro.py` | © NumPy Developers |
| [huggingface_hub](https://github.com/huggingface/huggingface_hub) (via faster-whisper) | 2.4.0 | Apache-2.0 | Downloads the Kokoro model and voices at a pinned revision | © Hugging Face, Inc. |
| Other packages the `speech` extra pulls in for misaki and spaCy: addict, regex, docopt, spacy-legacy, spacy-loggers, typer, rich, markdown-it-py, mdurl, shellingham, Jinja2, MarkupSafe, cloudpathlib, smart_open, wrapt, requests, urllib3, charset-normalizer, setuptools, flatbuffers, protobuf | see `uv.lock` | MIT, except: regex Apache-2.0 AND CNRI-Python; requests, flatbuffers Apache-2.0; Jinja2, MarkupSafe, protobuf BSD-3-Clause; wrapt BSD-2-Clause; shellingham ISC | Dependencies, used unmodified | Their respective authors |
| [Kokoro-82M v1.0](https://huggingface.co/hexgrad/Kokoro-82M), ONNX conversion [onnx-community/Kokoro-82M-v1.0-ONNX](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX) at revision `1939ad2a` | `model_quantized.onnx`, about 92 MB, plus one voice file per voice used | Apache-2.0 | Text-to-speech model, downloaded on first use into the `models` volume | hexgrad |
| [uv](https://github.com/astral-sh/uv) (copied into the image) | 0.13.6 | MIT or Apache-2.0 | Installs dependencies at build time | © Astral Software Inc. |
| [Hatchling](https://github.com/pypa/hatch) | build backend | MIT | Builds the `talk-to-rag` package | © the Hatch contributors |

### .NET API (`services/api`)

| What | Version | Licence | Where | Attribution |
|---|---|---|---|---|
| [.NET](https://github.com/dotnet/runtime) and [ASP.NET Core](https://github.com/dotnet/aspnetcore) (`mcr.microsoft.com/dotnet/aspnet:10.0` image) | 10.0 | MIT; image base is Debian | Runtime, web API | © .NET Foundation and contributors |
| Microsoft.AspNetCore.OpenApi | 10.0.14 | MIT | `/openapi/v1.json` | © .NET Foundation and contributors |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.14 | MIT | Validating sign-in tokens | © .NET Foundation and contributors |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.14 | MIT | `BackgroundService` for background source ingestion worker | © .NET Foundation and contributors |
| Microsoft.Extensions.Http | 10.0.14 | MIT | `AddHttpClient` for typed AI HTTP client | © .NET Foundation and contributors |
| Microsoft.Extensions.Identity.Core | 10.0.14 | MIT | `PasswordHasher<User>` (PBKDF2) | © .NET Foundation and contributors |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.14 | MIT | Binding `JwtOptions` | © .NET Foundation and contributors |
| [Microsoft.IdentityModel.JsonWebTokens](https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet) | 8.23.0 | MIT | Creating signed JWTs | © Microsoft Corporation |
| [Entity Framework Core](https://github.com/dotnet/efcore) | 10.0 | MIT | ORM and migrations | © .NET Foundation and contributors |
| [Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) | 10.0.3 | [PostgreSQL License](https://github.com/npgsql/efcore.pg/blob/main/LICENSE) | EF Core provider for Postgres | © The Npgsql Development Team |
| [Pgvector.EntityFrameworkCore](https://github.com/pgvector/pgvector-dotnet) | 0.3.0 | MIT | `vector` type and HNSW index in EF Core | © Andrew Kane |
| [EFCore.NamingConventions](https://github.com/efcore/EFCore.NamingConventions) | 10.0.1 | Apache-2.0 | snake_case table and column names | © Shay Rojansky and contributors |

### Web app (`web/`, built into the API image)

The production build collects these libraries' licence notices in `3rdpartylicenses.txt`, which the image serves next to the app.

| What | Version | Licence | Where | Attribution |
|---|---|---|---|---|
| [Angular](https://github.com/angular/angular) | 22.2 | MIT | Framework, router and forms of the web app | © Google LLC |
| [RxJS](https://github.com/ReactiveX/rxjs) | 7.8.2 | Apache-2.0 | HTTP streams in the web app | © Google, Inc., Netflix, Inc., Microsoft Corp. and contributors |
| [tslib](https://github.com/microsoft/tslib) | 2.9.1 | 0BSD | TypeScript runtime helpers in the web app | © Microsoft Corporation |

## Audit MCP server (`services/mcp`, run on the host, not shipped)

| What | Version | Licence | Where | Attribution |
|---|---|---|---|---|
| [MCP Python SDK](https://github.com/modelcontextprotocol/python-sdk) | 2.3.0 | MIT | Tool server in `services/mcp` that lets an outside model read the audit schema | © Anthropic, PBC and contributors |
| [Psycopg 3](https://github.com/psycopg/psycopg) | 3.3.9 | LGPL-3.0 | Postgres driver for `services/mcp`, used unmodified as a library | © The Psycopg Team |

## Models (run by Ollama on the host, not shipped)

Weights are downloaded by each user with `ollama pull`; talk-to-rag does not redistribute them.

| What | Status | Licence | Where | Attribution |
|---|---|---|---|---|
| [nomic-embed-text](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5) | In use | Apache-2.0 | Default embedding model (`local-embed`), 768 dimensions | © Nomic AI |
| [Gemma 3](https://ai.google.dev/gemma) 1B (`gemma3:1b`) | In use | [Gemma Terms of Use](https://ai.google.dev/gemma/terms) (not an open-source licence) | Query rewriting (`local-small`, the default chat model) | Google DeepMind. Redistributing the weights would require passing on the Gemma Terms and Prohibited Use Policy. |
| [Ministral 3 3B Instruct 2512](https://huggingface.co/mistralai/Ministral-3-3B-Instruct-2512-GGUF) (Q4_K_M GGUF) | In use | Apache-2.0 | Summaries, concept prerequisite proposals and flashcards (`local-3b`) | Mistral AI |
| Gemma 3 4B (`gemma3:4b`) | Configured, not yet used | Gemma Terms of Use | `local-4b`, `remote-4b` | Google DeepMind |
| [Phi-4-mini](https://huggingface.co/microsoft/Phi-4-mini-instruct) (`phi4-mini`) | Configured, not yet used | MIT | `local-phi` | © Microsoft Corporation |
| [Llama 3.2](https://github.com/meta-llama/llama-models) 1B (`llama3.2:1b`) | Configured, alternative | [Llama 3.2 Community License](https://github.com/meta-llama/llama-models/blob/main/models/llama3_2/LICENSE) | `local-llama` | © Meta Platforms, Inc. |

## Development and test tools (not shipped)

| What | Version | Licence | Where |
|---|---|---|---|
| [Ollama](https://github.com/ollama/ollama) | 0.40.13 | MIT | Serves local models on the host |
| Angular CLI and `@angular/build` | 22.2 | MIT | Building and serving the `web/` app |
| [Node.js](https://github.com/nodejs/node) (`node:24-bookworm-slim` build image) | 24 | MIT; image base is Debian | Building the `web/` app in the API image (`services/api/Dockerfile`); on the host only for `make web`, `make test-web`, `make lint` and `make test-e2e` |
| [TypeScript](https://github.com/microsoft/TypeScript) | 6.0.3 | Apache-2.0 | Compiling `web/` |
| [Vitest](https://github.com/vitest-dev/vitest) and [jsdom](https://github.com/jsdom/jsdom) | 5.1.0, 31.0.0 | MIT | `web/` unit tests |
| [Playwright](https://github.com/microsoft/playwright) | 1.65.0 | Apache-2.0 | `web/e2e` browser tests |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) and Docker Compose | Engine 29.9.2 | Docker Subscription Service Agreement (Desktop); Apache-2.0 (Engine, Compose) | Local stack (`compose.yaml`) |
| [.NET SDK](https://github.com/dotnet/sdk) (`mcr.microsoft.com/dotnet/sdk:10.0` build image) | 10.0.403 | MIT | Building the API |
| dotnet-ef (Microsoft.EntityFrameworkCore.Design) | 10.0.14 | MIT | Creating and applying migrations |
| [Testcontainers for .NET](https://dotnet.testcontainers.org) | 4.16.0 | MIT | Real PostgreSQL + pgvector container integration tests in `TalkToRag.IntegrationTests` |
| [xUnit](https://github.com/xunit/xunit) | 2.9.3 (runner 3.1.4) | Apache-2.0 | .NET tests |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | .NET test host |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) | 6.0.4 | MIT | .NET code coverage |
| [pytest](https://github.com/pytest-dev/pytest) | 9.2.0 | MIT | Python tests |
| [HTTPX](https://github.com/encode/httpx) | 0.28.1 | BSD-3-Clause | FastAPI test client |
| [Ruff](https://github.com/astral-sh/ruff) | 0.17.5 | MIT | Python lint and formatting (`make lint`) |
| [Prettier](https://github.com/prettier/prettier) | 3.8 | MIT | Formatting of the `web/` app |
| [Homebrew](https://brew.sh) | | BSD-2-Clause | Installing tools on macOS |

## How the project was written

- **AI-assisted code and docs.** Most of the code and documentation was written with AI coding tools, mainly Claude Code (Anthropic), under human direction and review. Commits written that way carry a `Co-Authored-By` trailer naming the model.
- **UI prototype (outside the repo).** The first clickable mockup used the [Inter](https://github.com/rsms/inter) and [IBM Plex Mono](https://github.com/IBM/plex) fonts (both SIL Open Font License 1.1, via Google Fonts). Credit them again if they reach the Angular app.
- **Sample text and test fixtures.** Passages and files used in tests and demos (TD learning, Monte Carlo, banjo tuning, and `services/ai/tests/fixtures/*.pdf/md/txt`) were created for this project, not quoted from a book.
