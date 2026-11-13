import { DatePipe } from '@angular/common';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, concatMap, from, toArray } from 'rxjs';
import { Api, Notebook, Source, Tag } from '../api';
import { Auth, errorMessage } from '../auth';
import { sourceFileUrl } from '../passages';
import { SearchSession } from '../search/search-session';
import { knownTagIds, tagCount, tagsOff, tagsOn, toggleId, withAnyTag } from '../tags';
import { anyWorking, failureNotice, formatBytes, inNotebook, reprocessLabel } from './source-list';

const SOURCE_DRAG_TYPE = 'application/x-talk-to-rag-source';
const POLL_MS = 3000;

@Component({
  selector: 'app-sources-page',
  imports: [DatePipe, FormsModule],
  templateUrl: './sources-page.html',
  styleUrl: './sources-page.css',
})
export class SourcesPage implements OnDestroy {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  private pollTimer: ReturnType<typeof setInterval> | null = null;

  protected readonly matched = inject(SearchSession).matchedSourceIds;
  protected readonly sources = signal<Source[]>([]);
  protected readonly loaded = signal(false);
  protected readonly notebooks = signal<Notebook[]>([]);
  protected readonly notebookId = signal<string | null>(null);
  protected readonly notebookSourceIds = signal<string[] | null>(null);
  protected readonly tags = signal<Tag[]>([]);
  protected readonly tagFilter = signal<string[]>([]);
  protected readonly visible = computed(() =>
    withAnyTag(
      inNotebook(this.sources(), this.notebookId() ? (this.notebookSourceIds() ?? []) : null),
      knownTagIds(this.tagFilter(), this.tags()),
    ),
  );
  protected readonly busy = signal('');
  protected readonly error = signal('');
  protected readonly dragging = signal(false);
  protected readonly dropTarget = signal<string | 'files' | null>(null);
  protected readonly creating = signal(false);
  protected readonly confirmingDelete = signal<string | null>(null);
  protected readonly editingTags = signal(false);
  protected readonly confirmingTagDelete = signal<string | null>(null);
  protected newName = '';
  protected newDescription = '';
  protected newTagName = '';

  protected readonly formatBytes = formatBytes;
  protected readonly reprocessLabel = reprocessLabel;
  protected readonly failureNotice = failureNotice;
  protected readonly tagsOn = tagsOn;
  protected readonly tagsOff = tagsOff;
  protected readonly tagCount = tagCount;

  constructor() {
    this.loadSources();
    this.loadNotebooks();
    this.loadTags();
  }

  fileUrl(source: Source): string {
    return sourceFileUrl(source.id, this.auth.token());
  }

  /** Picks up sources added outside this page, such as by `make sample`. */
  refresh(): void {
    this.error.set('');
    this.loadSources();
    this.loadNotebooks();
    const notebookId = this.notebookId();
    if (notebookId) {
      this.refreshNotebook(notebookId);
    }
  }

  showNotebook(id: string | null): void {
    this.notebookId.set(id);
    this.notebookSourceIds.set(null);
    if (id) {
      this.refreshNotebook(id);
    }
  }

  upload(files: FileList | File[] | null): void {
    const list = Array.from(files ?? []);
    if (list.length === 0) {
      return;
    }
    const uploads = from(list).pipe(
      concatMap((file) => this.api.uploadSource(file)),
      toArray(),
    );
    this.run(
      list.length === 1 ? `Uploading ${list[0].name}` : `Uploading ${list.length} files`,
      uploads,
      () => this.loadSources(),
    );
  }

  reprocess(source: Source): void {
    const label =
      source.status === 'Ready' ? `Processing ${source.title} again` : `Retrying ${source.title}`;
    this.run(label, this.api.retrySource(source.id), () => this.loadSources());
  }

  remove(source: Source): void {
    this.confirmingDelete.set(null);
    this.run(`Deleting ${source.title}`, this.api.deleteSource(source.id), () => {
      this.sources.update((sources) => sources.filter((item) => item.id !== source.id));
      this.loadSources();
      this.loadNotebooks();
      const notebookId = this.notebookId();
      if (notebookId) {
        this.refreshNotebook(notebookId);
      }
    });
  }

  addToNotebook(source: Source, notebookId: string): void {
    const notebook = this.notebooks().find((item) => item.id === notebookId);
    if (!notebook) {
      return;
    }
    this.run(
      `Adding ${source.title} to ${notebook.name}`,
      this.api.addSourceToNotebook(notebook.id, source.id),
      () => {
        this.loadNotebooks();
        if (this.notebookId() === notebook.id) {
          this.refreshNotebook(notebook.id);
        }
      },
    );
  }

