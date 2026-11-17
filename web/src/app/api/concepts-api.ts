import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type ProposalState = 'Proposed' | 'Accepted';

export type MasteryLevel = 'New' | 'Learning' | 'Solid';

/** Worked out by the API from the concept's attempts; never stored. */
export interface Mastery {
  level: MasteryLevel;
  attempts: number;
  /** Average score of the last few attempts, from 0 to 1. */
  recentScore: number | null;
  lastPractisedAtUtc: string | null;
}

export interface Concept {
  id: string;
  name: string;
  summary: string | null;
  state: ProposalState;
  proposedByModel: boolean;
  passageCount: number;
  mastery: Mastery;
}

/** "Understand prerequisiteId before conceptId." */
export interface Prerequisite {
  conceptId: string;
  prerequisiteId: string;
  state: ProposalState;
}

export interface ConceptGraph {
  concepts: Concept[];
  prerequisites: Prerequisite[];
}

export interface Place {
  sourceId: string;
  sourceTitle: string;
  pageNumber: number;
  pageLabel: string | null;
  section: string | null;
  snippet: string;
}

export interface ConceptPassage extends Place {
  id: string;
}

export interface PassageSuggestion extends Place {
  linked: boolean;
}

export interface ConceptDetails {
  concept: Concept;
  passages: ConceptPassage[];
}

@Injectable({ providedIn: 'root' })
export class ConceptsApi {
  private readonly http = inject(HttpClient);

  graph(): Observable<ConceptGraph> {
    return this.http.get<ConceptGraph>('/api/concepts/graph');
  }

  createConcept(name: string): Observable<Concept> {
    return this.http.post<Concept>('/api/concepts', { name });
  }

  updateConcept(id: string, changes: { name?: string; summary?: string }): Observable<Concept> {
    return this.http.patch<Concept>(`/api/concepts/${id}`, changes);
  }

  deleteConcept(id: string): Observable<void> {
    return this.http.delete<void>(`/api/concepts/${id}`);
  }

  accept(id: string): Observable<ConceptGraph> {
    return this.http.post<ConceptGraph>(`/api/concepts/${id}/accept`, {});
  }

  reject(id: string): Observable<ConceptGraph> {
    return this.http.post<ConceptGraph>(`/api/concepts/${id}/reject`, {});
  }

  addPrerequisite(id: string, prerequisiteId: string): Observable<ConceptGraph> {
    return this.http.put<ConceptGraph>(`/api/concepts/${id}/prerequisites/${prerequisiteId}`, {});
  }

  removePrerequisite(id: string, prerequisiteId: string): Observable<ConceptGraph> {
    return this.http.delete<ConceptGraph>(`/api/concepts/${id}/prerequisites/${prerequisiteId}`);
  }

  split(id: string, names: string[]): Observable<ConceptGraph> {
    return this.http.post<ConceptGraph>(`/api/concepts/${id}/split`, { names });
  }

  proposePrerequisites(id: string): Observable<ConceptGraph> {
    return this.http.post<ConceptGraph>(`/api/concepts/${id}/propose-prerequisites`, {});
  }

  details(id: string): Observable<ConceptDetails> {
    return this.http.get<ConceptDetails>(`/api/concepts/${id}`);
  }

  passageSuggestions(id: string): Observable<PassageSuggestion[]> {
    return this.http.get<PassageSuggestion[]>(`/api/concepts/${id}/passage-suggestions`);
  }

  addPassage(id: string, place: Place): Observable<ConceptPassage> {
    const { sourceId, pageNumber, pageLabel, section, snippet } = place;
    return this.http.post<ConceptPassage>(`/api/concepts/${id}/passages`, {
      sourceId,
      pageNumber,
      pageLabel,
      section,
      snippet,
    });
  }

  removePassage(id: string, passageId: string): Observable<void> {
    return this.http.delete<void>(`/api/concepts/${id}/passages/${passageId}`);
  }
}
