import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DueCards, PracticeApi, Question } from '../api/practice-api';
import { errorMessage } from '../auth';
import { Flashcard } from './flashcard';
import { FLASHCARDS_PLATFORM, dueSummary, roundSummary } from './practice-deck';

/** Steps through the cards due now across all concepts, then a few new ones. */
@Component({
  selector: 'app-review-page',
  imports: [RouterLink, Flashcard],
  templateUrl: './review-page.html',
  styleUrl: './practice-page.css',
})
export class ReviewPage {
  private readonly api = inject(PracticeApi);

  protected readonly due = signal<DueCards | null>(null);
  protected readonly index = signal(0);
  protected readonly scores = signal<number[]>([]);
  protected readonly busy = signal('');
  protected readonly error = signal('');

  protected readonly deck = computed(() => this.due()?.questions ?? []);
  protected readonly card = computed(() => this.deck()[this.index()] ?? null);
  protected readonly finished = computed(
    () => this.deck().length > 0 && this.index() >= this.deck().length,
  );
  protected readonly summary = computed(() => roundSummary(this.scores()));
  protected readonly dueText = computed(() => {
    const due = this.due();
    return due ? dueSummary(due.dueCount, due.newCount) : '';
  });
  private readonly conceptNames = computed(
    () => new Map(this.due()?.concepts.map((item) => [item.conceptId, item.conceptName]) ?? []),
  );

  constructor() {
    this.load();
  }

  conceptName(question: Question): string | null {
    return this.conceptNames().get(question.conceptId) ?? null;
  }

  /** Ignores a mark while the last one is saving, or for a card no longer shown. */
  rate(question: Question, score: number): void {
    if (this.busy() || question.id !== this.card()?.id) {
      return;
    }
    this.busy.set('Saving');
    this.error.set('');
    this.api.recordAttempt(question.id, FLASHCARDS_PLATFORM, score).subscribe({
      next: () => {
        this.busy.set('');
        this.scores.update((scores) => [...scores, score]);
        this.index.update((index) => index + 1);
      },
      error: (error) => this.fail(error),
    });
  }

  /** Asks the API what is due; after a round, that brings the next batch of new cards. */
  load(): void {
    this.busy.set('Loading');
    this.error.set('');
    this.api.due().subscribe({
      next: (due) => {
        this.busy.set('');
        this.due.set(due);
        this.index.set(0);
        this.scores.set([]);
      },
      error: (error) => this.fail(error),
    });
  }

  private fail(error: unknown): void {
    this.busy.set('');
    this.error.set(errorMessage(error));
  }
}
