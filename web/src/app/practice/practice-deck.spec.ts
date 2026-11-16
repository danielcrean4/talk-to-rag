import { Mastery, Question } from '../api';
import {
  MARKS,
  answered,
  cardSource,
  masteryDetail,
  practiceOrder,
  roundSummary,
} from './practice-deck';

const question = (id: string, lastScore: number | null = null, attempts = 0): Question => ({
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

describe('practiceOrder', () => {
  it('puts unanswered cards first, then the worst answered', () => {
    const order = practiceOrder([
      question('good', 1),
      question('new'),
      question('missed', 0),
      question('almost', 0.5),
    ]);
    expect(order.map((q) => q.id)).toEqual(['new', 'missed', 'almost', 'good']);
  });

  it('keeps the given order between cards that tie', () => {
    const order = practiceOrder([question('a'), question('b', 1), question('c'), question('d', 1)]);
    expect(order.map((q) => q.id)).toEqual(['a', 'c', 'b', 'd']);
  });

  it('leaves the input alone', () => {
    const input = [question('b', 1), question('a')];
    practiceOrder(input);
    expect(input.map((q) => q.id)).toEqual(['b', 'a']);
  });
});

describe('answered', () => {
  it('counts the attempt and keeps the score as the last one', () => {
    const card = answered(question('a', 0, 2), 1);
    expect(card.attempts).toBe(3);
    expect(card.lastScore).toBe(1);
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
      'Learning: 1 attempt, recent score 50%',
    );
    expect(masteryDetail(mastery({ level: 'Solid', attempts: 4, recentScore: 0.875 }))).toBe(
      'Solid: 4 attempts, recent score 88%',
    );
  });
});

describe('roundSummary', () => {
  it('counts only full marks as right', () => {
    expect(roundSummary([1, 0.5, 0, 1])).toBe('2 of 4 right');
  });
});
