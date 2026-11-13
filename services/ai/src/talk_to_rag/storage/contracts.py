"""The only storage types the rest of the AI service may depend on.

Files are addressed by key ("sources/{uuid}.pdf"), never by path. The .NET API
writes and reads the same keys, so both services must interpret them the same way.
"""

from typing import Protocol, runtime_checkable


class StorageError(RuntimeError):
    """Any storage failure, including a missing or invalid key."""


@runtime_checkable
class BlobStorage(Protocol):
    def read_bytes(self, key: str) -> bytes: ...

    def write_bytes(self, key: str, data: bytes) -> None: ...

    def exists(self, key: str) -> bool: ...
