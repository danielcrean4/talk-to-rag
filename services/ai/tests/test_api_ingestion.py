import uuid
from pathlib import Path

from fastapi.testclient import TestClient

from talk_to_rag.api.app import create_app
from talk_to_rag.storage import blob_storage

FIXTURES_DIR = Path(__file__).parent / "fixtures"


def test_ingest_source_pdf_success(tmp_path, monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_BLOB_ROOT", str(tmp_path))
    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")

    source_id = str(uuid.uuid4())
    blob_key = f"sources/{source_id}.pdf"
    pdf_bytes = (FIXTURES_DIR / "sample.pdf").read_bytes()
    blob_storage().write_bytes(blob_key, pdf_bytes)

    client = TestClient(create_app())
    response = client.post(
        "/v1/sources/ingest",
        json={
            "source_id": source_id,
            "blob_key": blob_key,
            "content_type": "application/pdf",
        },
    )

    assert response.status_code == 200
    data = response.json()
    assert "chunks" in data
    chunks = data["chunks"]
    assert len(chunks) == 2

    assert chunks[0]["page_number"] == 1
    assert chunks[0]["chunk_index"] == 0
    assert chunks[0]["section"] is None
    assert "Temporal-difference learning" in chunks[0]["content"]
    assert len(chunks[0]["embedding"]) == 8

    assert chunks[1]["page_number"] == 2
    assert chunks[1]["chunk_index"] == 1
    assert "Monte Carlo methods" in chunks[1]["content"]
    assert len(chunks[1]["embedding"]) == 8


def test_ingest_source_markdown_success(tmp_path, monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_BLOB_ROOT", str(tmp_path))
    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")

    source_id = str(uuid.uuid4())
    blob_key = f"sources/{source_id}.md"
    md_bytes = (FIXTURES_DIR / "sample.md").read_bytes()
    blob_storage().write_bytes(blob_key, md_bytes)

    client = TestClient(create_app())
    response = client.post(
        "/v1/sources/ingest",
        json={
            "source_id": source_id,
            "blob_key": blob_key,
            "content_type": "text/markdown",
        },
    )

    assert response.status_code == 200
    chunks = response.json()["chunks"]
    assert len(chunks) == 3
    assert chunks[0]["section"] == "Reinforcement Learning"
    assert chunks[1]["section"] == "Reinforcement Learning > Temporal-Difference Learning"
    assert chunks[2]["section"] == "Reinforcement Learning > Temporal-Difference Learning > Advantages of TD Methods"


def test_ingest_source_scanned_pdf_returns_422(tmp_path, monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_BLOB_ROOT", str(tmp_path))
    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")

    source_id = str(uuid.uuid4())
    blob_key = f"sources/{source_id}.pdf"
    scanned_bytes = (FIXTURES_DIR / "scanned_or_blank.pdf").read_bytes()
    blob_storage().write_bytes(blob_key, scanned_bytes)

    client = TestClient(create_app())
    response = client.post(
        "/v1/sources/ingest",
        json={
            "source_id": source_id,
            "blob_key": blob_key,
            "content_type": "application/pdf",
        },
    )

    assert response.status_code == 422
    assert "No text layer found" in response.json()["detail"]


def test_ingest_source_missing_blob_key_returns_404(tmp_path, monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_BLOB_ROOT", str(tmp_path))
    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")

    client = TestClient(create_app())
    response = client.post(
        "/v1/sources/ingest",
        json={
            "source_id": str(uuid.uuid4()),
            "blob_key": "sources/non-existent.pdf",
            "content_type": "application/pdf",
        },
    )

    assert response.status_code == 404


def test_ingest_source_unsupported_content_type_returns_422(tmp_path, monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_BLOB_ROOT", str(tmp_path))
    monkeypatch.setenv("TALK_TO_RAG_EMBED", "fake-embed")

    source_id = str(uuid.uuid4())
    blob_key = f"sources/{source_id}.bin"
    blob_storage().write_bytes(blob_key, b"binary content")

    client = TestClient(create_app())
    response = client.post(
        "/v1/sources/ingest",
        json={
            "source_id": source_id,
            "blob_key": blob_key,
            "content_type": "application/octet-stream",
        },
    )

    assert response.status_code == 422
    assert "Unsupported content type" in response.json()["detail"]


def test_ingest_source_model_error_returns_503(tmp_path, monkeypatch):
    from talk_to_rag.llm import ModelError

    monkeypatch.setenv("TALK_TO_RAG_BLOB_ROOT", str(tmp_path))

    class BrokenEmbedder:
        def embed_documents(self, texts: list[str]) -> list[list[float]]:
            raise ModelError("Ollama connection refused at http://localhost:11434")

        def embed_query(self, text: str) -> list[float]:
            raise ModelError("Ollama connection refused at http://localhost:11434")

    monkeypatch.setattr("talk_to_rag.api.routes.ingestion.embedder", lambda: BrokenEmbedder())

    source_id = str(uuid.uuid4())
    blob_key = f"sources/{source_id}.txt"
    blob_storage().write_bytes(blob_key, b"Some good text content to ingest.")

    client = TestClient(create_app())
    response = client.post(
        "/v1/sources/ingest",
        json={
            "source_id": source_id,
            "blob_key": blob_key,
            "content_type": "text/plain",
        },
    )

    assert response.status_code == 503
    assert "Ollama connection refused" in response.json()["detail"]


def test_ingestion_is_in_openapi_contract():
    paths = TestClient(create_app()).get("/openapi.json").json()["paths"]
    assert "/v1/sources/ingest" in paths
