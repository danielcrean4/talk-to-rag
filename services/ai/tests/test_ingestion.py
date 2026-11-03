from pathlib import Path
import pytest

from talk_to_rag.ingestion import (
    IngestionError,
    ParsedPage,
    default_chunker,
    parser_for,
)
from talk_to_rag.ingestion.adapters.markdown_parser import MarkdownSourceParser
from talk_to_rag.ingestion.adapters.paragraph_chunker import ParagraphChunker
from talk_to_rag.ingestion.adapters.pypdf_parser import PyPdfSourceParser
from talk_to_rag.ingestion.adapters.text_parser import PlainTextSourceParser

FIXTURES_DIR = Path(__file__).parent / "fixtures"


def test_pypdf_parser_extracts_page_by_page_text():
    pdf_bytes = (FIXTURES_DIR / "sample.pdf").read_bytes()
    parser = PyPdfSourceParser()
    pages = parser.parse(pdf_bytes)

    assert len(pages) == 2
    assert pages[0].page_number == 1
    assert "Temporal-difference learning" in pages[0].text
    assert pages[0].section is None

    assert pages[1].page_number == 2
    assert "Monte Carlo methods" in pages[1].text
    assert pages[1].section is None


def test_pypdf_parser_rejects_scanned_or_empty_pdf():
    pdf_bytes = (FIXTURES_DIR / "scanned_or_blank.pdf").read_bytes()
    parser = PyPdfSourceParser()

    with pytest.raises(IngestionError, match="No text layer found"):
        parser.parse(pdf_bytes)


def test_pypdf_parser_rejects_empty_or_corrupt_bytes():
    parser = PyPdfSourceParser()
    with pytest.raises(IngestionError, match="Document is empty"):
        parser.parse(b"")

    with pytest.raises(IngestionError, match="Corrupt or invalid PDF"):
        parser.parse(b"not a valid pdf content at all")


def test_pypdf_parser_accepts_copy_protected_pdf_with_owner_password():
    from io import BytesIO
    from pypdf import PdfReader, PdfWriter

    sample = (FIXTURES_DIR / "sample.pdf").read_bytes()
    reader = PdfReader(BytesIO(sample))
    writer = PdfWriter()
    for page in reader.pages:
        writer.add_page(page)
    writer.encrypt(user_password="", owner_password="owner_restriction_secret")
    buf = BytesIO()
    writer.write(buf)

    parser = PyPdfSourceParser()
    pages = parser.parse(buf.getvalue())
    assert len(pages) == 2
    assert "Temporal-difference learning" in pages[0].text


def test_pypdf_parser_rejects_user_password_encrypted_pdf():
    from io import BytesIO
    from pypdf import PdfReader, PdfWriter

    sample = (FIXTURES_DIR / "sample.pdf").read_bytes()
    reader = PdfReader(BytesIO(sample))
    writer = PdfWriter()
    for page in reader.pages:
        writer.add_page(page)
    writer.encrypt(user_password="actual_secret", owner_password="owner_secret")
    buf = BytesIO()
    writer.write(buf)

    parser = PyPdfSourceParser()
    with pytest.raises(IngestionError, match="PDF is password-protected or encrypted"):
        parser.parse(buf.getvalue())


def test_pypdf_parser_rejoins_hyphenated_linebreaks_and_captures_page_labels(monkeypatch):
    class StubPage:
        def extract_text(self):
            return "boot-\nstrapping and value func-\ntion approximation"

    class StubReader:
        pages = [StubPage()]
        page_labels = ["120"]
        is_encrypted = False

    monkeypatch.setattr("talk_to_rag.ingestion.adapters.pypdf_parser.PdfReader", lambda _: StubReader())

    parser = PyPdfSourceParser()
    pages = parser.parse(b"dummy pdf bytes")
    assert len(pages) == 1
    assert "bootstrapping and value function approximation" in pages[0].text
    assert pages[0].page_label == "120"


def test_markdown_parser_tracks_heading_breadcrumbs():
    md_bytes = (FIXTURES_DIR / "sample.md").read_bytes()
    parser = MarkdownSourceParser()
    pages = parser.parse(md_bytes)

    assert len(pages) == 3
    assert all(p.page_number == 1 for p in pages)

    assert pages[0].section == "Reinforcement Learning"
    assert "reward signal" in pages[0].text

    assert pages[1].section == "Reinforcement Learning > Temporal-Difference Learning"
    assert "dynamic programming" in pages[1].text

    assert pages[2].section == "Reinforcement Learning > Temporal-Difference Learning > Advantages of TD Methods"
    assert "bootstrap" in pages[2].text


