import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Mastery } from './concepts-api';

export interface Question {
  id: string;
  conceptId: string;
  prompt: string;
  answer: string;
  generatedByModel: boolean;
  sourceId: string | null;
  sourceTitle: string | null;
  pageNumber: number | null;
  pageLabel: string | null;
  section: string | null;
  attempts: number;
  lastScore: number | null;
  /** When the API's FSRS schedule says to ask the card again; null until it is first answered. */
  dueAtUtc: string | null;
}

export interface AttemptResult {
  conceptId: string;
  mastery: Mastery;
  /** When the answered question is due again. */
  dueAtUtc: string | null;
}

export interface DueConcept {
  conceptId: string;
  conceptName: string;
  /** Questions due now. */
  due: number;
  /** Questions never answered. */
  new: number;
}

/** Every question due now, earliest first, then a few never answered. The counts cover all of them. */
export interface DueCards {
  questions: Question[];
  dueCount: number;
  newCount: number;
  concepts: DueConcept[];
}

@Injectable({ providedIn: 'root' })
export class PracticeApi {
  private readonly http = inject(HttpClient);

  questions(conceptId: string): Observable<Question[]> {
    return this.http.get<Question[]>(`/api/concepts/${conceptId}/questions`);
  }

  /** The local model writes flashcards from the concept's linked passages; 400 when it has none. */
  generateQuestions(conceptId: string): Observable<Question[]> {
    return this.http.post<Question[]>(`/api/concepts/${conceptId}/questions/generate`, {});
  }

  due(): Observable<DueCards> {
    return this.http.get<DueCards>('/api/practice/due');
  }

  /** Records an answer with a score from 0 to 1 and returns the concept's updated mastery. */
  recordAttempt(questionId: string, platform: string, score: number): Observable<AttemptResult> {
    return this.http.post<AttemptResult>('/api/attempts', { questionId, platform, score });
  }
}
