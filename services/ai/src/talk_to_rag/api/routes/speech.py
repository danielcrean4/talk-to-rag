import uuid

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field

from talk_to_rag.speech import SpeechNotConfigured, UnknownVoice, get_stt, get_tts
from talk_to_rag.speech.text import spoken_text
from talk_to_rag.storage import StorageError, blob_storage

MAX_SYNTHESIZE_CHARS = 5000

router = APIRouter(prefix="/v1/speech", tags=["speech"])


class TranscribeRequest(BaseModel):
    blob_key: str


class TranscribeResponse(BaseModel):
    text: str


@router.post("/transcribe", response_model=TranscribeResponse)
def transcribe(request: TranscribeRequest) -> TranscribeResponse:
    """Transcribe an audio blob the API already wrote to shared storage."""
    storage = blob_storage()
    try:
        audio = storage.read_bytes(request.blob_key)
    except StorageError as error:
        raise HTTPException(status_code=404, detail=f"Blob not found: {request.blob_key}") from error
    if not audio:
        raise HTTPException(status_code=400, detail="Audio is empty.")
    try:
        return TranscribeResponse(text=get_stt().transcribe(audio))
    except SpeechNotConfigured as error:
        raise HTTPException(status_code=503, detail=str(error)) from error


class SynthesizeRequest(BaseModel):
    text: str = Field(max_length=MAX_SYNTHESIZE_CHARS)
    voice: str | None = None
    blob_key: str | None = None


class SynthesizeResponse(BaseModel):
    blob_key: str
    format: str
    content_type: str
    duration_seconds: float


@router.post("/synthesize", response_model=SynthesizeResponse)
def synthesize(request: SynthesizeRequest) -> SynthesizeResponse:
    """Speak text with the local voice model and write the WAV to shared storage for the API to stream."""
    text = spoken_text(request.text)
    if not text:
        raise HTTPException(status_code=400, detail="There is no text to speak.")
    try:
        speech = get_tts().synthesize(text, request.voice)
    except SpeechNotConfigured as error:
        raise HTTPException(status_code=503, detail=str(error)) from error
    except UnknownVoice as error:
        raise HTTPException(status_code=400, detail=str(error)) from error
    blob_key = request.blob_key or f"audio/{uuid.uuid4()}.wav"
    try:
        blob_storage().write_bytes(blob_key, speech.wav)
    except StorageError as error:
        raise HTTPException(status_code=400, detail=str(error)) from error
    return SynthesizeResponse(
        blob_key=blob_key,
        format="wav",
        content_type="audio/wav",
        duration_seconds=round(speech.duration_seconds, 3),
    )
