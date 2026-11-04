import pytest
from talk_to_rag.evaluation import compute_hit_at_k, compute_reciprocal_rank, evaluate_query_results


def test_compute_hit_at_k():
    target = [8, 10]

    # Target is 1st result
    assert compute_hit_at_k([8, 15, 20], target, 1) == 1.0
    assert compute_hit_at_k([8, 15, 20], target, 3) == 1.0

    # Target is 2nd result
    assert compute_hit_at_k([15, 10, 20], target, 1) == 0.0
    assert compute_hit_at_k([15, 10, 20], target, 2) == 1.0

    # Target is not in top 2
    assert compute_hit_at_k([15, 25, 10], target, 2) == 0.0
    assert compute_hit_at_k([15, 25, 10], target, 3) == 1.0


def test_compute_reciprocal_rank():
    target = [8, 10]

    # 1st place -> 1/1 = 1.0
    assert compute_reciprocal_rank([8, 20, 30], target) == 1.0

    # 2nd place -> 1/2 = 0.5
    assert compute_reciprocal_rank([5, 10, 30], target) == 0.5

    # 4th place -> 1/4 = 0.25
    assert compute_reciprocal_rank([1, 2, 3, 8], target) == 0.25

    # Not found -> 0.0
    assert compute_reciprocal_rank([1, 2, 3, 4], target) == 0.0


def test_evaluate_query_results_empty():
    res = evaluate_query_results([])
    assert res["hit@1"] == 0.0
    assert res["mrr"] == 0.0


def test_evaluate_query_results_averages():
    # Query 1: hit at rank 1 (RR = 1.0)
    # Query 2: hit at rank 2 (RR = 0.5)
    data = [
        ([8, 20], [8]),
        ([20, 10], [10]),
    ]
    res = evaluate_query_results(data)
    assert res["hit@1"] == 0.5
    assert res["hit@3"] == 1.0
    assert res["mrr"] == pytest.approx(0.75)
