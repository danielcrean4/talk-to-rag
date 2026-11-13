"""Markdown parser adapter that extracts sections with heading breadcrumbs."""

import re

from talk_to_rag.ingestion.contracts import IngestionError, ParsedPage

_HEADING_PATTERN = re.compile(r"^(#{1,6})\s+(.+?)\s*#*\s*$")
_FENCE_START_PATTERN = re.compile(r"^\s*(`{3,}|~{3,})")


class MarkdownSourceParser:
    """Parses markdown documents, preserving heading breadcrumb hierarchies."""

    def parse(self, content: bytes) -> list[ParsedPage]:
        if not content:
            raise IngestionError("Document is empty.")

        lines = content.decode("utf-8", errors="replace").splitlines()
        pages: list[ParsedPage] = []
        heading_stack: list[str] = []
        current_lines: list[str] = []
        in_fence = False
        fence_char = ""
        fence_len = 0

        def flush_section() -> None:
            section_text = "\n".join(current_lines).strip()
            if section_text:
                section = " > ".join(heading_stack) if heading_stack else None
                pages.append(ParsedPage(page_number=1, text=section_text, section=section))
            current_lines.clear()

        for line in lines:
            if in_fence:
                stripped = line.strip()
                if stripped.startswith(fence_char * fence_len) and all(c == fence_char for c in stripped):
                    in_fence = False
                current_lines.append(line)
                continue

            fence_match = _FENCE_START_PATTERN.match(line)
            if fence_match:
                in_fence = True
                fence_char = fence_match.group(1)[0]
                fence_len = len(fence_match.group(1))
                current_lines.append(line)
                continue

            heading = _HEADING_PATTERN.match(line)
            if not heading:
                current_lines.append(line)
                continue

            flush_section()
            level = len(heading.group(1))
            heading_stack = heading_stack[: level - 1]
            heading_stack.append(heading.group(2).strip())

        flush_section()

        if not pages:
            raise IngestionError("Document contains no text.")

        return pages