def test_markdown_parser_ignores_headings_inside_code_fences():
    md = b"""# Main Title

Some explanation before code.

```python
# this is a python comment, not a heading
def calculate_td_error():
    ## also not a heading
    return 42
```

## Next Section

~~~typescript
// comment
### not a section either
const x = 10;
~~~

Content in next section.
"""
    parser = MarkdownSourceParser()
    pages = parser.parse(md)

    assert len(pages) == 2
    assert pages[0].section == "Main Title"
    assert "def calculate_td_error():" in pages[0].text
    assert "# this is a python comment" in pages[0].text

    assert pages[1].section == "Main Title > Next Section"
    assert "const x = 10;" in pages[1].text
    assert "### not a section either" in pages[1].text


def test_markdown_parser_handles_document_without_headings():
    parser = MarkdownSourceParser()
    pages = parser.parse(b"Just simple markdown text without any headings.")
    assert len(pages) == 1
    assert pages[0].page_number == 1
    assert pages[0].section is None
    assert pages[0].text == "Just simple markdown text without any headings."


def test_markdown_parser_rejects_empty_document():
    parser = MarkdownSourceParser()
    with pytest.raises(IngestionError, match="empty|no text"):
        parser.parse(b"")
    with pytest.raises(IngestionError, match="empty|no text"):
        parser.parse(b"   \n\n  \t ")


def test_plain_text_parser_produces_single_page():
    txt_bytes = (FIXTURES_DIR / "sample.txt").read_bytes()
    parser = PlainTextSourceParser()
    pages = parser.parse(txt_bytes)

    assert len(pages) == 1
    assert pages[0].page_number == 1
    assert pages[0].section is None
    assert "Dynamic programming" in pages[0].text


def test_plain_text_parser_rejects_empty():
    parser = PlainTextSourceParser()
    with pytest.raises(IngestionError, match="empty|no text"):
        parser.parse(b"")
    with pytest.raises(IngestionError, match="empty|no text"):
        parser.parse(b"   \n  \t ")


def test_parser_for_dispatches_by_content_type():
    assert isinstance(parser_for("application/pdf"), PyPdfSourceParser)
    assert isinstance(parser_for("text/markdown"), MarkdownSourceParser)
    assert isinstance(parser_for("text/x-markdown"), MarkdownSourceParser)
    assert isinstance(parser_for("text/plain; charset=utf-8"), PlainTextSourceParser)

    with pytest.raises(IngestionError, match="Unsupported content type"):
        parser_for("image/png")


def test_paragraph_chunker_respects_boundaries_and_overlap():
    p1 = "Paragraph 1 is about Bellman optimality. " * 8
    p2 = "Paragraph 2 discusses policy evaluation. " * 8
    p3 = "Paragraph 3 covers value iteration. " * 8
    text = f"{p1}\n\n{p2}\n\n{p3}"

    chunker = default_chunker(target_chunk_size=600, overlap=100)
    pages = [ParsedPage(page_number=1, text=text, section="Chapter 3 > DP")]
    chunks = chunker.chunk(pages)

    assert len(chunks) > 1
    assert all(c.page_number == 1 for c in chunks)
    assert all(c.section == "Chapter 3 > DP" for c in chunks)
    # Consecutive indices
    assert [c.chunk_index for c in chunks] == list(range(len(chunks)))
    # Size constraint
    assert all(len(c.content) <= 650 for c in chunks)


def test_paragraph_chunker_never_crosses_page_boundaries():
    chunker = default_chunker(target_chunk_size=300, overlap=50)
    pages = [
        ParsedPage(page_number=1, text="Page 1 content about TD learning."),
        ParsedPage(page_number=2, text="Page 2 content about Monte Carlo methods."),
    ]
    chunks = chunker.chunk(pages)

    assert len(chunks) == 2
    assert chunks[0].page_number == 1
    assert chunks[0].chunk_index == 0
    assert "Page 1" in chunks[0].content

    assert chunks[1].page_number == 2
    assert chunks[1].chunk_index == 1
    assert "Page 2" in chunks[1].content
    # Page 1 text must not leak into page 2 overlap
    assert "Page 1" not in chunks[1].content


def test_paragraph_chunker_splits_oversized_paragraph_on_sentences():
    sentence1 = "This is sentence one about exploration versus exploitation."
    sentence2 = "This is sentence two discussing epsilon-greedy action selection."
    long_para = f"{sentence1} {sentence2}"

    chunker = ParagraphChunker(target_chunk_size=len(sentence1) + 15, overlap=20)
    chunks = chunker.chunk([ParsedPage(page_number=1, text=long_para)])

    assert len(chunks) == 2
    assert sentence1 in chunks[0].content
    assert sentence2 in chunks[1].content


def test_paragraph_chunker_validates_parameters():
    with pytest.raises(ValueError, match="target_chunk_size must be positive"):
        ParagraphChunker(target_chunk_size=0)

    with pytest.raises(ValueError, match="overlap must be non-negative"):
        ParagraphChunker(target_chunk_size=500, overlap=-1)

    with pytest.raises(ValueError, match="overlap must be less than target_chunk_size"):
        ParagraphChunker(target_chunk_size=500, overlap=500)
