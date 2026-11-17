import { Component, computed, input, output } from '@angular/core';
import { Concept } from '../api/concepts-api';
import { DueConcept } from '../api/practice-api';
import { ConceptTile } from './concept-tile';
import { PlacedConcept, levelGroups } from './graph-layout';

/** The concepts as a list grouped by prerequisite depth, for screens too narrow for the graph. */
@Component({
  selector: 'app-concept-list',
  imports: [ConceptTile],
  template: `
    @for (group of groups(); track group.level) {
      <section [attr.aria-label]="heading(group.level)">
        <h3>{{ heading(group.level) }}</h3>
        <ul>
          @for (concept of group.concepts; track concept.id) {
            <li>
              <button
                type="button"
                [appConceptTile]="concept"
                [selected]="concept.id === selectedId()"
                [due]="due().get(concept.id)?.due ?? 0"
                (click)="pick.emit(concept)"
              ></button>
            </li>
          }
        </ul>
      </section>
    }
    <p class="hint">Foundations come first; each level builds on the ones before it.</p>
  `,
  styleUrl: './concept-list.css',
})
export class ConceptList {
  readonly nodes = input.required<PlacedConcept[]>();
  readonly selectedId = input<string | null>(null);
  /** Flashcards due per concept id. */
  readonly due = input<Map<string, DueConcept>>(new Map());
  readonly pick = output<Concept>();

  protected readonly groups = computed(() => levelGroups(this.nodes()));

  protected heading(level: number): string {
    return level === 0 ? 'Foundations' : `Level ${level + 1}`;
  }
}
