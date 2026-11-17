import { Mastery } from '../api/concepts-api';
import { Question } from '../api/practice-api';
import {
  MARKS,
  answered,
  cardSource,
  dueSummary,
  isDue,
  masteryDetail,
  practiceOrder,
  roundSummary,
} from './practice-deck';

const now = new Date('2026-11-16T12:00:00Z');

const question = (
  id: string,
  lastScore: number | null = null,
  attempts = 0,
  dueAtUtc: string | null = null,
): Question => ({
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
  attempts: lastScore === null ? attempts : Math.max(attempts, 1),
  lastScore,
  dueAtUtc: lastScore === null ? dueAtUtc : (dueAtUtc ?? '2026-11-20T12:00:00Z'),
});

const mastery = (changes: Partial<Mastery> = {}): Mastery => ({
  level: 'New',
  attempts: 0,
  recentScore: null,
  lastPractisedAtUtc: null,
  ...changes,
});

describe('MARKS', () => {
  it('scores from 0 to 1, worst first', () => {
    const scores = MARKS.map((mark) => mark.score);
    expect(scores[0]).toBe(0);
    expect(scores.at(-1)).toBe(1);
    expect([...scores].sort((a, b) => a - b)).toEqual(scores);
  });
});

describe('isDue', () => {
  it('is true from the due time on, and never for a card not yet answered', () => {
    expect(isDue(question('a', 1, 1, '2026-11-16T12:00:00Z'), now)).toBe(true);
    expect(isDue(question('b', 1, 1, '2026-11-16T12:00:01Z'), now)).toBe(false);
    expect(isDue(question('c'), now)).toBe(false);
  });
});

describe('practiceOrder', () => {
  it('puts due cards first, longest overdue leading, then new cards, then the rest by due time', () => {
    const order = practiceOrder(
      [
        question('later', 1, 1, '2026-11-30T00:00:00Z'),
        question('new'),
        question('due', 0, 1, '2026-11-16T00:00:00Z'),
        question('soon', 0.5, 1, '2026-11-18T00:00:00Z'),
        question('overdue', 1, 1, '2026-11-10T00:00:00Z'),
      ],
      now,
    );
    expect(order.map((q) => q.id)).toEqual(['overdue', 'due', 'new', 'soon', 'later']);
  });

  it('keeps the given order between cards that tie', () => {
    const order = practiceOrder(
      [question('a'), question('b', 1), question('c'), question('d', 1)],
      now,
    );
    expect(order.map((q) => q.id)).toEqual(['a', 'c', 'b', 'd']);
  });

  it('leaves the input alone', () => {
    const input = [question('b', 1), question('a')];
    practiceOrder(input, now);
    expect(input.map((q) => q.id)).toEqual(['b', 'a']);
  });
});

describe('answered', () => {
  it('counts the attempt and keeps the score and the new due time', () => {
    const card = answered(question('a', 0, 2), 1, '2026-11-19T12:00:00Z');
    expect(card.attempts).toBe(3);
    expect(card.lastScore).toBe(1);
    expect(card.dueAtUtc).toBe('2026-11-19T12:00:00Z');
  });
});

describe('dueSummary', () => {
  it('names what there is to review', () => {
    expect(dueSummary(3, 2)).toBe('3 due, 2 new');
    expect(dueSummary(1, 0)).toBe('1 due');
    expect(dueSummary(0, 4)).toBe('4 new');
    expect(dueSummary(0, 0)).toBe('Nothing due');
  });
});

describe('cardSource', () => {
  it('is null for a card written by hand without a source', () => {
    expect(cardSource(question('a'))).toBeNull();
  });

  it('gives the title, section and page of a paged source', () => {
    const card = {
      ...question('a'),
      sourceId: 's',
      sourceTitle: 'Elements',
      pageNumber: 12,
      pageLabel: 'xii',
      section: 'Book I > Definitions',
    };
    expect(cardSource(card)).toBe('Elements · Book I › Definitions · p. xii');
  });

  it('leaves out the page of a source without pages', () => {
    const card = { ...question('a'), sourceId: 's', sourceTitle: 'Notes', pageNumber: 1 };
    expect(cardSource(card)).toBe('Notes');
  });
});

describe('masteryDetail', () => {
  it('says when a concept was never practised', () => {
    expect(masteryDetail(mastery())).toBe('Not practised yet');
  });

  it('gives the level, attempts and recent score', () => {
    expect(masteryDetail(mastery({ level: 'Learning', attempts: 1, recentScore: 0.5 }))).toBe(
      '1 attempt, recent score 50%',
    );
    expect(masteryDetail(mastery({ level: 'Solid', attempts: 4, recentScore: 0.875 }))).toBe(
      '4 attempts, recent score 88%',
    );
  });
});

describe('roundSummary', () => {
  it('counts only full marks as right', () => {
    expect(roundSummary([1, 0.5, 0, 1])).toBe('2 of 4 right');
  });
});
