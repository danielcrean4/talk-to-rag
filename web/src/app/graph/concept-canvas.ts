import { Component, input, output } from '@angular/core';
import { Concept } from '../api/concepts-api';
import { DueConcept } from '../api/practice-api';
import { ConceptTile } from './concept-tile';
import { GraphLayout, NODE_HEIGHT, NODE_WIDTH } from './graph-layout';

/** The concepts drawn in rows by prerequisite depth, with a curve for each link. */
@Component({
  selector: 'app-concept-canvas',
  imports: [ConceptTile],
  templateUrl: './concept-canvas.html',
  styleUrl: './concept-canvas.css',
})
export class ConceptCanvas {
  readonly layout = input.required<GraphLayout>();
  readonly selectedId = input<string | null>(null);
  /** Flashcards due per concept id. */
  readonly due = input<Map<string, DueConcept>>(new Map());
  readonly pick = output<Concept>();

  protected readonly nodeWidth = NODE_WIDTH;
  protected readonly nodeHeight = NODE_HEIGHT;

  protected isLinkedToSelection(conceptId: string, prerequisiteId: string): boolean {
    const selected = this.selectedId();
    return selected !== null && (conceptId === selected || prerequisiteId === selected);
  }
}
