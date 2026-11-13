"""Source document ingestion endpoint."""

from fastapi import APIRouter, HTTPException

from talk_to_rag.api.schemas import IngestedChunk, IngestSourceRequest, IngestSourceResponse
from talk_to_rag.ingestion import IngestionError, default_chunker, parser_for
from talk_to_rag.llm import ModelError, embedder, served_model
from talk_to_rag.storage import StorageError, blob_storage

router = APIRouter(prefix="/v1/sources", tags=["ingestion"])
_BATCH_SIZE = 32


@router.post("/ingest", response_model=IngestSourceResponse)
def ingest_source(request: IngestSourceRequest) -> IngestSourceResponse:
    storage = blob_storage()

    try:
        if not storage.exists(request.blob_key):
            raise HTTPException(status_code=404, detail=f"Blob key not found: '{request.blob_key}'")
        content = storage.read_bytes(request.blob_key)
    except StorageError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc

    try:
        pages = parser_for(request.content_type).parse(content)
    except IngestionError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc

    chunks = default_chunker().chunk(pages)
    if not chunks:
        raise HTTPException(status_code=422, detail="No chunks could be extracted from document.")

    contents = [c.content for c in chunks]
    embeddings: list[list[float]] = []
    try:
        emb = embedder()
        for i in range(0, len(contents), _BATCH_SIZE):
            embeddings.extend(emb.embed_documents(contents[i : i + _BATCH_SIZE]))
    except ModelError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc

    return IngestSourceResponse(
        embedding_model=served_model("embed"),
        chunks=[
            IngestedChunk(
                page_number=chunk.page_number,
                page_label=chunk.page_label,
                section=chunk.section,
                chunk_index=chunk.chunk_index,
                content=chunk.content,
                embedding=embeddings[index],
            )
            for index, chunk in enumerate(chunks)
        ],
    )
