import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Api, Concept, Question } from '../api';
import { Auth, errorMessage } from '../auth';
import { sourceFileUrl } from '../passages';
import {
  FLASHCARDS_PLATFORM,
  MARKS,
  answered,
  cardSource,
  masteryDetail,
  practiceOrder,
  roundSummary,
} from './practice-deck';

/** Steps through a concept's flashcards and records each answer as an attempt. */
@Component({
  selector: 'app-practice-page',
  imports: [RouterLink],
  templateUrl: './practice-page.html',
  styleUrl: './practice-page.css',
})
export class PracticePage {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);

  /** Bound from the `concepts/:id/practice` route. */
  readonly id = input.required<string>();

  protected readonly marks = MARKS;
  protected readonly cardSource = cardSource;
  protected readonly concept = signal<Concept | null>(null);
  protected readonly questions = signal<Question[]>([]);
  protected readonly deck = signal<Question[]>([]);
  protected readonly index = signal(0);
  protected readonly revealed = signal(false);
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

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
  }

  reveal(): void {
    this.revealed.set(true);
  }

  rate(question: Question, score: number): void {
    this.busy.set('Saving');
    this.error.set('');
    this.api.recordAttempt(question.id, FLASHCARDS_PLATFORM, score).subscribe({
      next: (result) => {
        this.busy.set('');
        this.concept.update((concept) => concept && { ...concept, mastery: result.mastery });
        this.questions.update((all) =>
          all.map((item) => (item.id === question.id ? answered(item, score) : item)),
        );
        this.scores.update((scores) => [...scores, score]);
        this.revealed.set(false);
        this.index.update((index) => index + 1);
      },
      error: (error) => this.fail(error),
    });
  }

  /** Starts a new round, worst-known cards first. */
  again(): void {
    this.deck.set(practiceOrder(this.questions()));
    this.index.set(0);
    this.revealed.set(false);
    this.scores.set([]);
  }

  generate(conceptId: string): void {
    this.busy.set('Asking the local model for flashcards');
    this.error.set('');
    this.api.generateQuestions(conceptId).subscribe({
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

  fileUrl(sourceId: string, question: Question): string {
    return sourceFileUrl(sourceId, this.auth.token(), { page: question.pageNumber ?? undefined });
  }

  private load(conceptId: string): void {
    this.loaded.set(false);
    this.busy.set('Loading');
    this.error.set('');
    forkJoin([this.api.details(conceptId), this.api.questions(conceptId)]).subscribe({
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
    });
  }

  private fail(error: unknown): void {
    this.busy.set('');
    this.error.set(errorMessage(error));
  }
}
