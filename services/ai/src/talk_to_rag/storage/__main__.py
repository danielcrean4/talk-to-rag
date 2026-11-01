"""Try blob storage by hand.

    python -m talk_to_rag.storage write sources/demo.txt notes.txt
    python -m talk_to_rag.storage read sources/demo.txt
"""

import argparse
import sys
from pathlib import Path

from talk_to_rag.storage import ROOT_ENV, StorageError, blob_storage


def main() -> None:
    parser = argparse.ArgumentParser(description=f"Blob storage under ${ROOT_ENV}.")
    commands = parser.add_subparsers(dest="command", required=True)
    write = commands.add_parser("write", help="store a local file under a key")
    write.add_argument("key")
    write.add_argument("file", type=Path)
    read = commands.add_parser("read", help="print the blob stored under a key")
    read.add_argument("key")
    args = parser.parse_args()

    storage = blob_storage()
    try:
        if args.command == "write":
            storage.write_bytes(args.key, args.file.read_bytes())
            print(f"stored {args.file} as '{args.key}'")
        else:
            sys.stdout.buffer.write(storage.read_bytes(args.key))
    except StorageError as error:
        sys.exit(f"error: {error}")


if __name__ == "__main__":
    main()
