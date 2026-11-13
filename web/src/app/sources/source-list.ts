import { Source } from '../api';

export function formatBytes(bytes: number): string {
  if (bytes <= 0) {
    return '0 B';
  }
  const units = ['B', 'KB', 'MB', 'GB'];
  const power = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${parseFloat((bytes / 1024 ** power).toFixed(1))} ${units[power]}`;
}

/** True while ingestion is still running for any source, which is when the list needs polling. */
export function anyWorking(sources: Source[]): boolean {
  return sources.some((source) => source.status === 'Pending' || source.status === 'Processing');
}

/** The sources in a notebook, or all of them when no notebook is picked. */
export function inNotebook(sources: Source[], notebookSourceIds: string[] | null): Source[] {
  if (notebookSourceIds === null) {
    return sources;
  }
  const ids = new Set(notebookSourceIds);
  return sources.filter((source) => ids.has(source.id));
}

/** The button that sends a source through ingestion again: Retry after a failure, Process again once it is Ready. */
export function reprocessLabel(source: Source): string | null {
  switch (source.status) {
    case 'Failed':
      return 'Retry';
    case 'Ready':
      return 'Process again';
    default:
      return null;
  }
}

/** Why the last ingestion failed. A Ready source can carry one when processing it again failed and the old passages were kept. */
export function failureNotice(source: Source): string | null {
  if (source.status === 'Failed') {
    return source.failureReason || 'Ingestion failed.';
  }
  return source.status === 'Ready' ? source.failureReason : null;
}
