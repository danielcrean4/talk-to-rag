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
| [pypdf](https://github.com/py-pdf/pypdf) | 6.21.4 | BSD-3-Clause | Page-by-page PDF text extraction in `ingestion/adapters/pypdf_parser.py` | © Mathieu Fenniak and contributors |
| [uv](https://github.com/astral-sh/uv) (copied into the image) | 0.13.6 | MIT or Apache-2.0 | Installs dependencies at build time | © Astral Software Inc. |
| [Hatchling](https://github.com/pypa/hatch) | build backend | MIT | Builds the `talk-to-rag` package | © the Hatch contributors |

### .NET API (`services/api`)

| What | Version | Licence | Where | Attribution |
|---|---|---|---|---|
| [.NET](https://github.com/dotnet/runtime) and [ASP.NET Core](https://github.com/dotnet/aspnetcore) (`mcr.microsoft.com/dotnet/aspnet:10.0` image) | 10.0 | MIT; image base is Debian | Runtime, web API | © .NET Foundation and contributors |
| Microsoft.AspNetCore.OpenApi | 10.0.14 | MIT | `/openapi/v1.json` | © .NET Foundation and contributors |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.14 | MIT | Validating sign-in tokens | © .NET Foundation and contributors |
| Microsoft.Extensions.Identity.Core | 10.0.14 | MIT | `PasswordHasher<User>` (PBKDF2) | © .NET Foundation and contributors |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.14 | MIT | Binding `JwtOptions` | © .NET Foundation and contributors |
| [Microsoft.IdentityModel.JsonWebTokens](https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet) | 8.23.0 | MIT | Creating signed JWTs | © Microsoft Corporation |
| [Entity Framework Core](https://github.com/dotnet/efcore) | 10.0 | MIT | ORM and migrations | © .NET Foundation and contributors |
| [Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) | 10.0.3 | [PostgreSQL License](https://github.com/npgsql/efcore.pg/blob/main/LICENSE) | EF Core provider for Postgres | © The Npgsql Development Team |
| [Pgvector.EntityFrameworkCore](https://github.com/pgvector/pgvector-dotnet) | 0.3.0 | MIT | `vector` type and HNSW index in EF Core | © Andrew Kane |
| [EFCore.NamingConventions](https://github.com/efcore/EFCore.NamingConventions) | 10.0.1 | Apache-2.0 | snake_case table and column names | © Shay Rojansky and contributors |

## Models (run by Ollama on the host, not shipped)

Weights are downloaded by each user with `ollama pull`; talk-to-rag does not redistribute them.

| What | Status | Licence | Where | Attribution |
|---|---|---|---|---|
| [nomic-embed-text](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5) | In use | Apache-2.0 | Default embedding model (`local-embed`), 768 dimensions | © Nomic AI |
| [Gemma 3](https://ai.google.dev/gemma) 1B (`gemma3:1b`) | In use | [Gemma Terms of Use](https://ai.google.dev/gemma/terms) (not an open-source licence) | Default chat model (`local-small`) | Google DeepMind. Redistributing the weights would require passing on the Gemma Terms and Prohibited Use Policy. |
| Gemma 3 4B (`gemma3:4b`) | Configured, not yet used | Gemma Terms of Use | `local-4b`, `remote-4b` | Google DeepMind |
| [Phi-4-mini](https://huggingface.co/microsoft/Phi-4-mini-instruct) (`phi4-mini`) | Configured, not yet used | MIT | `local-phi` | © Microsoft Corporation |

## Development and test tools (not shipped)

| What | Version | Licence | Where |
|---|---|---|---|
| [Ollama](https://github.com/ollama/ollama) | 0.40.13 | MIT | Serves local models on the host |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) and Docker Compose | Engine 29.9.2 | Docker Subscription Service Agreement (Desktop); Apache-2.0 (Engine, Compose) | Local stack (`compose.yaml`) |
| [.NET SDK](https://github.com/dotnet/sdk) (`mcr.microsoft.com/dotnet/sdk:10.0` build image) | 10.0.403 | MIT | Building the API |
| dotnet-ef (Microsoft.EntityFrameworkCore.Design) | 10.0.14 | MIT | Creating and applying migrations |
| [xUnit](https://github.com/xunit/xunit) | 2.9.3 (runner 3.1.4) | Apache-2.0 | .NET tests |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | .NET test host |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) | 6.0.4 | MIT | .NET code coverage |
| [pytest](https://github.com/pytest-dev/pytest) | 9.2.0 | MIT | Python tests |
| [HTTPX](https://github.com/encode/httpx) | 0.28.1 | BSD-3-Clause | FastAPI test client |
| [Homebrew](https://brew.sh) | | BSD-2-Clause | Installing tools on macOS |

## Internal provenance

- **AI-assisted code and docs.** Written with Claude Code (Anthropic, model Claude Opus 5.5) between 2026-10-30 and 2026-11-01, directed by Dan in conversation; every such commit carries a `Co-Authored-By: Claude Opus 5.5` trailer. `docs/architecture.md` was drafted in a separate AI-assisted planning session, in either Google Antigravity (Gemini) or Claude (claude.ai chat); which one, and the model, wasn't recorded at the time. Later edits were made with Claude Code.
- **UI prototype (outside the repo).** A clickable mockup made as a Claude artifact uses the [Inter](https://github.com/rsms/inter) and [IBM Plex Mono](https://github.com/IBM/plex) fonts (both SIL Open Font License 1.1, via Google Fonts). Credit them again if they reach the Angular app.
- **Sample text and test fixtures.** Passages and files used in tests and demos (TD learning, Monte Carlo, banjo tuning, and `services/ai/tests/fixtures/*.pdf/md/txt`) were created for this project, not quoted from a book.
