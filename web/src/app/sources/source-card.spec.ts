import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SourceDetailsChange, Source } from '../api/sources-api';
import { SourceCard } from './source-card';
import { SOURCE_DRAG_TYPE } from './source-list';

const source = (license: string | null): Source => ({
  id: 's1',
  title: 'Tides',
  fileName: 'tides.pdf',
  contentType: 'application/pdf',
  sizeBytes: 1,
  status: 'Ready',
  failureReason: null,
  chunkCount: 3,
  createdAtUtc: '2026-01-01T00:00:00Z',
  tagIds: [],
  license,
  sourceUrl: null,
});

describe('SourceCard', () => {
  let fixture: ComponentFixture<SourceCard>;
  const host = () => fixture.nativeElement as HTMLElement;
  const button = (text: string) =>
    Array.from(host().querySelectorAll('button')).find((item) => item.textContent?.trim() === text);

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    fixture = TestBed.createComponent(SourceCard);
    fixture.componentRef.setInput('appSourceCard', source('CC BY 4.0'));
    fixture.componentRef.setInput('tags', []);
    fixture.componentRef.setInput('notebooks', []);
    fixture.detectChanges();
  });

  async function openDetails(): Promise<void> {
    const opened: boolean[] = [];
    fixture.componentInstance.editingChange.subscribe((open) => opened.push(open));
    button('Edit details')!.click();
    expect(opened).toEqual([true]);
    fixture.componentRef.setInput('editing', true);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('starts the drafts from the source and saves them trimmed', async () => {
    await openDetails();
    const [license, origin] = Array.from(host().querySelectorAll<HTMLInputElement>('form input'));
    expect(license.value).toBe('CC BY 4.0');

    origin.value = '  https://example.org/tides  ';
    origin.dispatchEvent(new Event('input'));
    const saved: Required<SourceDetailsChange>[] = [];
    fixture.componentInstance.saveDetails.subscribe((change) => saved.push(change));
    button('Save')!.click();

    expect(saved).toEqual([{ license: 'CC BY 4.0', sourceUrl: 'https://example.org/tides' }]);
  });

  it('keeps the drafts when polling hands it a fresh copy of the source', async () => {
    await openDetails();
    const license = host().querySelector<HTMLInputElement>('form input')!;
    license.value = 'Public domain';
    license.dispatchEvent(new Event('input'));

    fixture.componentRef.setInput('appSourceCard', source('CC BY 4.0'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(host().querySelector<HTMLInputElement>('form input')!.value).toBe('Public domain');
  });

  it('cannot be dragged while its details are open, and carries its id when dragged', () => {
    expect(host().getAttribute('draggable')).toBe('true');
    fixture.componentRef.setInput('editing', true);
    fixture.detectChanges();
    expect(host().getAttribute('draggable')).toBe('false');

    const data = new Map<string, string>();
    const event = new Event('dragstart') as DragEvent;
    Object.defineProperty(event, 'dataTransfer', {
      value: { setData: (type: string, value: string) => data.set(type, value) },
    });
    let started = false;
    fixture.componentInstance.dragStarted.subscribe(() => (started = true));
    host().dispatchEvent(event);

    expect(data.get(SOURCE_DRAG_TYPE)).toBe('s1');
    expect(started).toBe(true);
  });
});
