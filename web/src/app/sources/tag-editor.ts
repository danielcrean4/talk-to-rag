import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Activity } from '../activity';
import { Tag, TagsApi } from '../api/tags-api';

/** Adds and deletes tags; the page keeps the list and the sources that carry them up to date. */
@Component({
  selector: 'app-tag-editor',
  imports: [FormsModule],
  templateUrl: './tag-editor.html',
  styleUrls: ['./sources-shared.css', './tag-editor.css'],
})
export class TagEditor {
  private readonly api = inject(TagsApi);
  protected readonly activity = inject(Activity);

  readonly tags = input.required<Tag[]>();
  readonly created = output<Tag>();
  readonly deleted = output<Tag>();
  readonly done = output<void>();

  protected newTagName = '';
  protected readonly confirmingDelete = signal<string | null>(null);

  protected create(): void {
    const name = this.newTagName.trim();
    if (!name) {
      return;
    }
    this.activity.run('Creating tag', this.api.createTag(name), (tag) => {
      this.newTagName = '';
      this.created.emit(tag);
    });
  }

  protected delete(tag: Tag): void {
    this.confirmingDelete.set(null);
    this.activity.run(`Deleting tag ${tag.name}`, this.api.deleteTag(tag.id), () =>
      this.deleted.emit(tag),
    );
  }
}
