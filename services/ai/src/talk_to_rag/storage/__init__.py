"""Blob storage for the AI service. Import from here, never from an adapter."""

import os

from talk_to_rag.storage.adapters.local import LocalBlobStorage
from talk_to_rag.storage.contracts import BlobStorage, StorageError

ROOT_ENV = "TALK_TO_RAG_BLOB_ROOT"
# Where compose.yaml mounts the volume shared with the .NET API.
DEFAULT_ROOT = "/var/talk-to-rag/blobs"


def blob_storage() -> BlobStorage:
    return LocalBlobStorage(os.environ.get(ROOT_ENV, DEFAULT_ROOT))


__all__ = ["BlobStorage", "StorageError", "blob_storage"]
