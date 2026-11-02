"""Request and response bodies: the HTTP contract the .NET API codes against."""

from typing import Literal
from uuid import UUID

from pydantic import BaseModel


class HealthResponse(BaseModel):
    status: Literal["ok"]
    service: str
    version: str


class IngestSourceRequest(BaseModel):
    source_id: UUID
    blob_key: str
    content_type: str


class IngestedChunk(BaseModel):
    page_number: int
    section: str | None = None
    chunk_index: int
    content: str
    embedding: list[float]


class IngestSourceResponse(BaseModel):
    chunks: list[IngestedChunk]
