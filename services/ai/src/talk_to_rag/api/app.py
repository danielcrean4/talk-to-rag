"""The AI service's HTTP app.

    uv run uvicorn talk_to_rag.api.app:create_app --factory --reload --port 8000
"""

from fastapi import FastAPI

from talk_to_rag.api.routes import health, ingestion, rag


def create_app() -> FastAPI:
    app = FastAPI(
        title="talk-to-rag AI service",
        summary="Stateless model work for the .NET API: ingestion, retrieval helpers and speech.",
    )
    app.include_router(health.router)
    app.include_router(ingestion.router)
    app.include_router(rag.router)
    return app
