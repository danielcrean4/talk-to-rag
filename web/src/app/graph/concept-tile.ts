import { Component, input } from '@angular/core';
import { Concept } from '../api/concepts-api';

/** A concept's button: its name, then its mastery, linked passages and due cards, or that the model suggested it. */
@Component({
  selector: 'button[appConceptTile]',
  template: `
    @let concept = this.concept();
    <span class="name">{{ concept.name }}</span>
    @if (concept.state === 'Proposed') {
      <span class="meta">suggested</span>
    } @else {
      <span class="meta">
        <span class="mastery" [attr.data-level]="concept.mastery.level">{{
          concept.mastery.level
        }}</span>
        @if (concept.passageCount) {
          · {{ concept.passageCount }} linked
        }
        @if (due(); as due) {
          · <span class="due">{{ due }} due</span>
        }
      </span>
    }
  `,
  styleUrl: './concept-tile.css',
  host: {
    '[class.proposed]': "concept().state === 'Proposed'",
    '[class.selected]': 'selected()',
    '[attr.aria-pressed]': 'selected()',
  },
})
export class ConceptTile {
  readonly concept = input.required<Concept>({ alias: 'appConceptTile' });
  readonly selected = input(false);
  /** Flashcards due now. */
  readonly due = input(0);
}
