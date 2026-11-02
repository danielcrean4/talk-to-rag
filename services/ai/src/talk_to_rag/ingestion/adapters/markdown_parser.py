"""Markdown parser adapter that extracts sections with heading breadcrumbs."""

import re

from talk_to_rag.ingestion.contracts import IngestionError, ParsedPage

_HEADING_PATTERN = re.compile(r"^(#{1,6})\s+(.+?)\s*#*\s*$")


class MarkdownSourceParser:
    """Parses markdown documents, preserving heading breadcrumb hierarchies."""

    def parse(self, content: bytes) -> list[ParsedPage]:
        if not content:
            raise IngestionError("Document is empty.")

        try:
            text = content.decode("utf-8")
        except UnicodeDecodeError:
            text = content.decode("utf-8", errors="replace")

        lines = text.splitlines()
        pages: list[ParsedPage] = []
        heading_stack: list[str] = []
        current_lines: list[str] = []

        def current_section() -> str | None:
            return " > ".join(heading_stack) if heading_stack else None

        def flush_section() -> None:
            section_text = "\n".join(current_lines).strip()
            if section_text:
                pages.append(
                    ParsedPage(
                        page_number=1,
                        text=section_text,
                        section=current_section(),
                    )
                )
            current_lines.clear()

        for line in lines:
            match = _HEADING_PATTERN.match(line)
            if match:
                flush_section()
                level = len(match.group(1))
                title = match.group(2).strip()

                # Adjust heading stack depth
                if level <= len(heading_stack):
                    heading_stack = heading_stack[: level - 1]
                heading_stack.append(title)
            else:
                current_lines.append(line)

        flush_section()

        if not pages:
            raise IngestionError("Document contains no text.")

        return pages
