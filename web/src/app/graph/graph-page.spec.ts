import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { Api, Concept, ConceptDetails, ConceptPassage } from '../api';
import { GraphPage } from './graph-page';

const concept = (id: string): Concept => ({
  id,
  name: id,
  summary: null,
  state: 'Accepted',
  proposedByModel: false,
  passageCount: 1,
  mastery: { level: 'New', attempts: 0, recentScore: null, lastPractisedAtUtc: null },
});

const passage = (id: string): ConceptPassage => ({
  id,
  sourceId: 's',
  sourceTitle: 'Tides',
  pageNumber: 1,
  pageLabel: null,
  section: null,
  snippet: id,
});

describe('GraphPage', () => {
  const details = new Map<string, Subject<ConceptDetails>>();

  beforeEach(() => {
    details.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        {
          provide: Api,
          useValue: {
            graph: () => of({ concepts: [concept('a'), concept('b')], prerequisites: [] }),
            due: () => of({ questions: [], dueCount: 0, newCount: 0, concepts: [] }),
            details: (id: string) => {
              details.set(id, new Subject());
              return details.get(id);
            },
          },
        },
      ],
    });
  });

  it('shows the passages of the concept selected last, whichever answer comes back first', () => {
    const fixture = TestBed.createComponent(GraphPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;

    page.select(concept('a'));
    page.select(concept('b'));
    details.get('b')!.next({ concept: concept('b'), passages: [passage('of b')] });
    details.get('a')!.next({ concept: concept('a'), passages: [passage('of a')] });

    expect(page.selectedId()).toBe('b');
    expect(page.passages().map((item) => item.id)).toEqual(['of b']);
  });

  it('disables every action on the selected concept while one is running', () => {
    const fixture = TestBed.createComponent(GraphPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    const proposed: Concept = { ...concept('a'), state: 'Proposed' };
    page.graph.set({
      concepts: [proposed, concept('b')],
      prerequisites: [{ conceptId: 'a', prerequisiteId: 'b', state: 'Proposed' }],
    });
    page.select(proposed);
    details.get('a')!.next({ concept: proposed, passages: [passage('p')] });
    page.splitNames = 'x, y';
    fixture.detectChanges();

    const panel = fixture.nativeElement.querySelector('aside') as HTMLElement;
    const actions = () =>
      Array.from(
        panel.querySelectorAll<HTMLButtonElement>('.proposal button, .links button, .row button'),
      )
        .concat(Array.from(panel.querySelectorAll<HTMLButtonElement>('.passages button')))
        .map((button) => [button.textContent?.trim(), button.disabled]);

    expect(actions().filter(([, disabled]) => disabled)).toEqual([]);
    page.busy.set('Accepting');
    fixture.detectChanges();
    expect(actions()).toEqual([
      ['Keep', true],
      ['Dismiss', true],
      ['b', false],
      ['Keep link', true],
      ['×', true],
      ['Suggest prerequisites', true],
      ['Split', true],
      ['Find passages in my sources', true],
      ['Delete concept', true],
      ['Unlink', true],
    ]);
  });
});
