import { Component, OnDestroy, inject, output, signal } from '@angular/core';
import { Api } from '../api';
import { errorMessage } from '../auth';

const RECORDING_TYPES = ['audio/webm', 'audio/mp4', 'audio/ogg'];
/** How often the recorder hands over audio while the button is held. */
const PREVIEW_INTERVAL_MS = 700;

/**
 * Hold the button (or Space/Enter on it) to record a question; the local Whisper model turns it into text.
 * While the button is held, the audio so far is transcribed again and again to preview the words.
 */
@Component({
  selector: 'app-push-to-talk',
  template: `
    <button
      type="button"
      class="mic"
      [class.listening]="state() === 'listening'"
      [disabled]="!supported || state() === 'transcribing'"
      [attr.aria-pressed]="state() === 'listening'"
      [attr.aria-label]="label()"
      [title]="supported ? label() : 'This browser cannot record audio.'"
      (pointerdown)="press($event)"
      (pointerup)="release()"
      (pointercancel)="release()"
      (keydown)="keyDown($event)"
      (keyup)="keyUp($event)"
      (contextmenu)="$event.preventDefault()"
    >
      <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
        <path d="M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3Z" />
        <path d="M5 11a7 7 0 0 0 14 0M12 18v3" />
      </svg>
    </button>
  `,
  styles: `
    :host {
      display: flex;
    }
    .mic {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 0.45rem 0.75rem;
      touch-action: none;
      user-select: none;
    }
    .mic svg {
      fill: none;
      stroke: currentColor;
      stroke-width: 2;
      stroke-linecap: round;
    }
    .mic.listening {
      background: var(--danger);
      border-color: var(--danger);
      color: #fff;
    }
  `,
})
export class PushToTalk implements OnDestroy {
  private readonly api = inject(Api);
  private recorder: MediaRecorder | null = null;
  private released = false;
  private destroyed = false;
  private parts: Blob[] = [];
  /** Goes up with each recording, so a preview that answers after its recording ended is dropped. */
  private recording = 0;
  private previewing = false;
  private previewedParts = 0;

  /** The words heard so far, which may still change. */
  readonly preview = output<string>();
  /** The whole question, once the button is released. */
  readonly transcribed = output<string>();
  readonly failed = output<string>();

  protected readonly supported =
    !!navigator.mediaDevices?.getUserMedia && typeof MediaRecorder !== 'undefined';
  protected readonly state = signal<'idle' | 'listening' | 'transcribing'>('idle');

  protected label(): string {
    return {
      idle: 'Hold to ask aloud',
      listening: 'Listening, release to search',
      transcribing: 'Transcribing',
    }[this.state()];
  }

  protected press(event: PointerEvent): void {
    if (event.button !== 0) {
      return;
    }
    (event.target as Element).setPointerCapture?.(event.pointerId);
    this.start();
  }

  protected keyDown(event: KeyboardEvent): void {
    if (event.key === ' ' || event.key === 'Enter') {
      event.preventDefault();
      if (!event.repeat) {
        this.start();
      }
    }
  }

  protected keyUp(event: KeyboardEvent): void {
    if (event.key === ' ' || event.key === 'Enter') {
      event.preventDefault();
      this.release();
    }
  }

  protected release(): void {
    this.released = true;
    if (this.recorder?.state === 'recording') {
      this.recorder.stop();
    }
  }

  private async start(): Promise<void> {
    if (this.state() !== 'idle') {
      return;
    }
    this.released = false;
    this.state.set('listening');
    let stream: MediaStream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (error) {
      this.state.set('idle');
      this.failed.emit(`Microphone unavailable: ${(error as Error).message}`);
      return;
    }
    if (this.destroyed) {
      stream.getTracks().forEach((track) => track.stop());
      return;
    }
    const mimeType = RECORDING_TYPES.find((type) => MediaRecorder.isTypeSupported(type));
    const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
    this.parts = [];
    this.previewedParts = 0;
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) {
        this.parts.push(event.data);
      }
      if (recorder.state === 'recording') {
        this.previewSoFar(recorder.mimeType);
      }
    };
    recorder.onstop = () => {
      stream.getTracks().forEach((track) => track.stop());
      this.recorder = null;
      this.recording++;
      this.transcribe(new Blob(this.parts, { type: recorder.mimeType }));
    };
    this.recorder = recorder;
    recorder.start(PREVIEW_INTERVAL_MS);
    if (this.released) {
      recorder.stop();
    }
  }

  /**
   * Transcribes everything recorded so far, from the start, because only the first part carries the
   * container header. One request runs at a time, so previews come as fast as the model answers.
   */
  private previewSoFar(mimeType: string): void {
    if (this.previewing || this.parts.length === this.previewedParts) {
      return;
    }
    const recording = this.recording;
    this.previewing = true;
    this.previewedParts = this.parts.length;
    const done = () => {
      this.previewing = false;
      if (recording === this.recording && this.recorder?.state === 'recording') {
        this.previewSoFar(mimeType);
      }
    };
    this.api.transcribe(new Blob(this.parts, { type: mimeType })).subscribe({
      next: ({ text }) => {
        // Silence transcribes to nothing; the words already shown stay.
        if (recording === this.recording && text.trim()) {
          this.preview.emit(text.trim());
        }
        done();
      },
      // A preview is a convenience: the full recording is still transcribed on release.
      error: done,
    });
  }

  private transcribe(audio: Blob): void {
    if (audio.size === 0) {
      this.state.set('idle');
      return;
    }
    this.state.set('transcribing');
    this.api.transcribe(audio).subscribe({
      next: ({ text }) => {
        this.state.set('idle');
        if (text.trim()) {
          this.transcribed.emit(text.trim());
        }
      },
      error: (error) => {
        this.state.set('idle');
        this.failed.emit(`Could not transcribe: ${errorMessage(error)}`);
      },
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.recording++;
    if (this.recorder) {
      this.recorder.onstop = null;
      this.recorder.stop();
      this.recorder.stream.getTracks().forEach((track) => track.stop());
    }
  }
}
