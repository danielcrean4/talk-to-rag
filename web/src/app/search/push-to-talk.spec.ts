import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PushToTalk } from './push-to-talk';

/** Stands in for MediaRecorder, which jsdom does not have. The test decides when audio arrives. */
class FakeRecorder {
  static made: FakeRecorder[] = [];
  static isTypeSupported = (type: string) => type === 'audio/webm';
  readonly mimeType = 'audio/webm';
  state: 'inactive' | 'recording' = 'inactive';
  ondataavailable: ((event: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;

  constructor(readonly stream: { getTracks: () => { stop: () => void }[] }) {
    FakeRecorder.made.push(this);
  }

  start(): void {
    this.state = 'recording';
  }

  /** Hands over the next slice of audio, as the recorder does on its interval. */
  record(audio: string): void {
    this.ondataavailable?.({ data: new Blob([audio]) });
  }

  stop(): void {
    this.state = 'inactive';
    this.record('end');
    this.onstop?.();
  }
}

describe('PushToTalk', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<PushToTalk>;
  let button: HTMLButtonElement;
  let previews: string[];
  let questions: string[];

  function key(type: 'keydown' | 'keyup'): void {
    button.dispatchEvent(new KeyboardEvent(type, { key: ' ' }));
  }

  /** Holds the button down and waits for the microphone. */
  async function hold(): Promise<FakeRecorder> {
    key('keydown');
    await fixture.whenStable();
    return FakeRecorder.made[FakeRecorder.made.length - 1];
  }

  function answer(text: string): void {
    http.expectOne('/api/speech/transcribe').flush({ text });
  }

  beforeEach(() => {
    FakeRecorder.made = [];
    vi.stubGlobal('MediaRecorder', FakeRecorder);
    vi.stubGlobal('navigator', {
      mediaDevices: { getUserMedia: async () => ({ getTracks: () => [{ stop: () => {} }] }) },
    });
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PushToTalk);
    previews = [];
    questions = [];
    fixture.componentInstance.preview.subscribe((text) => previews.push(text));
    fixture.componentInstance.transcribed.subscribe((text) => questions.push(text));
    fixture.detectChanges();
    button = fixture.nativeElement.querySelector('button');
  });

  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
  });

  it('previews the words while the button is held, and only the release gives the question', async () => {
    const recorder = await hold();

    recorder.record('one');
    answer(' What causes ');
    expect(previews).toEqual(['What causes']);
    expect(questions).toEqual([]);

    recorder.record('two');
    answer('What causes the tides');
    expect(previews).toEqual(['What causes', 'What causes the tides']);

    key('keyup');
    answer('What causes the tides to rise?');
    expect(questions).toEqual(['What causes the tides to rise?']);
    expect(previews).toHaveLength(2);
  });

  it('sends every preview the audio from the start', async () => {
    const recorder = await hold();

    recorder.record('one');
    const first = http.expectOne('/api/speech/transcribe');
    first.flush({ text: 'one' });
    recorder.record('two');
    const second = http.expectOne('/api/speech/transcribe');

    expect((first.request.body.get('audio') as Blob).size).toBe(3);
    expect((second.request.body.get('audio') as Blob).size).toBe(6);
    second.flush({ text: 'one two' });
    key('keyup');
    answer('one two');
  });

  it('runs one preview at a time and catches up when it answers', async () => {
    const recorder = await hold();

    recorder.record('one');
    recorder.record('two');
    const first = http.expectOne('/api/speech/transcribe');
    first.flush({ text: 'one' });
    answer('one two');

    expect(previews).toEqual(['one', 'one two']);
    key('keyup');
    answer('one two');
  });

  it('drops a preview that answers after the button is released', async () => {
    const recorder = await hold();

    recorder.record('one');
    const preview = http.expectOne('/api/speech/transcribe');
    key('keyup');
    const final = http.expectOne((request) => request.url === '/api/speech/transcribe');
    final.flush({ text: 'the whole question' });
    preview.flush({ text: 'the whole' });

    expect(previews).toEqual([]);
    expect(questions).toEqual(['the whole question']);
  });

  it('keeps the words shown when a preview hears nothing or fails', async () => {
    const recorder = await hold();

    recorder.record('one');
    answer('What causes');
    recorder.record('two');
    answer('');
    recorder.record('three');
    http
      .expectOne('/api/speech/transcribe')
      .flush('no', { status: 500, statusText: 'Server Error' });

    expect(previews).toEqual(['What causes']);
    key('keyup');
    answer('What causes the tides?');
    expect(questions).toEqual(['What causes the tides?']);
  });
});