  createNotebook(): void {
    const name = this.newName.trim();
    if (!name) {
      return;
    }
    this.run(
      'Creating notebook',
      this.api.createNotebook(name, this.newDescription.trim() || null),
      (notebook) => {
        this.newName = '';
        this.newDescription = '';
        this.creating.set(false);
        this.notebooks.update((notebooks) => [...notebooks, notebook]);
      },
    );
  }

  newNotebook(): void {
    this.editingTags.set(false);
    this.creating.set(true);
  }

  toggleEditingTags(): void {
    this.creating.set(false);
    this.confirmingTagDelete.set(null);
    this.editingTags.update((open) => !open);
  }

  toggleTagFilter(tagId: string): void {
    this.tagFilter.update((ids) => toggleId(ids, tagId));
  }

  createTag(): void {
    const name = this.newTagName.trim();
    if (!name) {
      return;
    }
    this.run('Creating tag', this.api.createTag(name), (tag) => {
      this.newTagName = '';
      this.tags.update((tags) => [...tags, tag]);
    });
  }

  deleteTag(tag: Tag): void {
    this.confirmingTagDelete.set(null);
    this.run(`Deleting tag ${tag.name}`, this.api.deleteTag(tag.id), () => {
      this.tags.update((tags) => tags.filter((item) => item.id !== tag.id));
      this.tagFilter.update((ids) => ids.filter((id) => id !== tag.id));
      this.setSourceTags((ids) => ids.filter((id) => id !== tag.id));
    });
  }

  tagSource(source: Source, tagId: string): void {
    const tag = this.tags().find((item) => item.id === tagId);
    if (!tag) {
      return;
    }
    this.run(
      `Adding tag ${tag.name} to ${source.title}`,
      this.api.tagSource(source.id, tag.id),
      () => this.setSourceTags((ids) => (ids.includes(tag.id) ? ids : [...ids, tag.id]), source.id),
    );
  }

  untagSource(source: Source, tag: Tag): void {
    this.run(
      `Removing tag ${tag.name} from ${source.title}`,
      this.api.untagSource(source.id, tag.id),
      () => this.setSourceTags((ids) => ids.filter((id) => id !== tag.id), source.id),
    );
  }

  dragSource(event: DragEvent, source: Source): void {
    event.dataTransfer?.setData(SOURCE_DRAG_TYPE, source.id);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'link';
    }
    this.dragging.set(true);
  }

  dragOverNotebook(event: DragEvent, notebook: Notebook): void {
    if (event.dataTransfer?.types.includes(SOURCE_DRAG_TYPE)) {
      event.preventDefault();
      event.dataTransfer.dropEffect = 'link';
      this.dropTarget.set(notebook.id);
    }
  }

  dropOnNotebook(event: DragEvent, notebook: Notebook): void {
    const sourceId = event.dataTransfer?.getData(SOURCE_DRAG_TYPE);
    this.endDrag();
    const source = this.sources().find((item) => item.id === sourceId);
    if (source) {
      event.preventDefault();
      this.addToNotebook(source, notebook.id);
    }
  }

  dragOverFiles(event: DragEvent): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      this.dropTarget.set('files');
    }
  }

  dropFiles(event: DragEvent): void {
    this.endDrag();
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      this.upload(event.dataTransfer.files);
    }
  }

  endDrag(): void {
    this.dragging.set(false);
    this.dropTarget.set(null);
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  private loadSources(): void {
    this.api.sources().subscribe({
      next: (sources) => {
        this.sources.set(sources);
        this.loaded.set(true);
        this.pollWhileWorking(sources);
      },
      error: (error) => this.error.set(errorMessage(error)),
    });
  }

  private loadNotebooks(): void {
    this.api.notebooks().subscribe({
      next: (notebooks) => this.notebooks.set(notebooks),
      error: (error) => this.error.set(errorMessage(error)),
    });
  }

  private loadTags(): void {
    this.api.tags().subscribe({
      next: (tags) => this.tags.set(tags),
      error: (error) => this.error.set(errorMessage(error)),
    });
  }

  /** Changes the tag ids of one source, or of every source when no id is given. */
  private setSourceTags(change: (tagIds: string[]) => string[], sourceId?: string): void {
    this.sources.update((sources) =>
      sources.map((source) =>
        sourceId === undefined || source.id === sourceId
          ? { ...source, tagIds: change(source.tagIds) }
          : source,
      ),
    );
  }

  private refreshNotebook(id: string): void {
    this.api.notebook(id).subscribe({
      next: (details) => {
        if (this.notebookId() === id) {
          this.notebookSourceIds.set(details.sourceIds);
        }
      },
      error: (error) => this.error.set(errorMessage(error)),
    });
  }

  private pollWhileWorking(sources: Source[]): void {
    if (anyWorking(sources) && !this.pollTimer) {
      this.pollTimer = setInterval(() => this.loadSources(), POLL_MS);
    } else if (!anyWorking(sources) && this.pollTimer) {
      this.stopPolling();
      this.loadNotebooks();
    }
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
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
