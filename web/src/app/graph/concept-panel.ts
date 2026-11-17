import { Component, input, model, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  Concept,
  ConceptPassage,
  PassageSuggestion,
  Place,
  Prerequisite,
} from '../api/concepts-api';
import { DueConcept } from '../api/practice-api';
import { isPagedCitation } from '../history/conversation-turns';
import { FileTarget } from '../passages';
import { dueSummary, masteryDetail } from '../practice/practice-deck';
import { SourceFileLink } from '../source-file-link';

export interface LinkedPrerequisite {
  link: Prerequisite;
  concept: Concept;
}

/** The selected concept: its summary, practice, prerequisites and passages, and what can be done with them. */
@Component({
  selector: 'app-concept-panel',
  imports: [FormsModule, RouterLink, SourceFileLink],
  templateUrl: './concept-panel.html',
  styleUrl: './concept-panel.css',
})
export class ConceptPanel {
  readonly concept = input.required<Concept>();
  /** What the concept builds on, with the state of each link. */
  readonly prerequisites = input<LinkedPrerequisite[]>([]);
  /** Concepts that could still be linked as a prerequisite. */
  readonly linkable = input<Concept[]>([]);
  readonly passages = input<ConceptPassage[]>([]);
  readonly suggestions = input<PassageSuggestion[] | null>(null);
  readonly cards = input<DueConcept | null>(null);
  readonly busy = input(false);
  readonly splitNames = model('');
  readonly prerequisiteToAdd = model('');

  readonly rename = output<string>();
  readonly saveSummary = output<string>();
  readonly accept = output<void>();
  readonly dismiss = output<void>();
  readonly open = output<Concept>();
  readonly addPrerequisite = output<string>();
  readonly removePrerequisite = output<string>();
  readonly propose = output<void>();
  readonly split = output<void>();
  readonly findPassages = output<void>();
  readonly linkPassage = output<PassageSuggestion>();
  readonly unlinkPassage = output<ConceptPassage>();
  readonly remove = output<void>();

  protected readonly masteryDetail = masteryDetail;
  protected readonly dueSummary = dueSummary;

  protected where(place: Place): string {
    const page = place.pageLabel ?? String(place.pageNumber);
    const section = place.section?.split(' > ').slice(-2).join(' › ');
    return section ? `${section} · p. ${page}` : `p. ${page}`;
  }

  /** Opens a PDF at the passage's page, and other sources at the passage's first words. */
  protected fileTarget(place: Place): FileTarget {
    return isPagedCitation(place) ? { page: place.pageNumber } : { text: place.snippet };
  }
}
