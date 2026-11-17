import { Component, effect, input, output, signal } from '@angular/core';
import { Question } from '../api';
import { SourceFileLink } from '../source-file-link';
import { MARKS, cardSource } from './practice-deck';

/** One flashcard: the prompt, then on request the answer, its source and the marks. */
@Component({
  selector: 'app-flashcard',
  imports: [SourceFileLink],
  templateUrl: './flashcard.html',
  styleUrl: './flashcard.css',
})
export class Flashcard {
  readonly card = input.required<Question>();
  /** Such as "Card 2 of 5". */
  readonly progress = input('');
  /** The concept the card tests, shown when cards from several concepts are mixed. */
  readonly concept = input<string | null>(null);
  readonly busy = input(false);
  /** The score of the mark chosen. */
  readonly rated = output<number>();

  protected readonly marks = MARKS;
  protected readonly cardSource = cardSource;
  protected readonly revealed = signal(false);

  constructor() {
    effect(() => {
      this.card();
      this.revealed.set(false);
    });
  }
}
