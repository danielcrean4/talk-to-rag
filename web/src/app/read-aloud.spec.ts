import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReadAloud } from './read-aloud';

/** Stands in for HTMLAudioElement, which jsdom cannot play. */
class FakeAudio {
  static made: FakeAudio[] = [];
  currentTime = 0;
  ended = false;
  /** Where each play() started from. */
  readonly starts: number[] = [];
  onended: (() => void) | null = null;
  onerror: (() => void) | null = null;

  constructor(readonly src: string) {
    FakeAudio.made.push(this);
  }

  play(): Promise<void> {
    this.starts.push(this.currentTime);
    this.currentTime += 1;
    return Promise.resolve();
  }

  pause(): void {}
}

describe('ReadAloud', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<ReadAloud>;
  let element: HTMLElement;

  function buttons(): string[] {
    return Array.from(element.querySelectorAll('button')).map(
      (button) => button.textContent?.trim() ?? '',
    );
  }

  function click(text: string): void {
    const button = Array.from(element.querySelectorAll('button')).find(
      (item) => item.textContent?.trim() === text,
    );
    if (!button) {
      throw new Error(`No button "${text}" in ${buttons().join(', ')}`);
    }
    button.click();
    fixture.detectChanges();
  }

  beforeEach(() => {
    FakeAudio.made = [];
    vi.stubGlobal('Audio', FakeAudio);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:summary');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReadAloud);
    fixture.componentRef.setInput('text', 'Spring tides follow the new moon [1].');
    fixture.detectChanges();
    element = fixture.nativeElement;
  });

  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  function readOnce(): FakeAudio {
    click('Read aloud');
    http.expectOne('/api/speech/synthesize').flush(new Blob(['wav']));
    fixture.detectChanges();
    return FakeAudio.made[0];
  }

  it('stops by pausing, then resumes the same audio without asking the voice model again', () => {
    const audio = readOnce();
    expect(buttons()).toEqual(['Stop reading', 'Replay']);

    click('Stop reading');
    expect(buttons()).toEqual(['Resume', 'Replay']);

    click('Resume');
    expect(audio.starts).toEqual([0, 1]);
    expect(FakeAudio.made).toHaveLength(1);
  });

  it('replays from the start', () => {
    const audio = readOnce();
    click('Replay');

    expect(audio.starts).toEqual([0, 0]);
    expect(buttons()[0]).toBe('Stop reading');
  });

  it('drops the audio when the summary changes', () => {
    readOnce();
    fixture.componentRef.setInput('text', 'A different summary.');
    fixture.detectChanges();

    expect(buttons()).toEqual(['Read aloud']);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:summary');
  });
});
