import { HttpEventType } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { Api, SearchResponse } from '../api';
import { errorMessage } from '../auth';
import { SearchScope, SearchSettingsStore, searchRequest } from './search-settings';
import { SummaryState, readSummary } from './summary-stream';

/**
 * The current search, kept while moving between screens. Searches continue one conversation,
 * so a follow-up question is expanded with the earlier ones in mind, until the user starts over.
 */
@Injectable({ providedIn: 'root' })
export class SearchSession {
  private readonly api = inject(Api);
  private readonly settings = inject(SearchSettingsStore);
  private summaryRun?: Subscription;

  readonly results = signal<SearchResponse | null>(null);
  readonly conversationId = signal<string | null>(null);
  readonly searching = signal(false);
  readonly summary = signal<SummaryState | null>(null);
  readonly summarizing = signal(false);
  readonly error = signal('');

  /** The sources the last search found passages in, highlighted on the sources screen. */
  readonly matchedSourceIds = computed(
    () => new Set(this.results()?.groups.map((group) => group.source.id) ?? []),
  );

  search(query: string, scope: SearchScope): void {
    this.stopSummary();
    this.summary.set(null);
    this.searching.set(true);
    this.error.set('');
    const request = searchRequest(query, this.settings.settings(), {
      ...scope,
      conversationId: this.conversationId(),
    });
    this.api.search(request).subscribe({
      next: (response) => {
        this.searching.set(false);
        this.results.set(response);
        this.conversationId.set(response.conversationId);
        // The passages show at once; the summary streams in after them.
        if (response.groups.some((group) => group.passages.length > 0)) {
          this.summarize();
        }
      },
      error: (error) => {
        this.searching.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }

  summarize(): void {
    const conversationId = this.conversationId();
    if (!conversationId) {
      return;
    }
    this.stopSummary();
    this.summary.set(readSummary(''));
    this.summarizing.set(true);
    this.summaryRun = this.api.summaryStream(conversationId).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.DownloadProgress && event.partialText !== undefined) {
          this.summary.set(readSummary(event.partialText));
        } else if (event.type === HttpEventType.Response) {
          this.summary.set(readSummary(event.body ?? ''));
        }
      },
      error: (error) => {
        this.summarizing.set(false);
        this.summary.update((summary) => summary && { ...summary, error: errorMessage(error) });
      },
      complete: () => {
        this.summarizing.set(false);
        this.summary.update((summary) =>
          summary && !summary.done && !summary.error
            ? { ...summary, error: 'The summary stopped before it was finished.' }
            : summary,
        );
      },
    });
  }

  startOver(): void {
    this.stopSummary();
    this.results.set(null);
    this.summary.set(null);
    this.conversationId.set(null);
    this.error.set('');
  }

  private stopSummary(): void {
    this.summaryRun?.unsubscribe();
    this.summarizing.set(false);
  }
}
