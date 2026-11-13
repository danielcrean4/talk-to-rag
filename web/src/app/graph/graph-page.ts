import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { Api, Concept, ConceptGraph, ConceptPassage, PassageSuggestion, Place } from '../api';
import { Auth, errorMessage } from '../auth';
import { isPagedCitation } from '../history/conversation-turns';
import { sourceFileUrl } from '../passages';
import { NODE_HEIGHT, NODE_WIDTH, layoutGraph } from './graph-layout';

@Component({
  selector: 'app-graph-page',
  imports: [FormsModule],
  templateUrl: './graph-page.html',
  styleUrl: './graph-page.css',
})
export class GraphPage {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);

  readonly nodeWidth = NODE_WIDTH;
  readonly nodeHeight = NODE_HEIGHT;

  readonly graph = signal<ConceptGraph>({ concepts: [], prerequisites: [] });
  readonly loaded = signal(false);
  readonly busy = signal('');
  readonly error = signal('');
  readonly selectedId = signal<string | null>(null);
  readonly passages = signal<ConceptPassage[]>([]);
  readonly suggestions = signal<PassageSuggestion[] | null>(null);

  newName = '';
  splitNames = '';
  prerequisiteToAdd = '';

  readonly layout = computed(() => layoutGraph(this.graph().concepts, this.graph().prerequisites));
  readonly selected = computed(
    () => this.graph().concepts.find((concept) => concept.id === this.selectedId()) ?? null,
  );
  readonly proposedCount = computed(
    () => this.graph().concepts.filter((concept) => concept.state === 'Proposed').length,
  );

  /** What the selected concept builds on, with the state of each link. */
  readonly selectedPrerequisites = computed(() => {
    const byId = new Map(this.graph().concepts.map((concept) => [concept.id, concept]));
    return this.graph()
      .prerequisites.filter((link) => link.conceptId === this.selectedId())
      .map((link) => ({ link, concept: byId.get(link.prerequisiteId)! }))
      .sort((a, b) => a.concept.name.localeCompare(b.concept.name));
  });

  /** Concepts that could still be added as a prerequisite of the selected one. */
  readonly linkable = computed(() => {
    const taken = new Set(this.selectedPrerequisites().map((item) => item.concept.id));
    return this.graph().concepts.filter(
      (concept) =>
        concept.id !== this.selectedId() && concept.state === 'Accepted' && !taken.has(concept.id),
    );
  });

  constructor() {
    this.run('Loading', this.api.graph(), (graph) => {
      this.graph.set(graph);
      this.loaded.set(true);
    });
  }

  select(concept: Concept): void {
    this.selectedId.set(concept.id);
    this.passages.set([]);
    this.suggestions.set(null);
    this.splitNames = '';
    this.prerequisiteToAdd = '';
    this.api.details(concept.id).subscribe({
      next: (details) => this.passages.set(details.passages),
      error: (error) => this.error.set(errorMessage(error)),
    });
  }

  isLinkedToSelection(conceptId: string, prerequisiteId: string): boolean {
    const selected = this.selectedId();
    return selected !== null && (conceptId === selected || prerequisiteId === selected);
  }

  add(): void {
    const name = this.newName.trim();
    if (!name) {
      return;
    }
    this.run('Adding', this.api.createConcept(name), (concept) => {
      this.newName = '';
      this.graph.update((graph) => ({ ...graph, concepts: [...graph.concepts, concept] }));
      this.select(concept);
    });
  }

  rename(concept: Concept, name: string): void {
    if (name.trim() && name.trim() !== concept.name) {
      this.run('Renaming', this.api.updateConcept(concept.id, { name }), (updated) =>
        this.replace(updated),
      );
    }
  }

  saveSummary(concept: Concept, summary: string): void {
    if (summary.trim() !== (concept.summary ?? '')) {
      this.run('Saving', this.api.updateConcept(concept.id, { summary }), (updated) =>
        this.replace(updated),
      );
    }
  }

  remove(concept: Concept): void {
    if (!confirm(`Delete "${concept.name}" and its links? This cannot be undone.`)) {
      return;
    }
    this.run('Deleting', this.api.deleteConcept(concept.id), () => {
      this.selectedId.set(null);
      this.reload();
    });
  }

  accept(concept: Concept): void {
    this.run('Accepting', this.api.accept(concept.id), (graph) => this.graph.set(graph));
  }

  dismiss(concept: Concept): void {
    this.run('Dismissing', this.api.reject(concept.id), (graph) => {
      this.graph.set(graph);
      if (this.selectedId() === concept.id) {
        this.selectedId.set(null);
      }
    });
  }

  propose(concept: Concept): void {
    this.run('Asking the local model', this.api.proposePrerequisites(concept.id), (graph) =>
      this.graph.set(graph),
    );
  }

  split(concept: Concept): void {
    const names = this.splitNames
      .split(',')
      .map((name) => name.trim())
      .filter(Boolean);
    if (names.length === 0) {
      return;
    }
    this.run('Splitting', this.api.split(concept.id, names), (graph) => {
      this.graph.set(graph);
      this.splitNames = '';
    });
  }

  addPrerequisite(concept: Concept, prerequisiteId: string): void {
    if (!prerequisiteId) {
      return;
    }
    this.run('Linking', this.api.addPrerequisite(concept.id, prerequisiteId), (graph) => {
      this.graph.set(graph);
      this.prerequisiteToAdd = '';
    });
  }

  removePrerequisite(concept: Concept, prerequisiteId: string): void {
    this.run('Unlinking', this.api.removePrerequisite(concept.id, prerequisiteId), (graph) =>
      this.graph.set(graph),
    );
  }

  findPassages(concept: Concept): void {
    this.run('Searching your sources', this.api.passageSuggestions(concept.id), (suggestions) =>
      this.suggestions.set(suggestions),
    );
  }

  linkPassage(concept: Concept, suggestion: PassageSuggestion): void {
    this.run('Linking', this.api.addPassage(concept.id, suggestion), (passage) => {
      this.passages.update((passages) => [...passages, passage]);
      this.suggestions.update(
        (all) =>
          all?.map((item) => (item === suggestion ? { ...item, linked: true } : item)) ?? null,
      );
      this.replace({ ...concept, passageCount: concept.passageCount + 1 });
    });
  }

  unlinkPassage(concept: Concept, passage: ConceptPassage): void {
    this.run('Unlinking', this.api.removePassage(concept.id, passage.id), () => {
      this.passages.update((passages) => passages.filter((item) => item.id !== passage.id));
      this.replace({ ...concept, passageCount: Math.max(0, concept.passageCount - 1) });
      this.suggestions.set(null);
    });
  }

  where(place: Place): string {
    const page = place.pageLabel ?? String(place.pageNumber);
    const section = place.section?.split(' > ').slice(-2).join(' › ');
    return section ? `${section} · p. ${page}` : `p. ${page}`;
  }

  /** Opens a PDF at the passage's page, and other sources at the passage's first words. */
  fileUrl(place: Place): string {
    return sourceFileUrl(
      place.sourceId,
      this.auth.token(),
      isPagedCitation(place) ? { page: place.pageNumber } : { text: place.snippet },
    );
  }

  private reload(): void {
    this.run('Loading', this.api.graph(), (graph) => this.graph.set(graph));
  }

  private replace(concept: Concept): void {
    this.graph.update((graph) => ({
      ...graph,
      concepts: graph.concepts.map((item) => (item.id === concept.id ? concept : item)),
    }));
  }

  private run<T>(label: string, request: Observable<T>, done: (result: T) => void): void {
    this.busy.set(label);
    this.error.set('');
    request.subscribe({
      next: (result) => {
        this.busy.set('');
        done(result);
      },
      error: (error) => {
        this.busy.set('');
        this.error.set(errorMessage(error));
      },
    });
  }
}
