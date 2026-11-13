"""Concept routes: a local model proposes what to learn before a concept."""

import json
import re

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from talk_to_rag import tracing
from talk_to_rag.llm import Message, ModelError, chat_model

router = APIRouter(prefix="/v1/concepts", tags=["concepts"])

MAX_PREREQUISITES = 6
MAX_QUESTIONS = 8


class ProposePrerequisitesRequest(BaseModel):
    concept: str
    summary: str | None = None
    known_concepts: list[str] = []


class ProposedConcept(BaseModel):
    name: str
    summary: str | None = None


class ProposePrerequisitesResponse(BaseModel):
    prerequisites: list[ProposedConcept]
    trace: list[dict] = []


@router.post("/propose-prerequisites", response_model=ProposePrerequisitesResponse)
def propose_prerequisites(request: ProposePrerequisitesRequest) -> ProposePrerequisitesResponse:
    concept = request.concept.strip()
    if not concept:
        raise HTTPException(status_code=400, detail="Concept cannot be empty.")

    try:
        completion = chat_model("local-3b").complete([Message(role="user", content=_prompt(request, concept))])
    except ModelError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc

    return ProposePrerequisitesResponse(prerequisites=parse_prerequisites(completion, concept), trace=tracing.calls())


def _prompt(request: ProposePrerequisitesRequest, concept: str) -> str:
    known = "\n".join(f"- {name}" for name in request.known_concepts[:200]) or "(none yet)"
    about = f" ({request.summary.strip()})" if request.summary and request.summary.strip() else ""
    return (
        f"A learner wants to understand: {concept}{about}\n\n"
        f"List the {MAX_PREREQUISITES} or fewer concepts they must understand first. "
        "Choose direct prerequisites only, one step more basic than the concept, not the whole field.\n"
        "The learner already tracks these concepts. When one of them is a prerequisite, use its name exactly:\n"
        f"{known}\n\n"
        'Reply with only a JSON array, such as [{"name": "Matrix multiplication", '
        '"summary": "One sentence on what it is."}]. Names are short noun phrases.'
    )


def parse_prerequisites(completion: str, concept: str) -> list[ProposedConcept]:
    """Pull the JSON array out of a model reply, dropping malformed, repeated and self-referring entries."""
    seen = {concept.casefold()}
    proposals: list[ProposedConcept] = []
    for item in _json_array(completion):
        if not isinstance(item, dict) or not isinstance(item.get("name"), str):
            continue
        name = " ".join(item["name"].split())[:120]
        if not name or name.casefold() in seen:
            continue
        seen.add(name.casefold())
        summary = item.get("summary")
        proposals.append(ProposedConcept(name=name, summary=summary.strip()[:400] if isinstance(summary, str) else None))
    return proposals[:MAX_PREREQUISITES]


class QuestionPassage(BaseModel):
    n: int
    text: str


class GenerateQuestionsRequest(BaseModel):
    concept: str
    passages: list[QuestionPassage]
    count: int = 4


class GeneratedQuestion(BaseModel):
    prompt: str
    answer: str
    passage: int | None = None


class GenerateQuestionsResponse(BaseModel):
    questions: list[GeneratedQuestion]
    trace: list[dict] = []


@router.post("/generate-questions", response_model=GenerateQuestionsResponse)
def generate_questions(request: GenerateQuestionsRequest) -> GenerateQuestionsResponse:
    concept = request.concept.strip()
    passages = [p for p in request.passages if p.text.strip()]
    if not concept or not passages:
        raise HTTPException(status_code=400, detail="A concept and at least one passage are required.")

    count = max(1, min(request.count, MAX_QUESTIONS))
    excerpts = "\n\n".join(f"[{p.n}] {p.text.strip()}" for p in passages)
    prompt = (
        f"Write {count} flashcards that test understanding of: {concept}\n"
        "Use only facts stated in the numbered passages below. Each answer is one or two sentences.\n"
        "Ask about ideas, not about the wording of the passage, and never mention 'the passage' in a question.\n\n"
        f"{excerpts}\n\n"
        'Reply with only a JSON array, such as [{"prompt": "What does X do?", "answer": "It ...", "passage": 1}], '
        "where passage is the number of the passage the answer comes from."
    )
    try:
        completion = chat_model("local-3b").complete([Message(role="user", content=prompt)])
    except ModelError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc

    return GenerateQuestionsResponse(questions=parse_questions(completion, {p.n for p in passages})[:count], trace=tracing.calls())


def parse_questions(completion: str, passage_numbers: set[int]) -> list[GeneratedQuestion]:
    """Pull flashcards out of a model reply, dropping malformed ones and passage numbers that were never sent."""
    questions: list[GeneratedQuestion] = []
    for item in _json_array(completion):
        if not isinstance(item, dict):
            continue
        prompt, answer, passage = item.get("prompt"), item.get("answer"), item.get("passage")
        if not isinstance(prompt, str) or not isinstance(answer, str) or not prompt.strip() or not answer.strip():
            continue
        questions.append(
            GeneratedQuestion(
                prompt=prompt.strip()[:500],
                answer=answer.strip()[:1000],
                passage=passage if isinstance(passage, int) and passage in passage_numbers else None,
            )
        )
    return questions


def _json_array(completion: str) -> list:
    """The first-to-last bracketed span of a model reply, parsed as JSON, or [] when there is none."""
    match = re.search(r"\[.*\]", completion, re.DOTALL)
    if not match:
        return []
    try:
        return json.loads(match.group(0))
    except json.JSONDecodeError:
        return []
