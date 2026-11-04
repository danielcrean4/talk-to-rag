"""Speech routes for local push-to-talk (Whisper) and text-to-speech (Kokoro/Piper)."""

from fastapi import APIRouter, HTTPException, Request, Response
from pydantic import BaseModel

from talk_to_rag.speech import get_router, get_stt, get_tts
from talk_to_rag.speech.router import ScreenContext
from talk_to_rag.storage import blob_storage

router = APIRouter(prefix="/v1/speech", tags=["speech"])


class TranscribeResponse(BaseModel):
    text: str


class SynthesizeRequest(BaseModel):
    text: str


class ScreenContextDto(BaseModel):
    source_id: str | None = None
    source_title: str | None = None
    current_page: int | None = None
    total_pages: int | None = None
    selected_text: str | None = None
    active_view: str | None = None


class RouteIntentRequest(BaseModel):
    transcript: str
    screen_context: ScreenContextDto | None = None


class RouteIntentResponse(BaseModel):
    intent: str
    topic: str | None = None
    query: str | None = None
    route: str
    confidence: float


@router.post("/transcribe", response_model=TranscribeResponse)
async def transcribe_audio(request: Request) -> TranscribeResponse:
    content_type = request.headers.get("content-type", "")
    if "application/json" in content_type:
        body = await request.json()
        blob_key = body.get("blob_key")
        if not blob_key:
            raise HTTPException(status_code=400, detail="blob_key is required.")
        storage = blob_storage()
        if not storage.exists(blob_key):
            raise HTTPException(status_code=404, detail=f"Blob not found: {blob_key}")
        audio_bytes = storage.read_bytes(blob_key)
    else:
        audio_bytes = await request.body()

    if not audio_bytes:
        raise HTTPException(status_code=400, detail="Audio content cannot be empty.")

    stt = get_stt()
    text = stt.transcribe(audio_bytes)
    return TranscribeResponse(text=text)


@router.post("/synthesize")
def synthesize_speech(request: SynthesizeRequest) -> Response:
    text = request.text.strip()
    if not text:
        raise HTTPException(status_code=400, detail="Text cannot be empty.")
    tts = get_tts()
    audio_bytes = tts.synthesize(text)
    return Response(content=audio_bytes, media_type="audio/wav")


@router.post("/route-intent", response_model=RouteIntentResponse)
def route_speech_intent(request: RouteIntentRequest) -> RouteIntentResponse:
    transcript = request.transcript.strip()
    if not transcript:
        raise HTTPException(status_code=400, detail="Transcript cannot be empty.")

    context = None
    if request.screen_context is not None:
        context = ScreenContext(
            source_id=request.screen_context.source_id,
            source_title=request.screen_context.source_title,
            current_page=request.screen_context.current_page,
            total_pages=request.screen_context.total_pages,
            selected_text=request.screen_context.selected_text,
            active_view=request.screen_context.active_view,
        )

    router_inst = get_router()
    result = router_inst.route(transcript, context)
    return RouteIntentResponse(
        intent=result.intent,
        topic=result.topic,
        query=result.query,
        route=result.route,
        confidence=result.confidence,
    )
