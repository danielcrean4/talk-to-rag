"""Blob storage on a local directory, such as the Docker volume shared with the .NET API."""

import os
import tempfile
from pathlib import Path, PurePosixPath

from talk_to_rag.storage.contracts import StorageError


class LocalBlobStorage:
    def __init__(self, root: str | Path):
        self._root = Path(root).resolve()

    def read_bytes(self, key: str) -> bytes:
        path = self._path(key)
        try:
            return path.read_bytes()
        except FileNotFoundError:
            raise StorageError(f"No blob with key '{key}'") from None
        except OSError as error:
            raise StorageError(f"Could not read '{key}': {error}") from error

    def write_bytes(self, key: str, data: bytes) -> None:
        path = self._path(key)
        try:
            path.parent.mkdir(parents=True, exist_ok=True)
            # Write to a temp file and rename, so a reader never sees half a file.
            with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as tmp:
                tmp.write(data)
            os.replace(tmp.name, path)
        except OSError as error:
            raise StorageError(f"Could not write '{key}': {error}") from error

    def exists(self, key: str) -> bool:
        return self._path(key).is_file()

    def _path(self, key: str) -> Path:
        # Keys come from another service, so refuse anything that could escape the root.
        parts = PurePosixPath(key).parts
        if not key or "\\" in key or PurePosixPath(key).is_absolute() or ".." in parts:
            raise StorageError(f"Invalid blob key '{key}'")
        return self._root.joinpath(*parts)
