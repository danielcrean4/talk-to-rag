import pytest

from talk_to_rag.storage import BlobStorage, StorageError
from talk_to_rag.storage.adapters.local import LocalBlobStorage


@pytest.fixture
def storage(tmp_path):
    return LocalBlobStorage(tmp_path)


def test_local_storage_satisfies_the_contract(storage):
    assert isinstance(storage, BlobStorage)


def test_written_bytes_read_back_under_the_same_key(storage, tmp_path):
    storage.write_bytes("sources/abc.pdf", b"%PDF-1.7")

    assert storage.read_bytes("sources/abc.pdf") == b"%PDF-1.7"
    assert storage.exists("sources/abc.pdf")
    assert (tmp_path / "sources/abc.pdf").read_bytes() == b"%PDF-1.7"


def test_writing_again_replaces_the_blob(storage):
    storage.write_bytes("audio/x.webm", b"one")
    storage.write_bytes("audio/x.webm", b"two")

    assert storage.read_bytes("audio/x.webm") == b"two"


def test_missing_key_raises_storage_error(storage):
    assert not storage.exists("sources/missing.pdf")
    with pytest.raises(StorageError, match="No blob with key"):
        storage.read_bytes("sources/missing.pdf")


@pytest.mark.parametrize("key", ["", "../secret", "sources/../../etc/passwd", "/etc/passwd", "sources\\x.pdf"])
def test_keys_that_could_escape_the_root_are_rejected(storage, key):
    with pytest.raises(StorageError, match="Invalid blob key"):
        storage.read_bytes(key)
    with pytest.raises(StorageError, match="Invalid blob key"):
        storage.write_bytes(key, b"x")
