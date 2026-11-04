"""Layered command router for voice interaction.

Layer 1: Exact-match fast path (deterministic regex / string matching, 0ms model latency).
Layer 2: Local LLM with screen context producing structured JSON intent.
Layer 3: Fallback to search query on unparseable or unrecognized output.
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from typing import Any, Literal

from talk_to_rag.llm.contracts import ChatModel, Message

IntentType = Literal[
    "next_page",
    "prev_page",
    "read_this",
    "summarize_page",
    "pull_up",
    "search",
]

RouteType = Literal["fast_path", "llm", "fallback"]

VALID_INTENTS: set[str] = {
    "next_page",
    "prev_page",
    "read_this",
    "summarize_page",
    "pull_up",
    "search",
}


@dataclass(frozen=True)
class ScreenContext:
    source_id: str | None = None
    source_title: str | None = None
    current_page: int | None = None
    total_pages: int | None = None
    selected_text: str | None = None
    active_view: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "source_id": self.source_id,
            "source_title": self.source_title,
            "current_page": self.current_page,
            "total_pages": self.total_pages,
            "selected_text": self.selected_text,
            "active_view": self.active_view,
        }


@dataclass(frozen=True)
class RouteResult:
    intent: IntentType
    topic: str | None = None
    query: str | None = None
    route: RouteType = "fast_path"
    confidence: float = 1.0


class LayeredCommandRouter:
    """Routes voice transcripts to fixed application commands using layered fast-path and LLM evaluation."""

    _FAST_PATH_NEXT = re.compile(r"^(?:next|next\s+page|turn\s+page|page\s+forward|forward\s+page|forward)$", re.IGNORECASE)
    _FAST_PATH_PREV = re.compile(r"^(?:prev|previous|previous\s+page|prev\s+page|page\s+back|back\s+page|go\s+back|back)$", re.IGNORECASE)
    _FAST_PATH_READ = re.compile(r"^(?:read\s+this|read\s+this\s+page|read\s+page|read\s+aloud|read\s+to\s+me|read\s+it|listen)$", re.IGNORECASE)
    _FAST_PATH_SUMMARIZE = re.compile(r"^(?:summarize|summarize\s+page|summarize\s+this\s+page|summarize\s+this|summary)$", re.IGNORECASE)
    _FAST_PATH_PULL_UP = re.compile(r"^(?:pull\s+up|find|look\s+up|show\s+me)\s+(.+)$", re.IGNORECASE)
    _FAST_PATH_SEARCH = re.compile(r"^(?:search\s+for|search)\s+(.+)$", re.IGNORECASE)

    def __init__(self, model: ChatModel | None = None):
        self._model = model

    def route(self, transcript: str, context: ScreenContext | None = None) -> RouteResult:
        normalized = transcript.strip().rstrip(".?!")
        if not normalized:
            return RouteResult(intent="search", query="", route="fast_path", confidence=1.0)

        # Layer 1: Exact-match fast path
        fast_result = self._match_fast_path(normalized)
        if fast_result is not None:
            return fast_result

        # Layer 2: LLM classifier with screen context
        if self._model is not None:
            llm_result = self._classify_with_llm(normalized, context)
            if llm_result is not None:
                return llm_result

        # Layer 3: Fallback to search query
        return RouteResult(intent="search", query=normalized, route="fallback", confidence=0.7)

    def _match_fast_path(self, text: str) -> RouteResult | None:
        if self._FAST_PATH_NEXT.match(text):
            return RouteResult(intent="next_page", route="fast_path", confidence=1.0)
        if self._FAST_PATH_PREV.match(text):
            return RouteResult(intent="prev_page", route="fast_path", confidence=1.0)
        if self._FAST_PATH_READ.match(text):
            return RouteResult(intent="read_this", route="fast_path", confidence=1.0)
        if self._FAST_PATH_SUMMARIZE.match(text):
            return RouteResult(intent="summarize_page", route="fast_path", confidence=1.0)

        pull_match = self._FAST_PATH_PULL_UP.match(text)
        if pull_match:
            topic = pull_match.group(1).strip()
            return RouteResult(intent="pull_up", topic=topic, route="fast_path", confidence=0.95)

        search_match = self._FAST_PATH_SEARCH.match(text)
        if search_match:
            query = search_match.group(1).strip()
            return RouteResult(intent="search", query=query, route="fast_path", confidence=0.95)

        return None

    def _classify_with_llm(self, transcript: str, context: ScreenContext | None) -> RouteResult | None:
        context_str = json.dumps(context.to_dict()) if context else "{}"
        system_prompt = (
            "You are an intent router for a textbook study application.\n"
            "Classify the user's voice transcript into exactly one command:\n"
            "- next_page: advance to the next page\n"
            "- prev_page: go back to the previous page\n"
            "- read_this: read current page or selected text aloud\n"
            "- summarize_page: summarize the active page\n"
            "- pull_up: look up a specific topic across sources (extract the topic string)\n"
            "- search: general conceptual question or search query\n\n"
            f"Screen context:\n{context_str}\n\n"
            "Respond ONLY with a valid JSON object matching:\n"
            '{"intent": "next_page"|"prev_page"|"read_this"|"summarize_page"|"pull_up"|"search", '
            '"topic": "<topic or null>", "query": "<cleaned query or null>"}'
        )

        try:
            raw_response = self._model.complete([
                Message(role="system", content=system_prompt),
                Message(role="user", content=transcript),
            ])
            cleaned = raw_response.strip()
            # Handle potential markdown code fencing
            if cleaned.startswith("```"):
                cleaned = re.sub(r"^```(?:json)?\s*", "", cleaned)
                cleaned = re.sub(r"\s*```$", "", cleaned)

            data = json.loads(cleaned)
            intent = data.get("intent")
            if intent in VALID_INTENTS:
                return RouteResult(
                    intent=intent,  # type: ignore
                    topic=data.get("topic"),
                    query=data.get("query") or transcript,
                    route="llm",
                    confidence=0.9,
                )
        except Exception:
            # On parse error or model failure, defer to Layer 3 fallback
            return None

        return None
