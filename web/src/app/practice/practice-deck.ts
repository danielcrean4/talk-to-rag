import { Mastery, Question } from '../api';
import { isPagedCitation } from '../history/conversation-turns';
import { locationLabel } from '../passages';

/** The platform name flashcard attempts are recorded under. */
export const FLASHCARDS_PLATFORM = 'flashcards';

export interface Mark {
  label: string;
  score: number;
}

/** How the user rates an answer once it is shown, and the score each rating records. */
export const MARKS: readonly Mark[] = [
  { label: 'Missed', score: 0 },
  { label: 'Almost', score: 0.5 },
  { label: 'Got it', score: 1 },
];

/**
 * The order to practise in: cards never answered first, then the ones last answered worst.
 * Cards that tie keep the order the API gave them.
 */
export function practiceOrder(questions: readonly Question[]): Question[] {
  const rank = (question: Question) => question.lastScore ?? -1;
  return questions
    .map((question, index) => ({ question, index }))
    .sort((a, b) => rank(a.question) - rank(b.question) || a.index - b.index)
    .map((item) => item.question);
}

/** The card after an answer with this score, as the API will report it next time. */
export function answered(question: Question, score: number): Question {
  return { ...question, attempts: question.attempts + 1, lastScore: score };
}

/** Where a card came from, such as "Euclid · Book I · p. 4", or null for a card with no source. */
export function cardSource(question: Question): string | null {
  if (!question.sourceId) {
    return null;
  }
  const paged =
    question.pageNumber !== null &&
    isPagedCitation({ pageNumber: question.pageNumber, pageLabel: question.pageLabel });
  return [question.sourceTitle, locationLabel(question, paged)].filter(Boolean).join(' · ');
}

/** "Not practised yet", or the level with the number of attempts and the recent score. */
export function masteryDetail(mastery: Mastery): string {
  if (mastery.attempts === 0) {
    return 'Not practised yet';
  }
  const attempts = `${mastery.attempts} ${mastery.attempts === 1 ? 'attempt' : 'attempts'}`;
  const score =
    mastery.recentScore === null ? '' : `, recent score ${Math.round(mastery.recentScore * 100)}%`;
  return `${mastery.level}: ${attempts}${score}`;
}

/** How a finished round went, such as "2 of 3 right". Only "Got it" counts as right. */
export function roundSummary(scores: readonly number[]): string {
  const right = scores.filter((score) => score === 1).length;
  return `${right} of ${scores.length} right`;
}
