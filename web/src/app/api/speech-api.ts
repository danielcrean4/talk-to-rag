import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class SpeechApi {
  private readonly http = inject(HttpClient);

  transcribe(audio: Blob): Observable<{ text: string }> {
    const form = new FormData();
    form.append('audio', audio, 'question');
    return this.http.post<{ text: string }>('/api/speech/transcribe', form);
  }

  /** A WAV of the text spoken by the local voice model; 503 when no voice model is set up. */
  synthesize(text: string): Observable<Blob> {
    return this.http.post('/api/speech/synthesize', { text }, { responseType: 'blob' });
  }
}
