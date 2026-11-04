"""Evaluation metrics for retrieval ranking: Hit@k and Mean Reciprocal Rank (MRR)."""

from typing import Sequence


def compute_hit_at_k(retrieved_pages: Sequence[int], target_pages: Sequence[int], k: int) -> float:
    """Return 1.0 if any target page is in the top-k retrieved pages, else 0.0."""
    target_set = set(target_pages)
    for p in retrieved_pages[:k]:
        if p in target_set:
            return 1.0
    return 0.0


def compute_reciprocal_rank(retrieved_pages: Sequence[int], target_pages: Sequence[int]) -> float:
    """Return 1/rank of the first target page found, or 0.0 if not found."""
    target_set = set(target_pages)
    for idx, p in enumerate(retrieved_pages, start=1):
        if p in target_set:
            return 1.0 / idx
    return 0.0


def evaluate_query_results(
    retrieved_pages_by_query: Sequence[tuple[Sequence[int], Sequence[int]]],
) -> dict[str, float]:
    """Compute Hit@1, Hit@3, Hit@5, Hit@10, and MRR over a collection of queries.

    Each item is a tuple of (retrieved_pages, target_pages).
    """
    total = len(retrieved_pages_by_query)
    if total == 0:
        return {"hit@1": 0.0, "hit@3": 0.0, "hit@5": 0.0, "hit@10": 0.0, "mrr": 0.0}

    hit1 = sum(compute_hit_at_k(ret, tgt, 1) for ret, tgt in retrieved_pages_by_query) / total
    hit3 = sum(compute_hit_at_k(ret, tgt, 3) for ret, tgt in retrieved_pages_by_query) / total
    hit5 = sum(compute_hit_at_k(ret, tgt, 5) for ret, tgt in retrieved_pages_by_query) / total
    hit10 = sum(compute_hit_at_k(ret, tgt, 10) for ret, tgt in retrieved_pages_by_query) / total
    mrr = sum(compute_reciprocal_rank(ret, tgt) for ret, tgt in retrieved_pages_by_query) / total

    return {
        "hit@1": hit1,
        "hit@3": hit3,
        "hit@5": hit5,
        "hit@10": hit10,
        "mrr": mrr,
    }
