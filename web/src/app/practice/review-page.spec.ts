import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { AttemptResult, DueCards, PracticeApi, Question } from '../api/practice-api';
import { ReviewPage } from './review-page';

const question = (id: string): Question => ({
  id,
  conceptId: 'c',
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

const due: DueCards = {
  questions: [question('q1'), question('q2')],
  dueCount: 0,
  newCount: 2,
  concepts: [{ conceptId: 'c', conceptName: 'Tides', due: 0, new: 2 }],
};

describe('ReviewPage', () => {
  const attempts: { questionId: string; response: Subject<AttemptResult> }[] = [];

  beforeEach(() => {
    attempts.length = 0;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        {
          provide: PracticeApi,
          useValue: {
            due: () => of(due),
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

  it('ignores a second mark while the first is saving, and a mark for a card already passed', () => {
    const page = TestBed.createComponent(ReviewPage).componentInstance;
    const first = page['card']()!;

    page.rate(first, 1);
    page.rate(first, 1);
    expect(attempts.map((item) => item.questionId)).toEqual(['q1']);

    attempts[0].response.next({
      conceptId: 'c',
      mastery: { level: 'Learning', attempts: 1, recentScore: 1, lastPractisedAtUtc: null },
      dueAtUtc: null,
    });
    expect(page['card']()?.id).toBe('q2');
    page.rate(first, 0);
    expect(attempts).toHaveLength(1);
  });
});
