"""The only storage types the rest of the AI service may depend on.

Files are addressed by key ("sources/{uuid}.pdf"), never by path, so the same key
works whether the bytes live on a shared volume or in S3, GCS or Azure Blob.
"""

from typing import Protocol, runtime_checkable


class StorageError(RuntimeError):
    """Any storage failure, including a missing or invalid key."""


@runtime_checkable
class BlobStorage(Protocol):
    def read_bytes(self, key: str) -> bytes: ...

    def write_bytes(self, key: str, data: bytes) -> None: ...

    def exists(self, key: str) -> bool: ...
