"""The AI service's HTTP app.

uv run uvicorn talk_to_rag.api.app:create_app --factory --reload --port 8000
"""

from fastapi import Depends, FastAPI

from talk_to_rag.api.routes import concepts, health, ingestion, rag, speech
from talk_to_rag.tracing import start_trace


def create_app() -> FastAPI:
    app = FastAPI(
        title="talk-to-rag AI service",
        summary="Stateless model work for the .NET API: ingestion, retrieval helpers and speech.",
        dependencies=[Depends(start_trace)],
    )
    app.include_router(health.router)
    app.include_router(ingestion.router)
    app.include_router(rag.router)
    app.include_router(speech.router)
    app.include_router(concepts.router)
    return app
