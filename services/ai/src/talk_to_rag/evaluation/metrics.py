"""Retrieval metrics: hit@k and mean reciprocal rank over ranked passages."""

import re
from collections.abc import Mapping, Sequence
from dataclasses import dataclass

KS = (1, 3, 5, 10)


@dataclass(frozen=True)
class Hit:
    """One returned passage. Hits are kept in rank order."""

    source_title: str
    page: int
    content: str = ""


@dataclass(frozen=True)
class Target:
    """Where a question is answered: one source, narrowed by page, by quoted text, or both.

    Text is for sources without pages (Markdown, plain text). A passage matches when it
    contains any of the phrases, ignoring case, punctuation and line breaks.
    """

    source_title: str
    pages: frozenset[int] = frozenset()
    texts: tuple[str, ...] = ()

    def matches(self, hit: Hit) -> bool:
        if hit.source_title.casefold() != self.source_title.casefold():
            return False
        if self.pages and hit.page not in self.pages:
            return False
        if not self.texts:
            return True
        content = _words(hit.content)
        return any(_words(text) in content for text in self.texts)


def first_relevant_rank(hits: Sequence[Hit], target: Target) -> int | None:
    """1-based rank of the first hit that matches the target, or None."""
    for rank, hit in enumerate(hits, start=1):
        if target.matches(hit):
            return rank
    return None


def summarize(results: Sequence[tuple[Sequence[Hit], Target]]) -> dict[str, float]:
    """hit@k for each k in KS, plus MRR, averaged over all questions."""
    ranks = [first_relevant_rank(hits, target) for hits, target in results]
    total = len(ranks) or 1
    metrics = {f"hit@{k}": sum(1 for r in ranks if r is not None and r <= k) / total for k in KS}
    metrics["mrr"] = sum(1 / r for r in ranks if r is not None) / total
    return metrics


def below(metrics: Mapping[str, float], minimums: Mapping[str, float]) -> list[str]:
    """The scores that are under their minimum, as text for an error message."""
    return [f"{name} {metrics[name]:.3f} < {minimum}" for name, minimum in minimums.items() if metrics[name] < minimum]


def _words(text: str) -> str:
    return " ".join(re.sub(r"[^a-z0-9]+", " ", text.casefold()).split())
