import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { concatMap, from, toArray } from 'rxjs';
import { Activity } from '../activity';
import { Notebook, NotebooksApi } from '../api/notebooks-api';
import { Source, SourceDetailsChange, SourcesApi } from '../api/sources-api';
import { Tag, TagsApi } from '../api/tags-api';
import { SearchSession } from '../search/search-session';
import { knownTagIds, tagCount, toggleId, withAnyTag } from '../tags';
import { NotebookBar, SourceDrop } from './notebook-bar';
import { SourceCard } from './source-card';
import { anyWorking, inNotebook, safeHttpUrl } from './source-list';
import { TagEditor } from './tag-editor';

const POLL_MS = 3000;

@Component({
  selector: 'app-sources-page',
  imports: [FormsModule, NotebookBar, SourceCard, TagEditor],
  templateUrl: './sources-page.html',
  styleUrls: ['./sources-shared.css', './sources-page.css'],
  providers: [Activity],
})
export class SourcesPage implements OnDestroy {
  private readonly sourcesApi = inject(SourcesApi);
  private readonly notebooksApi = inject(NotebooksApi);
  private readonly tagsApi = inject(TagsApi);
  private readonly activity = inject(Activity);
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
  protected readonly busy = this.activity.busy;
  protected readonly error = this.activity.error;
  protected readonly dragging = signal(false);
  protected readonly overFiles = signal(false);
  protected readonly creating = signal(false);
  protected readonly confirmingDelete = signal<string | null>(null);
  protected readonly editingTags = signal(false);
  /** The source whose license and origin are being edited. */
  protected readonly editingDetails = signal<string | null>(null);
  protected newName = '';
  protected newDescription = '';

  protected readonly tagCount = tagCount;

  constructor() {
    this.loadSources();
    this.loadNotebooks();
    this.loadTags();
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
      concatMap((file) => this.sourcesApi.uploadSource(file)),
      toArray(),
    );
    this.activity.run(
      list.length === 1 ? `Uploading ${list[0].name}` : `Uploading ${list.length} files`,
      uploads,
      () => this.loadSources(),
    );
  }

  reprocess(source: Source): void {
    const label =
      source.status === 'Ready' ? `Processing ${source.title} again` : `Retrying ${source.title}`;
    this.activity.run(label, this.sourcesApi.retrySource(source.id), () => this.loadSources());
  }

  remove(source: Source): void {
    this.confirmingDelete.set(null);
    this.activity.run(`Deleting ${source.title}`, this.sourcesApi.deleteSource(source.id), () => {
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
    this.activity.run(
      `Adding ${source.title} to ${notebook.name}`,
      this.notebooksApi.addSourceToNotebook(notebook.id, source.id),
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
    this.activity.run(
      'Creating notebook',
      this.notebooksApi.createNotebook(name, this.newDescription.trim() || null),
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
    this.editingTags.update((open) => !open);
  }

  toggleTagFilter(tagId: string): void {
    this.tagFilter.update((ids) => toggleId(ids, tagId));
  }

  tagCreated(tag: Tag): void {
    this.tags.update((tags) => [...tags, tag]);
  }

  tagDeleted(tag: Tag): void {
    this.tags.update((tags) => tags.filter((item) => item.id !== tag.id));
    this.tagFilter.update((ids) => ids.filter((id) => id !== tag.id));
    this.setSourceTags((ids) => ids.filter((id) => id !== tag.id));
  }

  tagSource(source: Source, tagId: string): void {
    const tag = this.tags().find((item) => item.id === tagId);
    if (!tag) {
      return;
    }
    this.activity.run(
      `Adding tag ${tag.name} to ${source.title}`,
      this.tagsApi.tagSource(source.id, tag.id),
      () => this.setSourceTags((ids) => (ids.includes(tag.id) ? ids : [...ids, tag.id]), source.id),
    );
  }

  untagSource(source: Source, tag: Tag): void {
    this.activity.run(
      `Removing tag ${tag.name} from ${source.title}`,
      this.tagsApi.untagSource(source.id, tag.id),
      () => this.setSourceTags((ids) => ids.filter((id) => id !== tag.id), source.id),
    );
  }

  editDetails(source: Source, open: boolean): void {
    if (open) {
      this.error.set('');
    }
    this.editingDetails.set(open ? source.id : null);
  }

  saveDetails(source: Source, changes: Required<SourceDetailsChange>): void {
    if (changes.sourceUrl && !safeHttpUrl(changes.sourceUrl)) {
      this.error.set('The origin must be a full http:// or https:// address.');
      return;
    }
    this.activity.run(
      `Saving details of ${source.title}`,
      this.sourcesApi.updateSource(source.id, changes),
      (saved) => {
        this.editingDetails.set(null);
        this.sources.update((sources) =>
          sources.map((item) =>
            item.id === saved.id
              ? { ...item, license: saved.license, sourceUrl: saved.sourceUrl }
              : item,
          ),
        );
      },
    );
  }

  dropOnNotebook(drop: SourceDrop): void {
    this.endDrag();
    const source = this.sources().find((item) => item.id === drop.sourceId);
    if (source) {
      this.addToNotebook(source, drop.notebookId);
    }
  }

  dragOverFiles(event: DragEvent): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      this.overFiles.set(true);
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
    this.overFiles.set(false);
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  private loadSources(): void {
    this.sourcesApi.sources().subscribe({
      next: (sources) => {
        this.sources.set(sources);
        this.loaded.set(true);
        this.pollWhileWorking(sources);
      },
      error: (error) => this.activity.fail(error),
    });
  }

  private loadNotebooks(): void {
    this.notebooksApi.notebooks().subscribe({
      next: (notebooks) => this.notebooks.set(notebooks),
      error: (error) => this.activity.fail(error),
    });
  }

  private loadTags(): void {
    this.tagsApi.tags().subscribe({
      next: (tags) => this.tags.set(tags),
      error: (error) => this.activity.fail(error),
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
    this.notebooksApi.notebook(id).subscribe({
      next: (details) => {
        if (this.notebookId() === id) {
          this.notebookSourceIds.set(details.sourceIds);
        }
      },
      error: (error) => this.activity.fail(error),
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
}
