# `talk-to-rag` Architecture Blueprint & Implementation Plan

## Goal Description

`talk-to-rag` is a voice-enabled Retrieval-Augmented Generation (RAG) research and study platform designed around **Notebooks**, **Sources**, **Hybrid Search (`pgvector` + PostgreSQL Full-Text Search)**, and **Grounded Citations**.

**Product focus: sources first, retrieval first.** The main question the app answers is *which of my sources cover this, and where in them* (source, page, section, snippet). A model-written summary of the found passages is optional, layered on top. Chat is not the main screen.

It demonstrates a clean, polyglot enterprise architecture (**C# / ASP.NET Core**, **Python / FastAPI**, **Angular**, **PostgreSQL + `pgvector`**, and **Docker Compose**) with strict separation of concerns:
1. **Cross-Boundary Ports:** `IBlobStorage` / `LocalBlobStorage` (paired with Python `BlobStorage` / `LocalBlobStorage`), `IAiClient` / `HttpAiClient`, `ICurrentUser` / `ClaimsCurrentUser`, `ITokenGenerator` / `JwtTokenGenerator`.
2. **Domain Model (`TalkToRag.Domain`):**
   - **Root User-Owned Entities (carrying `UserId`):** `Notebook`, `Source`, `Tag`, `Conversation`.
   - **Child Entities (owned by parent FK):** `SourceChunk`, `Message`, `MessageCitation`.
   - **Many-to-Many Join Entities (`0..N`):** `NotebookSource`, `SourceTag`, `ConversationNotebook`, `ConversationTag` (wrapped in `ConversationScope`).
3. **Application, Ingestion & Persistence:** `ISourceIngestionQueue` + `ChannelSourceIngestionQueue` + `SourceIngestionWorker : BackgroundService`, `TalkToRagDbContext` (managing `HasPostgresExtension("vector")` directly in EF Core migrations), and feature modules (`Auth`, `Notebooks`, `Sources`, `Tags`, `Search`, `Conversations`).
4. **API Controllers (`TalkToRag.Api`):** `HealthController`, `AuthController`, `NotebooksController`, `SourcesController`, `TagsController`, `SearchController`, `ConversationsController`, `SpeechController`.
5. **Python Stateless AI Service (`services/ai`):** Capability modules `llm/`, `storage/`, `ingestion/`, `rag/`, `speech/`, `evals/`, and `api/` (`GET /health`, `POST /v1/sources/ingest`, `POST /v1/rag/expand-query`, `POST /v1/rag/answer`, `POST /v1/speech/transcribe`, `POST /v1/speech/synthesize`).

---

## System Architecture & Workflows

### Container & Process Topology (`compose.yaml` + Native Ollama)

```mermaid
flowchart TB
    subgraph Host["macOS Host (M1 MacBook Air / Mac mini via SSH Tunnel)"]
        Ollama["Ollama Server (localhost:11434)\ngemma3:1b | gemma3:4b | phi4-mini | nomic-embed-text"]
    end

    subgraph Docker["Docker Compose Bridge Network (talk-to-rag)"]
        Web["web (Angular SPA)\nPort 4200"]
        Api["services/api (ASP.NET Core Web API)\nPort 5000"]
        Ai["services/ai (Python FastAPI)\nPort 8000 (Internal Network)"]
        Db[("postgres (pgvector/pgvector:pg17)\nPort 5432")]
        Blobs[("Shared Docker Volume (/var/talk-to-rag/blobs)\nsources/{uuid}.pdf | audio/{uuid}.webm")]
    end

    Web -- "HTTP / SSE (JWT Bearer)" --> Api
    Api -- "EF Core Migrations + Hybrid SQL" --> Db
    Api -- "IBlobStorage (Write/Read by Key)" --> Blobs
    Api -- "IAiClient HTTP (BlobKey handoff)" --> Ai
    Ai -- "BlobStorage (Read/Write by Key)" --> Blobs
    Ai -- "OpenAI-Compatible HTTP\nhost.docker.internal:11434" --> Ollama
```

### End-to-End Sequence Flows

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Web as Angular (web)
    participant Api as .NET API (services/api)
    participant Blobs as Shared BlobStorage
    participant Db as Postgres + pgvector
    participant Ai as Python AI (services/ai)

    rect rgb(235, 245, 255)
    Note over User,Ai: Flow A: Source Upload & Async Ingestion (BlobKey Handoff)
    User->>Web: Upload PDF + optional NotebookIds & TagIds
    Web->>Api: POST /api/sources (multipart/form-data)
    Api->>Blobs: WriteAsync("sources/{sourceId}.pdf", stream)
    Api->>Db: INSERT sources (status='Processing') + notebook_sources + source_tags
    Api->>Api: ISourceIngestionQueue.EnqueueAsync(sourceId)
    Api-->>Web: 202 Accepted { id, status: "Processing" }
    Note over Api: SourceIngestionWorker (BackgroundService) dequeues sourceId
    Api->>Ai: POST /v1/sources/ingest { source_id, blob_key: "sources/{sourceId}.pdf" }
    Ai->>Blobs: read_bytes("sources/{sourceId}.pdf")
    Note over Ai: Parse pages + LaTeX -> SemanticTextChunker -> Embed (nomic-embed-text)
    Ai-->>Api: 200 OK { chunks: [{ page_number, chunk_index, content, embedding }] }
    Api->>Db: Bulk INSERT source_chunks & UPDATE sources SET status='Ready'
    end

    rect rgb(240, 255, 240)
    Note over User,Ai: Flow B: Scoped Multi-Query RAG Conversation Turn
    User->>Web: Push-to-talk audio OR text question
    opt Voice Input
        Web->>Api: POST /api/speech/transcribe (audio blob)
        Api->>Blobs: WriteAsync("audio/{audioId}.webm", stream)
        Api->>Ai: POST /v1/speech/transcribe { blob_key: "audio/{audioId}.webm" }
        Ai->>Blobs: read_bytes("audio/{audioId}.webm") & run Whisper
        Ai-->>Api: { transcript }
        Api-->>Web: { audioBlobKey, transcript } (Editable before sending)
    end
    Web->>Api: POST /api/conversations/{id}/messages { content, audioBlobKey? }
    Api->>Db: Load conversation history & ConversationScope (Notebooks + Tags)
    Api->>Ai: POST /v1/rag/expand-query { question, recent_messages }
    Note over Ai: Rewrite into 2-3 standalone search queries & embed each
    Ai-->>Api: { queries: [{ text, embedding }] }
    Api->>Db: Hybrid Search SQL (pgvector cosine <=> + Postgres FTS ts_rank + RRF)<br/>WHERE s.user_id = @userId AND matches ConversationScope
    Db-->>Api: Top K matching SourceChunks (with Source Title & Page Number)
    Api->>Ai: POST /v1/rag/answer (SSE stream) { question, history, cited_chunks }
    Ai-->>Api: Stream answer tokens + cited chunk IDs
    Api-->>Web: Stream answer tokens + citations
    Api->>Db: INSERT user Message, assistant Message & MessageCitations
    end
```

---

## Monorepo Folder Structure & Naming Inventory

```text
talk-to-rag/
├── compose.yaml                                    # Postgres (pgvector:pg17), services/ai, services/api, web
├── Makefile                                        # make up, make down, make test, make migrate
├── README.md                                       # Architecture overview, quickstart, roadmap
├── .gitignore
├── docs/
│   └── architecture.md                             # Detailed system & RAG design notes
├── deploy/
│   └── terraform/                                  # Reserved for future Cloud (GCP/AWS/Azure) IaC
│
├── services/
│   ├── ai/                                         # Python 3.12+ Stateless AI Service (uv + FastAPI)
│   │   ├── Dockerfile
│   │   ├── pyproject.toml
│   │   ├── uv.lock
│   │   ├── config/
│   │   │   ├── models.toml                         # US-origin defaults (gemma3:1b, gemma3:4b, phi4-mini, nomic)
│   │   │   ├── models.local.example.toml           # Template for SSH-tunneled Mac mini / private hosts
│   │   │   └── models.local.toml                   # Gitignored machine-local overrides
│   │   ├── src/talk_to_rag/
│   │   │   ├── __init__.py
│   │   │   ├── api/
│   │   │   │   ├── __init__.py
│   │   │   │   ├── app.py                          # create_app() FastAPI factory
│   │   │   │   ├── schemas.py                      # IngestSourceRequest/Response, ExpandQueryRequest/Response, etc.
│   │   │   │   └── routes/
│   │   │   │       ├── __init__.py
│   │   │   │       ├── health.py                   # GET /health
│   │   │   │       ├── ingestion.py                # POST /v1/sources/ingest
│   │   │   │       ├── rag.py                      # POST /v1/rag/expand-query, POST /v1/rag/answer
│   │   │   │       └── speech.py                   # POST /v1/speech/transcribe, POST /v1/speech/synthesize
│   │   │   ├── llm/                                # Existing LLM & Embedder layer
│   │   │   │   ├── __init__.py
│   │   │   │   ├── __main__.py
│   │   │   │   ├── contracts.py                    # ChatModel, Embedder, Message, Role, ModelError
│   │   │   │   ├── config.py
│   │   │   │   ├── factory.py
│   │   │   │   └── adapters/
│   │   │   │       ├── __init__.py
│   │   │   │       ├── openai_compatible.py
│   │   │   │       └── fake.py
│   │   │   ├── storage/                            # Shared blob storage abstraction
│   │   │   │   ├── __init__.py
│   │   │   │   ├── contracts.py                    # BlobStorage Protocol, StorageError
│   │   │   │   └── adapters/
│   │   │   │       ├── __init__.py
│   │   │   │       └── local.py                    # LocalBlobStorage
│   │   │   ├── ingestion/                          # Source parsing & semantic chunking
│   │   │   │   ├── __init__.py
│   │   │   │   ├── contracts.py                    # SourceParser, TextChunker, ParsedPage, ChunkPayload, IngestionError
│   │   │   │   └── adapters/
│   │   │   │       └── __init__.py
│   │   │   ├── rag/                                # Query expansion & grounded citation generation
│   │   │   │   ├── __init__.py
│   │   │   │   ├── contracts.py                    # QueryExpander, AnswerGenerator, ExpandedQuery, CitedChunk
│   │   │   │   └── adapters/
│   │   │   │       └── __init__.py
│   │   │   ├── speech/                             # Whisper STT & Piper TTS
│   │   │   │   ├── __init__.py
│   │   │   │   ├── contracts.py                    # SpeechTranscriber, SpeechSynthesizer, SpeechError
│   │   │   │   └── adapters/
│   │   │   │       └── __init__.py
│   │   │   └── evals/                              # Retrieval evaluation (hit@k, MRR)
│   │   │       ├── __init__.py
│   │   │       └── contracts.py                    # RetrievalEvalCase, EvalMetrics
│   │   └── tests/
│   │       ├── test_llm.py
│   │       ├── test_storage.py
│   │       └── test_api_health.py
│   │
│   └── api/                                        # C# / ASP.NET Core Clean Architecture Solution
│       ├── Dockerfile
│       ├── TalkToRag.slnx                              # .NET 10 XML solution format
│       ├── src/
│       │   ├── TalkToRag.Domain/
│       │   │   ├── TalkToRag.Domain.csproj
│       │   │   ├── Entities/
│       │   │   │   ├── User.cs
│       │   │   │   ├── Notebook.cs
│       │   │   │   ├── Source.cs
│       │   │   │   ├── SourceChunk.cs
│       │   │   │   ├── Tag.cs
│       │   │   │   ├── NotebookSource.cs
│       │   │   │   ├── SourceTag.cs
│       │   │   │   ├── Conversation.cs
│       │   │   │   ├── ConversationNotebook.cs
│       │   │   │   ├── ConversationTag.cs
│       │   │   │   ├── Message.cs
│       │   │   │   └── MessageCitation.cs
│       │   │   ├── Enums/
│       │   │   │   ├── SourceStatus.cs             # Pending, Processing, Ready, Failed
│       │   │   │   └── MessageRole.cs              # User, Assistant
│       │   │   └── Exceptions/
│       │   │       ├── DomainException.cs
│       │   │       ├── NotFoundException.cs
│       │   │       └── ForbiddenAccessException.cs
│       │   │
│       │   ├── TalkToRag.Application/
│       │   │   ├── TalkToRag.Application.csproj
│       │   │   ├── DependencyInjection.cs
│       │   │   ├── Common/
│       │   │   │   ├── Interfaces/
│       │   │   │   │   ├── IBlobStorage.cs
│       │   │   │   │   ├── IAiClient.cs
│       │   │   │   │   ├── ICurrentUser.cs
│       │   │   │   │   ├── ITokenGenerator.cs
│       │   │   │   │   └── ISourceIngestionQueue.cs
│       │   │   │   └── Models/
│       │   │   │       └── ConversationScope.cs    # Wrapper: NotebookIds, TagIds, SourceIds lists
│       │   │   ├── Auth/
│       │   │   │   ├── AuthContracts.cs
│       │   │   │   ├── IAuthService.cs
│       │   │   │   └── AuthService.cs
│       │   │   ├── Notebooks/
│       │   │   │   ├── NotebookContracts.cs
│       │   │   │   ├── INotebookService.cs
│       │   │   │   └── NotebookService.cs
│       │   │   ├── Sources/
│       │   │   │   ├── SourceContracts.cs
│       │   │   │   ├── ISourceService.cs
│       │   │   │   └── SourceService.cs
│       │   │   ├── Tags/
│       │   │   │   ├── TagContracts.cs
│       │   │   │   ├── ITagService.cs
│       │   │   │   └── TagService.cs
│       │   │   ├── Search/
│       │   │   │   ├── SearchContracts.cs
│       │   │   │   ├── ISearchService.cs
│       │   │   │   └── SearchService.cs
│       │   │   └── Conversations/
│       │   │       ├── ConversationContracts.cs
│       │   │       ├── IConversationService.cs
│       │   │       └── ConversationService.cs
│       │   │
│       │   ├── TalkToRag.Infrastructure/
│       │   │   ├── TalkToRag.Infrastructure.csproj
│       │   │   ├── DependencyInjection.cs
│       │   │   ├── Persistence/
│       │   │   │   ├── TalkToRagDbContext.cs
│       │   │   │   ├── Configurations/
│       │   │   │   │   ├── UserConfiguration.cs
│       │   │   │   │   ├── NotebookConfiguration.cs
│       │   │   │   │   ├── SourceConfiguration.cs
│       │   │   │   │   ├── SourceChunkConfiguration.cs
│       │   │   │   │   ├── TagConfiguration.cs
│       │   │   │   │   ├── NotebookSourceConfiguration.cs
│       │   │   │   │   ├── SourceTagConfiguration.cs
│       │   │   │   │   ├── ConversationConfiguration.cs
│       │   │   │   │   ├── ConversationNotebookConfiguration.cs
│       │   │   │   │   ├── ConversationTagConfiguration.cs
│       │   │   │   │   ├── MessageConfiguration.cs
│       │   │   │   │   └── MessageCitationConfiguration.cs
│       │   │   │   └── Migrations/
│       │   │   ├── Storage/
│       │   │   │   └── LocalBlobStorage.cs
│       │   │   ├── Ai/
│       │   │   │   └── HttpAiClient.cs
│       │   │   ├── Ingestion/
│       │   │   │   ├── ChannelSourceIngestionQueue.cs
│       │   │   │   └── SourceIngestionWorker.cs
│       │   │   └── Security/
│       │   │       ├── ClaimsCurrentUser.cs
│       │   │       └── JwtTokenGenerator.cs
│       │   │
│       │   └── TalkToRag.Api/
│       │       ├── TalkToRag.Api.csproj
│       │       ├── Program.cs
│       │       ├── appsettings.json
│       │       ├── appsettings.Development.json
│       │       └── Controllers/
│       │           ├── HealthController.cs
│       │           ├── AuthController.cs
│       │           ├── NotebooksController.cs
│       │           ├── SourcesController.cs
│       │           ├── TagsController.cs
│       │           ├── SearchController.cs
│       │           ├── ConversationsController.cs
│       │           └── SpeechController.cs
│       │
│       └── tests/
│           └── TalkToRag.UnitTests/
│               └── TalkToRag.UnitTests.csproj
│
└── web/                                            # Angular SPA
    ├── Dockerfile
    ├── package.json
    ├── angular.json
    └── src/
```

---

## PostgreSQL Relational & Vector Schema (`TalkToRagDbContext`)

```mermaid
erDiagram
    users ||--o{ notebooks : "owns (user_id)"
    users ||--o{ sources : "owns (user_id)"
    users ||--o{ tags : "owns (user_id)"
    users ||--o{ conversations : "owns (user_id)"

    notebooks ||--o{ notebook_sources : "links"
    sources ||--o{ notebook_sources : "linked in (0..N)"

    sources ||--o{ source_tags : "tagged with"
    tags ||--o{ source_tags : "applied to (0..N)"
    sources ||--o{ source_chunks : "split into"

    conversations ||--o{ conversation_notebooks : "scoped to"
    notebooks ||--o{ conversation_notebooks : "in scope"
    conversations ||--o{ conversation_tags : "scoped to"
    tags ||--o{ conversation_tags : "in scope"
    conversations ||--o{ messages : "has turns"

    messages ||--o{ message_citations : "cites"
    source_chunks ||--o{ message_citations : "cited in"

    users {
        uuid id PK
        text email UK
        text display_name
        text password_hash
        timestamptz created_at_utc
    }
    notebooks {
        uuid id PK
        uuid user_id FK
        text name
        text description
        timestamptz created_at_utc
    }
    sources {
        uuid id PK
        uuid user_id FK
        text title
        text file_name
        text content_type
        bigint size_bytes
        text blob_key
        text status "Pending | Processing | Ready | Failed"
        text failure_reason
        timestamptz created_at_utc
    }
    source_chunks {
        uuid id PK
        uuid source_id FK
        int page_number
        int chunk_index
        text content
        tsvector search_vector "Generated FTS GIN index"
        vector_768 embedding "pgvector HNSW cosine index"
    }
    tags {
        uuid id PK
        uuid user_id FK
        text name
        timestamptz created_at_utc
    }
    conversations {
        uuid id PK
        uuid user_id FK
        text title
        timestamptz created_at_utc
        timestamptz updated_at_utc
    }
    messages {
        uuid id PK
        uuid conversation_id FK
        text role "User | Assistant"
        text content
        text audio_blob_key
        timestamptz created_at_utc
    }
```

---

## Multi-Milestone Roadmap

| Milestone | Scope | Key Concepts |
|---|---|---|
| **Milestone 1** | Monorepo layout (`services/ai`, `services/api`, `web`), US-origin model config, Shared `BlobStorage`, Python FastAPI scaffold, .NET Clean Architecture + EF Core `pgvector` schema + JWT Auth, `compose.yaml` | Clean Architecture ports/adapters, `pgvector` HNSW indexes, Postgres `tsvector` GIN indexes, shared blob storage by UUID |
| **Milestone 2** | Source Upload (`POST /api/sources`), `SourceIngestionWorker`, Python PDF/Markdown parsing & semantic chunking (`POST /v1/sources/ingest`) | Chunk size & overlap trade-offs, preserving page citations & LaTeX equations, batch embedding |
| **Milestone 3** | Hybrid Search (`POST /api/search`) returning passages grouped by source with page/section locations, then an optional grounded summary of those passages (`POST /api/conversations/{id}/messages`, which also keeps the history of what was asked and found) | Cosine distance (`<=>`), Reciprocal Rank Fusion (RRF), multi-query expansion, pre-filtered `ConversationScope` (`Notebook` & `Tag` filters), grounded citation prompting |
| **Milestone 4** | Angular UI matching the sources-first prototype: Sources grid (file icon, name, description on hover), drag-and-drop upload and drag-to-notebook, top-centred view toggles (Sources / Results / History), floating search bar with a prominent push-to-talk mic, Results as passages with "open at page" links, optional summary with read-aloud (Whisper STT & Piper TTS) | Streaming SSE responses in Angular, browser `MediaRecorder` push-to-talk workflow, HTML drag and drop |
| **Milestone 5** | Automated Evaluation Suite (`hit@k`, `MRR`) + GitHub Actions CI | Golden Q&A dataset, retrieval recall (`hit@k`) and ranking quality (`MRR`), regression gating in CI |
