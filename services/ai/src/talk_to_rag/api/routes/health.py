from importlib.metadata import version

from fastapi import APIRouter

from talk_to_rag.api.schemas import HealthResponse

router = APIRouter(tags=["health"])


@router.get("/health")
def health() -> HealthResponse:
    """Liveness only: the process is up and serving. It does not check Ollama or storage."""
    return HealthResponse(status="ok", service="talk-to-rag-ai", version=version("talk-to-rag"))
