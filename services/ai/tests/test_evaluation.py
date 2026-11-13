import pytest

from talk_to_rag.evaluation.metrics import Hit, Target, first_relevant_rank, summarize

NOTES = Target("cs229-notes1", frozenset({8, 10}))


def test_hit_needs_the_right_source_not_just_the_right_page():
    assert first_relevant_rank([Hit("other-book", 8), Hit("CS229-Notes1", 10)], NOTES) == 2


def test_no_relevant_hit_has_no_rank():
    assert first_relevant_rank([Hit("cs229-notes1", 3), Hit("other-book", 8)], NOTES) is None


def test_summary_averages_hit_at_k_and_reciprocal_rank():
    results = [
        ([Hit("cs229-notes1", 8)], NOTES),  # rank 1
        ([Hit("x", 1), Hit("x", 2), Hit("x", 3), Hit("cs229-notes1", 10)], NOTES),  # rank 4
        ([Hit("x", 8)], NOTES),  # miss
    ]

    metrics = summarize(results)

    assert metrics["hit@1"] == pytest.approx(1 / 3)
    assert metrics["hit@3"] == pytest.approx(1 / 3)
    assert metrics["hit@5"] == pytest.approx(2 / 3)
    assert metrics["mrr"] == pytest.approx((1 + 1 / 4) / 3)


def test_empty_set_scores_zero():
    assert summarize([])["mrr"] == 0.0


def test_text_target_ignores_case_punctuation_and_line_breaks():
    target = Target("us-constitution", texts=("sole Power of Impeachment",))

    hits = [
        Hit("us-constitution", 1, "The Senate shall have the sole Power to try all Impeachments."),
        Hit("other", 1, "the sole power of impeachment"),
        Hit("us-constitution", 1, "and shall have the sole\nPower of Impeachment."),
    ]

    assert first_relevant_rank(hits, target) == 3


def test_target_with_pages_and_text_needs_both():
    target = Target("book", frozenset({4}), ("gradient descent",))

    assert not target.matches(Hit("book", 5, "gradient descent"))
    assert not target.matches(Hit("book", 4, "normal equation"))
    assert target.matches(Hit("book", 4, "Batch gradient descent."))


def test_below_names_only_the_scores_under_their_minimum():
    from talk_to_rag.evaluation.metrics import below

    assert below({"hit@5": 0.9, "mrr": 0.6}, {"hit@5": 0.85, "mrr": 0.7}) == ["mrr 0.600 < 0.7"]
    assert below({"hit@5": 0.9, "mrr": 0.7}, {"hit@5": 0.85, "mrr": 0.7}) == []
