"""RAG routes for query expansion and cited answer generation."""

import json
import logging
import re

from fastapi import APIRouter, HTTPException
from fastapi.responses import StreamingResponse

from talk_to_rag import tracing
from talk_to_rag.api.schemas import (
    AnswerRequest,
    AnswerResponse,
    ExpandQueryRequest,
    ExpandQueryResponse,
    QueryItem,
)
from talk_to_rag.llm import Message, ModelError, chat_model, embedder

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/v1/rag", tags=["rag"])


@router.post("/expand-query", response_model=ExpandQueryResponse)
def expand_query(request: ExpandQueryRequest) -> ExpandQueryResponse:
    question = request.question.strip()
    if not question:
        raise HTTPException(status_code=400, detail="Question cannot be empty.")

    query_texts = [question]

    if request.expand:
        try:
            messages = [Message(role=msg.role, content=msg.content) for msg in request.recent_messages[-4:]]
            prompt = (
                "You are a search query optimizer. Given the user's question, rewrite it into 2 or 3 standalone search queries "
                "optimized for retrieval. Return only the queries, one per line, with no bullets, numbers, or introductory text.\n\n"
                f"Question: {question}"
            )
            messages.append(Message(role="user", content=prompt))
            completion = chat_model("local-small").complete(messages)
            lines = [re.sub(r"^(?:\s*[-*]|\s*\d+[.)])+\s*", "", line).strip() for line in completion.splitlines() if line.strip()]
            for line in lines[:3]:
                if line and line not in query_texts:
                    query_texts.append(line)
        except ModelError as exc:
            logger.warning("Query expansion failed; falling back to original query: %s", exc)

    try:
        emb = embedder()
        queries = [QueryItem(text=q, embedding=emb.embed_query(q)) for q in query_texts]
    except ModelError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    return ExpandQueryResponse(queries=queries, trace=tracing.calls())


# The 1B model copies the passages back instead of answering; the 3B model summarises them.
SUMMARY_MODEL = "local-3b"

SUMMARY_INSTRUCTIONS = (
    "You are a study assistant. Answer the question from the reference passages and nothing else.\n\n"
    "Rules:\n"
    "1. Write a short answer in your own words: one paragraph, at most six sentences. Do not copy passages out.\n"
    "2. After each claim, cite the passage it comes from with its number in brackets, like [1] or [2][5].\n"
    "3. Use only facts stated in the passages, and only passage numbers that were given.\n"
    "4. If the passages do not answer the question, say that the sources do not cover it.\n"
    "5. Do not add a list of citations or sources at the end."
)

NO_PASSAGES = "No relevant passages were found to answer this question."


def summary_messages(request: AnswerRequest) -> list[Message]:
    passages = []
    for passage in request.passages:
        location = [
            f"{label}: {value}"
            for label, value in (
                ("Source", passage.source_title),
                ("Page", passage.page_number),
                ("Section", passage.section),
            )
            if value
        ]
        heading = f"Passage [{passage.n}]" + (f" ({', '.join(location)})" if location else "")
        passages.append(f"{heading}:\n{passage.content}")

    question = request.question.strip()
    prompt = "Reference passages:\n" + "\n\n".join(passages) + f"\n\nQuestion: {question}\n\nAnswer:"
    return [Message(role="system", content=SUMMARY_INSTRUCTIONS), Message(role="user", content=prompt)]


def cited_passages(answer: str, request: AnswerRequest) -> list[int]:
    """The passage numbers the answer cites, without any number that matches no passage."""
    given = {passage.n for passage in request.passages}
    cited = {int(number) for number in re.findall(r"\[(\d+)\]", answer)}
    if cited - given:
        logger.warning("The model cited passages that were not given: %s", sorted(cited - given))
    return sorted(cited & given)


def _require_question(request: AnswerRequest) -> None:
    if not request.question.strip():
        raise HTTPException(status_code=400, detail="Question cannot be empty.")


@router.post("/answer", response_model=AnswerResponse)
def answer_passages(request: AnswerRequest) -> AnswerResponse:
    _require_question(request)
    if not request.passages:
        return AnswerResponse(answer=NO_PASSAGES, cited=[])

    try:
        answer = chat_model(SUMMARY_MODEL).complete(summary_messages(request)).strip()
    except ModelError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc

    return AnswerResponse(answer=answer, cited=cited_passages(answer, request), trace=tracing.calls())


@router.post("/answer/stream")
def answer_passages_stream(request: AnswerRequest) -> StreamingResponse:
    _require_question(request)

    def events():
        if not request.passages:
            yield _event({"token": NO_PASSAGES})
            yield _event({"done": True, "cited": []})
            return

        try:
            tokens = []
            for token in chat_model(SUMMARY_MODEL).stream(summary_messages(request)):
                tokens.append(token)
                yield _event({"token": token})
        except ModelError as exc:
            yield _event({"error": str(exc)})
            return

        # The API stores the trace and does not pass this line on to the browser.
        yield _event({"trace": tracing.calls()})
        yield _event({"done": True, "cited": cited_passages("".join(tokens), request)})

    return StreamingResponse(events(), media_type="text/event-stream")


def _event(data: dict) -> str:
    return f"data: {json.dumps(data)}\n\n"
