import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { Api, AttemptResult, Concept, ConceptDetails, Question } from '../api';
import { PracticePage } from './practice-page';

const concept = (id: string): Concept => ({
  id,
  name: id,
  summary: null,
  state: 'Accepted',
  proposedByModel: false,
  passageCount: 1,
  mastery: { level: 'New', attempts: 0, recentScore: null, lastPractisedAtUtc: null },
});

const question = (id: string, conceptId: string): Question => ({
  id,
  conceptId,
  prompt: `${id}?`,
  answer: id,
  generatedByModel: true,
  sourceId: null,
  sourceTitle: null,
  pageNumber: null,
  pageLabel: null,
  section: null,
  attempts: 0,
  lastScore: null,
  dueAtUtc: null,
});

describe('PracticePage', () => {
  const details = new Map<string, Subject<ConceptDetails>>();
  const questions = new Map<string, Subject<Question[]>>();
  const attempts: { questionId: string; response: Subject<AttemptResult> }[] = [];

  function open(fixture: ComponentFixture<PracticePage>, id: string): void {
    fixture.componentRef.setInput('id', id);
    fixture.detectChanges();
  }

  function respond(id: string): void {
    details.get(id)!.next({ concept: concept(id), passages: [] });
    details.get(id)!.complete();
    questions.get(id)!.next([question(`${id}1`, id), question(`${id}2`, id)]);
    questions.get(id)!.complete();
  }

  beforeEach(() => {
    details.clear();
    questions.clear();
    attempts.length = 0;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        {
          provide: Api,
          useValue: {
            details: (id: string) => {
              details.set(id, new Subject());
              return details.get(id);
            },
            questions: (id: string) => {
              questions.set(id, new Subject());
              return questions.get(id);
            },
            recordAttempt: (questionId: string) => {
              const response = new Subject<AttemptResult>();
              attempts.push({ questionId, response });
              return response;
            },
          },
        },
      ],
    });
  });

  it('shows the cards of the concept opened last, whichever answer comes back first', () => {
    const fixture = TestBed.createComponent(PracticePage);
    open(fixture, 'a');
    open(fixture, 'b');
    const page = fixture.componentInstance;

    respond('b');
    respond('a');

    expect(page['concept']()?.id).toBe('b');
    expect(page['questions']().map((item) => item.id)).toEqual(['b1', 'b2']);
  });

  it('ignores a second mark while the first is saving, and a mark for a card already passed', () => {
    const fixture = TestBed.createComponent(PracticePage);
    open(fixture, 'a');
    respond('a');
    const page = fixture.componentInstance;
    const first = page['card']()!;

    page.rate(first, 1);
    page.rate(first, 1);
    expect(attempts.map((item) => item.questionId)).toEqual([first.id]);

    attempts[0].response.next({ conceptId: 'a', mastery: concept('a').mastery, dueAtUtc: null });
    expect(page['index']()).toBe(1);
    page.rate(first, 0);
    expect(attempts).toHaveLength(1);
  });
});
