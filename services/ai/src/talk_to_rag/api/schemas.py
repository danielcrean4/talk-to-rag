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
    page_label: str | None = None
    section: str | None = None
    chunk_index: int
    content: str
    embedding: list[float]


class IngestSourceResponse(BaseModel):
    embedding_model: str
    chunks: list[IngestedChunk]


class RecentMessage(BaseModel):
    role: Literal["user", "assistant"]
    content: str


class ExpandQueryRequest(BaseModel):
    question: str
    recent_messages: list[RecentMessage] = []
    expand: bool = False


class QueryItem(BaseModel):
    text: str
    embedding: list[float]


class ExpandQueryResponse(BaseModel):
    queries: list[QueryItem]
    trace: list[dict] = []


class PassageItem(BaseModel):
    n: int
    content: str
    source_title: str | None = None
    page_number: int | None = None
    section: str | None = None


class AnswerRequest(BaseModel):
    question: str
    passages: list[PassageItem]


class AnswerResponse(BaseModel):
    answer: str
    cited: list[int]
    trace: list[dict] = []
