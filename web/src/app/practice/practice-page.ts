import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Concept, ConceptsApi } from '../api/concepts-api';
import { PracticeApi, Question } from '../api/practice-api';
import { errorMessage } from '../auth';
import { latestRequest } from '../latest-request';
import { Flashcard } from './flashcard';
import {
  FLASHCARDS_PLATFORM,
  answered,
  dueSummary,
  isDue,
  masteryDetail,
  practiceOrder,
  roundSummary,
} from './practice-deck';

/** Steps through a concept's flashcards and records each answer as an attempt. */
@Component({
  selector: 'app-practice-page',
  imports: [RouterLink, Flashcard],
  templateUrl: './practice-page.html',
  styleUrl: './practice-page.css',
})
export class PracticePage {
  private readonly conceptsApi = inject(ConceptsApi);
  private readonly practiceApi = inject(PracticeApi);
  private readonly loadRun = latestRequest();

  /** Bound from the `concepts/:id/practice` route. */
  readonly id = input.required<string>();

  protected readonly concept = signal<Concept | null>(null);
  protected readonly questions = signal<Question[]>([]);
  protected readonly deck = signal<Question[]>([]);
  protected readonly index = signal(0);
  protected readonly scores = signal<number[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal('');
  protected readonly error = signal('');

  protected readonly card = computed(() => this.deck()[this.index()] ?? null);
  protected readonly finished = computed(
    () => this.deck().length > 0 && this.index() >= this.deck().length,
  );
  protected readonly masteryText = computed(() => {
    const concept = this.concept();
    return concept ? masteryDetail(concept.mastery) : '';
  });
  protected readonly summary = computed(() => roundSummary(this.scores()));
  /** Counted when the cards load or an answer comes back, not as the clock passes a due time. */
  protected readonly dueText = computed(() => {
    const now = new Date();
    const questions = this.questions();
    const due = questions.filter((question) => isDue(question, now)).length;
    return dueSummary(due, questions.filter((question) => question.dueAtUtc === null).length);
  });

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
  }

  /** Ignores a mark while the last one is saving, or for a card no longer shown. */
  rate(question: Question, score: number): void {
    if (this.busy() || question.id !== this.card()?.id) {
      return;
    }
    this.busy.set('Saving');
    this.error.set('');
    this.practiceApi.recordAttempt(question.id, FLASHCARDS_PLATFORM, score).subscribe({
      next: (result) => {
        this.busy.set('');
        this.concept.update((concept) => concept && { ...concept, mastery: result.mastery });
        this.questions.update((all) =>
          all.map((item) =>
            item.id === question.id ? answered(item, score, result.dueAtUtc) : item,
          ),
        );
        this.scores.update((scores) => [...scores, score]);
        this.index.update((index) => index + 1);
      },
      error: (error) => this.fail(error),
    });
  }

  /** Starts a new round, due cards first. */
  again(): void {
    this.deck.set(practiceOrder(this.questions(), new Date()));
    this.index.set(0);
    this.scores.set([]);
  }

  generate(conceptId: string): void {
    this.busy.set('Asking the local model for flashcards');
    this.error.set('');
    this.practiceApi.generateQuestions(conceptId).subscribe({
      next: (added) => {
        this.busy.set('');
        if (added.length === 0) {
          this.error.set('The model wrote no new flashcards. Try again, or link more passages.');
          return;
        }
        this.questions.update((all) => [...all, ...added]);
        this.again();
      },
      error: (error) => this.fail(error),
    });
  }

  private load(conceptId: string): void {
    this.loaded.set(false);
    this.busy.set('Loading');
    this.error.set('');
    this.loadRun.run(
      forkJoin([this.conceptsApi.details(conceptId), this.practiceApi.questions(conceptId)]),
      {
        next: ([details, questions]) => {
          this.busy.set('');
          this.concept.set(details.concept);
          this.questions.set(questions);
          this.again();
          this.loaded.set(true);
        },
        error: (error) => {
          this.fail(error);
          this.loaded.set(true);
        },
      },
    );
  }

  private fail(error: unknown): void {
    this.busy.set('');
    this.error.set(errorMessage(error));
  }
}
