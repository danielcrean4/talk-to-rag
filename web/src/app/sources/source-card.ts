import { DatePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Notebook } from '../api/notebooks-api';
import { Source, SourceDetailsChange } from '../api/sources-api';
import { Tag } from '../api/tags-api';
import { SourceFileLink } from '../source-file-link';
import { tagsOff, tagsOn } from '../tags';
import {
  SOURCE_DRAG_TYPE,
  failureNotice,
  formatBytes,
  originHost,
  reprocessLabel,
  safeHttpUrl,
} from './source-list';

/** One source in the list, dragged onto a notebook to add it there. */
@Component({
  selector: 'li[appSourceCard]',
  imports: [DatePipe, FormsModule, SourceFileLink],
  templateUrl: './source-card.html',
  styleUrls: ['./sources-shared.css', './source-card.css'],
  host: {
    class: 'source',
    '[class.match]': 'matched()',
    '[class.failed]': "source().status === 'Failed'",
    '[attr.draggable]': "editing() ? 'false' : 'true'",
    '(dragstart)': 'dragStart($event)',
    '(dragend)': 'dragEnded.emit()',
  },
})
export class SourceCard {
  readonly source = input.required<Source>({ alias: 'appSourceCard' });
  readonly tags = input.required<Tag[]>();
  readonly notebooks = input.required<Notebook[]>();
  readonly busy = input(false);
  /** Whether the last search found passages in it. */
  readonly matched = input(false);
  /** The page lets one source at a time edit its details or ask to confirm a delete. */
  readonly editing = input(false);
  readonly confirmingDelete = input(false);

  readonly editingChange = output<boolean>();
  readonly confirmingDeleteChange = output<boolean>();
  readonly saveDetails = output<Required<SourceDetailsChange>>();
  readonly reprocess = output<void>();
  readonly remove = output<void>();
  readonly addToNotebook = output<string>();
  readonly tag = output<string>();
  readonly untag = output<Tag>();
  readonly dragStarted = output<void>();
  readonly dragEnded = output<void>();

  /** Drafts live in the card, so polling the list cannot wipe them. */
  protected draftLicense = '';
  protected draftSourceUrl = '';

  protected readonly formatBytes = formatBytes;
  protected readonly reprocessLabel = reprocessLabel;
  protected readonly failureNotice = failureNotice;
  protected readonly tagsOn = tagsOn;
  protected readonly tagsOff = tagsOff;
  protected readonly safeHttpUrl = safeHttpUrl;
  protected readonly originHost = originHost;

  protected editDetails(): void {
    this.draftLicense = this.source().license ?? '';
    this.draftSourceUrl = this.source().sourceUrl ?? '';
    this.editingChange.emit(true);
  }

  protected save(): void {
    this.saveDetails.emit({
      license: this.draftLicense.trim(),
      sourceUrl: this.draftSourceUrl.trim(),
    });
  }

  protected dragStart(event: DragEvent): void {
    event.dataTransfer?.setData(SOURCE_DRAG_TYPE, this.source().id);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'link';
    }
    this.dragStarted.emit();
  }
}
