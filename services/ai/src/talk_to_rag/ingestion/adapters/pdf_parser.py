"""PDF parser adapter: page text from PDFium, page labels and the encryption check from pypdf."""

import io
import re

import pypdfium2 as pdfium
from pypdf import PasswordType, PdfReader
from pypdf.errors import PyPdfError

from talk_to_rag.ingestion.contracts import IngestionError, ParsedPage


class PdfSourceParser:
    """Extracts text page by page from PDF files."""

    def parse(self, content: bytes) -> list[ParsedPage]:
        if not content:
            raise IngestionError("Document is empty.")

        try:
            reader = PdfReader(io.BytesIO(content))
        except (PyPdfError, ValueError) as exc:
            raise IngestionError(f"Corrupt or invalid PDF: {exc}") from exc

        if reader.is_encrypted and reader.decrypt("") == PasswordType.NOT_DECRYPTED:
            raise IngestionError("PDF is password-protected or encrypted.")

        page_labels = [str(lbl) for lbl in reader.page_labels] if reader.page_labels else []

        try:
            texts = _page_texts(content)
        except pdfium.PdfiumError as exc:
            raise IngestionError(f"Failed to extract text: {exc}") from exc

        pages: list[ParsedPage] = []
        for index, text in enumerate(texts):
            # PDFium puts U+FFFE where a word was hyphenated across a line end, so dropping it
            # rejoins the word. Some PDFs also yield NUL and other control characters, which
            # Postgres text columns reject; the carriage returns go with them.
            cleaned = repair_ligatures(re.sub(r"[\x00-\x08\x0b-\x1f\ufffe]", "", text)).strip()
            if cleaned:
                label = page_labels[index] if index < len(page_labels) else None
                pages.append(
                    ParsedPage(
                        page_number=index + 1,
                        text=cleaned,
                        section=None,
                        page_label=label,
                    )
                )

        if not pages:
            raise IngestionError("No text layer found. This looks like a scanned PDF and needs OCR.")

        return pages


_LIGATURES = str.maketrans(
    {
        "\ufb00": "ff",
        "\ufb01": "fi",
        "\ufb02": "fl",
        "\ufb03": "ffi",
        "\ufb04": "ffl",
        "\ufb05": "st",
        "\ufb06": "st",
    }
)

# Some fonts map the ff ligature glyph to U+21B5, so "effect" comes out as "e\u21b5ect". Only
# the arrow between two letters is repaired; one standing alone may be a real arrow.
_FF_AS_ARROW = re.compile(r"(?<=[^\W\d_])\u21b5(?=[^\W\d_])")


def repair_ligatures(text: str) -> str:
    """Spell out ligature characters as letters so keyword search finds the words."""
    return _FF_AS_ARROW.sub("ff", text.translate(_LIGATURES))


def _page_texts(content: bytes) -> list[str]:
    # pypdf runs words together on tightly justified lines, where the gaps are set by glyph
    # positions and not by space characters. PDFium works the spaces out from the positions.
    document = pdfium.PdfDocument(content)
    try:
        return [_text(page) for page in document]
    finally:
        document.close()


def _text(page: pdfium.PdfPage) -> str:
    textpage = page.get_textpage()
    try:
        return textpage.get_text_range()
    finally:
        textpage.close()
        page.close()
