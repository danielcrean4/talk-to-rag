import { Component, inject, input, output, signal } from '@angular/core';
import { Activity } from '../activity';
import { Notebook } from '../api/notebooks-api';
import { SOURCE_DRAG_TYPE } from './source-list';

export interface SourceDrop {
  notebookId: string;
  sourceId: string;
}

/** Picks the notebook the list shows, and takes a source dropped on a notebook's name. */
@Component({
  selector: 'app-notebook-bar',
  templateUrl: './notebook-bar.html',
  styleUrls: ['./sources-shared.css', './notebook-bar.css'],
})
export class NotebookBar {
  protected readonly activity = inject(Activity);

  readonly notebooks = input.required<Notebook[]>();
  readonly selectedId = input<string | null>(null);
  /** True while a source is being dragged, so the notebooks show they take a drop. */
  readonly dragging = input(false);
  /** Whether the new notebook form is open. */
  readonly creating = input(false);

  readonly show = output<string | null>();
  readonly newNotebook = output<void>();
  readonly refresh = output<void>();
  readonly dropSource = output<SourceDrop>();

  protected readonly dropTarget = signal<string | null>(null);

  protected dragOver(event: DragEvent, notebook: Notebook): void {
    if (event.dataTransfer?.types.includes(SOURCE_DRAG_TYPE)) {
      event.preventDefault();
      event.dataTransfer.dropEffect = 'link';
      this.dropTarget.set(notebook.id);
    }
  }

  protected drop(event: DragEvent, notebook: Notebook): void {
    const sourceId = event.dataTransfer?.getData(SOURCE_DRAG_TYPE);
    this.dropTarget.set(null);
    if (sourceId) {
      event.preventDefault();
      this.dropSource.emit({ notebookId: notebook.id, sourceId });
    }
  }
}
