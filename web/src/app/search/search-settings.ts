import { Injectable, signal } from '@angular/core';
import { SearchMode, SearchRequest } from '../api';

export interface SearchSettings {
  mode: SearchMode;
  limit: number;
  expand: boolean;
  /** Passages further than this cosine distance are dropped; null leaves it to the server. */
  cutoff: number | null;
}

export const DEFAULT_SETTINGS: SearchSettings = {
  mode: 'hybrid',
  limit: 20,
  expand: false,
  cutoff: null,
};

/** Which sources a search covers: those in the notebook and with any of the tags, when given. */
export interface SearchScope {
  notebookId: string | null;
  tagIds: string[];
}

const STORAGE_KEY = 'talk-to-rag.search-settings';

export function parseSettings(stored: string | null): SearchSettings {
  try {
    return { ...DEFAULT_SETTINGS, ...JSON.parse(stored ?? '{}') };
  } catch {
    return DEFAULT_SETTINGS;
  }
}

export function searchRequest(
  query: string,
  settings: SearchSettings,
  scope: SearchScope & { conversationId: string | null },
): SearchRequest {
  const request: SearchRequest = {
    query,
    mode: settings.mode,
    expand: settings.expand,
    limit: settings.limit,
    notebookIds: scope.notebookId ? [scope.notebookId] : null,
    tagIds: scope.tagIds.length ? scope.tagIds : null,
    conversationId: scope.conversationId,
  };
  if (settings.cutoff) {
    request.maxCosineDistance = settings.cutoff;
  }
  return request;
}

export function describeSettings(settings: SearchSettings): string {
  const mode = settings.mode[0].toUpperCase() + settings.mode.slice(1);
  const parts = [
    mode,
    settings.expand ? 'expanded' : null,
    `top ${settings.limit}`,
    settings.cutoff ? `cutoff ${settings.cutoff}` : null,
  ];
  return parts.filter(Boolean).join(' · ');
}

@Injectable({ providedIn: 'root' })
export class SearchSettingsStore {
  readonly settings = signal(parseSettings(localStorage.getItem(STORAGE_KEY)));

  save(settings: SearchSettings): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
    this.settings.set(settings);
  }
}
