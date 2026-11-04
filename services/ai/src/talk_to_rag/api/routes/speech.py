"""Speech routes for local push-to-talk (Whisper) and text-to-speech (Kokoro/Piper)."""

from fastapi import APIRouter, HTTPException, Request, Response
from pydantic import BaseModel

from talk_to_rag.speech import get_stt, get_tts
from talk_to_rag.storage import blob_storage

router = APIRouter(prefix="/v1/speech", tags=["speech"])


class TranscribeResponse(BaseModel):
    text: str


class SynthesizeRequest(BaseModel):
    text: str


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
