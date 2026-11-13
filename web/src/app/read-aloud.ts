import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, effect, inject, input, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';
import { Api } from './api';
import { spokenText } from './passages';

/** The API refuses longer text. */
const MAX_CHARACTERS = 5000;

/**
 * Reads a summary aloud with the local voice model. The browser's own voice is used only
 * when the API has no voice model (503), and the button says so. Stopping keeps the audio,
 * so it can be resumed or replayed without asking the model again; a new summary drops it.
 */
@Component({
  selector: 'app-read-aloud',
  template: `
    <button
      type="button"
      [attr.aria-pressed]="state() === 'loading' || state() === 'playing'"
      [disabled]="!text().trim()"
      (click)="toggle()"
    >
      {{
        state() === 'idle'
          ? 'Read aloud'
          : state() === 'paused'
            ? 'Resume'
            : state() === 'loading'
              ? 'Preparing… Stop'
              : 'Stop reading'
      }}
    </button>
    @if (hasAudio()) {
      <button type="button" title="Play the summary from the start" (click)="replay()">
        Replay
      </button>
    }
    @if (note()) {
      <small role="status">{{ note() }}</small>
    }
  `,
})
export class ReadAloud implements OnDestroy {
  readonly text = input.required<string>();

  protected readonly state = signal<'idle' | 'loading' | 'playing' | 'paused'>('idle');
  protected readonly note = signal('');
  protected readonly hasAudio = signal(false);

  private readonly api = inject(Api);
  private request?: Subscription;
  private audio?: HTMLAudioElement;
  private audioUrl?: string;

  constructor() {
    effect(() => {
      this.text();
      untracked(() => this.forget());
    });
  }

  toggle(): void {
    const state = this.state();
    if ((state === 'idle' || state === 'paused') && this.audio) {
      this.play(this.audio);
      return;
    }
    if (state !== 'idle') {
      this.stop();
      return;
    }
    const text = spokenText(this.text()).slice(0, MAX_CHARACTERS);
    this.note.set('');
    this.state.set('loading');
    this.request = this.api.synthesize(text).subscribe({
      next: (wav) => this.keep(wav),
      error: (error: HttpErrorResponse) => {
        if (error.status === 503 && 'speechSynthesis' in window) {
          this.speakWithBrowser(text);
        } else {
          this.state.set('idle');
          this.note.set('Could not read this aloud.');
        }
      },
    });
  }

  replay(): void {
    if (this.audio) {
      this.audio.currentTime = 0;
      this.play(this.audio);
    }
  }

  ngOnDestroy(): void {
    this.forget();
  }

  private keep(wav: Blob): void {
    this.audioUrl = URL.createObjectURL(wav);
    const audio = new Audio(this.audioUrl);
    audio.onended = () => this.state.set('idle');
    audio.onerror = () => {
      this.forget();
      this.note.set('Could not read this aloud.');
    };
    this.audio = audio;
    this.hasAudio.set(true);
    this.play(audio);
  }

  private play(audio: HTMLAudioElement): void {
    this.state.set('playing');
    // The browser may refuse to start audio that arrived long after the click; the next click plays it.
    audio.play().catch(() => {
      if (this.audio === audio) {
        this.state.set('paused');
      }
    });
  }

  private speakWithBrowser(text: string): void {
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.onend = utterance.onerror = () => this.state.set('idle');
    this.note.set("No local voice model is set up, so this is the browser's voice.");
    this.state.set('playing');
    speechSynthesis.speak(utterance);
  }

  /** Stops reading, keeping the audio to resume from. */
  private stop(): void {
    this.request?.unsubscribe();
    if ('speechSynthesis' in window) {
      speechSynthesis.cancel();
    }
    if (this.audio && !this.audio.ended && this.audio.currentTime > 0) {
      this.audio.pause();
      this.state.set('paused');
    } else {
      this.audio?.pause();
      this.state.set('idle');
    }
  }

  private forget(): void {
    this.request?.unsubscribe();
    if ('speechSynthesis' in window) {
      speechSynthesis.cancel();
    }
    if (this.audio) {
      this.audio.onended = this.audio.onerror = null;
      this.audio.pause();
      this.audio = undefined;
    }
    if (this.audioUrl) {
      URL.revokeObjectURL(this.audioUrl);
      this.audioUrl = undefined;
    }
    this.hasAudio.set(false);
    this.note.set('');
    this.state.set('idle');
  }
}
