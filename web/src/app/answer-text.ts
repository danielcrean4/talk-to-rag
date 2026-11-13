import { Component, computed, input, output } from '@angular/core';
import { answerParts } from './passages';

/** A summary with its [n] markers as buttons that lead to the cited passage. */
@Component({
  selector: 'app-answer-text',
  // The text is shown with its line breaks, so the template adds no whitespace of its own.
  // prettier-ignore
  template: `
    @for (paragraph of paragraphs(); track $index) {
      <p>@for (part of paragraph; track $index) {@if ('cite' in part) {<button type="button" class="cite" [attr.aria-label]="'Show passage ' + part.cite" (click)="cite.emit(part.cite)">[{{ part.cite }}]</button>} @else {<span>{{ part.text }}</span>}}</p>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    p {
      margin: 0 0 0.75rem;
      white-space: pre-line;
    }
    p:last-child {
      margin-bottom: 0;
    }
    .cite {
      padding: 0 0.2rem;
      margin: 0 0.05rem;
      border: none;
      background: none;
      color: var(--accent);
      font-size: 0.8125rem;
      font-weight: 600;
      vertical-align: baseline;
    }
    .cite:hover {
      text-decoration: underline;
    }
  `,
})
export class AnswerText {
  readonly answer = input.required<string>();
  readonly cite = output<number>();

  protected readonly paragraphs = computed(() => answerParts(this.answer()));
}
