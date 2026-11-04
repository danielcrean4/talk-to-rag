"""RAG routes for query expansion and cited answer generation."""

import json
import logging
import re
from fastapi import APIRouter, HTTPException
from fastapi.responses import StreamingResponse

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
            model = chat_model("local-small")
            context_messages = [
                Message(role=msg.role, content=msg.content)
                for msg in request.recent_messages[-4:]
            ]
            prompt = (
                f"You are a search query optimizer. Given the user's question, rewrite it into 2 or 3 standalone search queries "
                f"optimized for retrieval. Return only the queries, one per line, with no bullets, numbers, or introductory text.\n\n"
                f"Question: {question}"
            )
            context_messages.append(Message(role="user", content=prompt))
            completion = model.complete(context_messages)
            lines = [
                re.sub(r"^(?:\s*[-*]|\s*\d+[.)])+\s*", "", line).strip()
                for line in completion.splitlines()
                if line.strip()
            ]
            for line in lines[:3]:
                if line and line not in query_texts:
                    query_texts.append(line)
        except ModelError as exc:
            logger.warning("Query expansion failed; falling back to original query: %s", exc)

    try:
        emb = embedder()
        queries = [
            QueryItem(text=q, embedding=emb.embed_query(q))
            for q in query_texts
        ]
        return ExpandQueryResponse(queries=queries)
    except ModelError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc


@router.post("/answer", response_model=AnswerResponse)
def answer_passages(request: AnswerRequest) -> AnswerResponse:
    question = request.question.strip()
    if not question:
        raise HTTPException(status_code=400, detail="Question cannot be empty.")

    if not request.passages:
        return AnswerResponse(
            answer="No relevant passages were found to answer this question.",
            cited=[],
        )

    valid_passage_numbers = {p.n for p in request.passages}
    formatted_passages: list[str] = []
    for p in request.passages:
        meta_parts: list[str] = []
        if p.source_title:
            meta_parts.append(f"Source: {p.source_title}")
        if p.page_number is not None:
            meta_parts.append(f"Page: {p.page_number}")
        if p.section:
            meta_parts.append(f"Section: {p.section}")
        meta_str = f" ({', '.join(meta_parts)})" if meta_parts else ""
        formatted_passages.append(f"Passage [{p.n}]{meta_str}:\n{p.content}")

    passages_text = "\n\n".join(formatted_passages)

    system_prompt = (
        "You are an accurate, honest AI study assistant. Answer the user's question using ONLY facts stated directly in the provided reference passages.\n\n"
        "Rules:\n"
        "1. Answer using ONLY information from the passages. If the passages do not contain enough facts to answer, state plainly that the provided sources do not cover it.\n"
        "2. Cite your claims with bracketed passage numbers like [1] or [1, 2] corresponding directly to the passage provided.\n"
        "3. Never guess, extrapolate, or invent facts not in the passages.\n"
        "4. Never cite a passage number that was not provided in the input."
    )

    user_prompt = f"Reference Passages:\n{passages_text}\n\nQuestion: {question}\n\nAnswer with citations:"

    try:
        model = chat_model("local-small")
        completion = model.complete([
            Message(role="system", content=system_prompt),
            Message(role="user", content=user_prompt),
        ])
    except ModelError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc

    # Parse citations in the answer: find [1], [2], etc.
    cited_raw = re.findall(r"\[(\d+)\]", completion)
    cited_numbers = set()
    invented_citations: list[int] = []

    for raw in cited_raw:
        n = int(raw)
        if n in valid_passage_numbers:
            cited_numbers.add(n)
        else:
            invented_citations.append(n)

    if invented_citations:
        logger.warning(
            "Model invented citation numbers not present in passage input: %s",
            invented_citations,
        )

    return AnswerResponse(
        answer=completion.strip(),
        cited=sorted(cited_numbers),
    )


@router.post("/answer/stream")
def answer_passages_stream(request: AnswerRequest) -> StreamingResponse:
    question = request.question.strip()
    if not question:
        raise HTTPException(status_code=400, detail="Question cannot be empty.")

    if not request.passages:
        def empty_generator():
            yield f"data: {json.dumps({'token': 'No relevant passages were found to answer this question.'})}\n\n"
            yield f"data: {json.dumps({'done': True, 'cited': []})}\n\n"

        return StreamingResponse(empty_generator(), media_type="text/event-stream")

    valid_passage_numbers = {p.n for p in request.passages}
    formatted_passages: list[str] = []
    for p in request.passages:
        meta_parts: list[str] = []
        if p.source_title:
            meta_parts.append(f"Source: {p.source_title}")
        if p.page_number is not None:
            meta_parts.append(f"Page: {p.page_number}")
        if p.section:
            meta_parts.append(f"Section: {p.section}")
        meta_str = f" ({', '.join(meta_parts)})" if meta_parts else ""
        formatted_passages.append(f"Passage [{p.n}]{meta_str}:\n{p.content}")

    passages_text = "\n\n".join(formatted_passages)

    system_prompt = (
        "You are an accurate, honest AI study assistant. Answer the user's question using ONLY facts stated directly in the provided reference passages.\n\n"
        "Rules:\n"
        "1. Answer using ONLY information from the passages. If the passages do not contain enough facts to answer, state plainly that the provided sources do not cover it.\n"
        "2. Cite your claims with bracketed passage numbers like [1] or [1, 2] corresponding directly to the passage provided.\n"
        "3. Never guess, extrapolate, or invent facts not in the passages.\n"
        "4. Never cite a passage number that was not provided in the input."
    )
    user_prompt = f"Reference Passages:\n{passages_text}\n\nQuestion: {question}\n\nAnswer with citations:"

    def event_stream():
        try:
            model = chat_model("local-small")
            accumulated = []
            for token in model.stream([
                Message(role="system", content=system_prompt),
                Message(role="user", content=user_prompt),
            ]):
                accumulated.append(token)
                yield f"data: {json.dumps({'token': token})}\n\n"

            full_text = "".join(accumulated)
            cited_raw = re.findall(r"\[(\d+)\]", full_text)
            cited_numbers = {int(n) for n in cited_raw if int(n) in valid_passage_numbers}
            yield f"data: {json.dumps({'done': True, 'cited': sorted(cited_numbers)})}\n\n"
        except ModelError as exc:
            yield f"data: {json.dumps({'error': str(exc)})}\n\n"

    return StreamingResponse(event_stream(), media_type="text/event-stream")
