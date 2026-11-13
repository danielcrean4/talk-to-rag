import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Notebook, Tag } from '../api';
import { AnswerText } from '../answer-text';
import { ReadAloud } from '../read-aloud';
import { knownTagIds, toggleId } from '../tags';
import { PushToTalk } from './push-to-talk';
import { ResultGroups } from './result-groups';
import { SearchSession } from './search-session';
import { SearchSettingsStore, describeSettings } from './search-settings';
import { SettingsPanel } from './settings-panel';

@Component({
  selector: 'app-search-page',
  imports: [FormsModule, AnswerText, PushToTalk, ReadAloud, ResultGroups, SettingsPanel],
  templateUrl: './search-page.html',
  styleUrl: './search-page.css',
})
export class SearchPage {
  private readonly api = inject(Api);
  private readonly settings = inject(SearchSettingsStore);
  protected readonly session = inject(SearchSession);

  protected query = this.session.results()?.query ?? '';
  protected notebookId = '';
  protected readonly notebooks = signal<Notebook[]>([]);
  protected readonly tags = signal<Tag[]>([]);
  protected readonly tagIds = signal<string[]>([]);
  protected readonly voiceError = signal('');
  protected readonly flashed = signal<number | null>(null);
  protected readonly settingsOpen = signal(false);
  protected readonly settingsLine = computed(() => describeSettings(this.settings.settings()));
  protected readonly passageCount = computed(
    () =>
      this.session.results()?.groups.reduce((total, group) => total + group.passages.length, 0) ??
      0,
  );

  constructor() {
    this.api.notebooks().subscribe({ next: (notebooks) => this.notebooks.set(notebooks) });
    this.api.tags().subscribe({ next: (tags) => this.tags.set(tags) });
  }

  toggleTag(tagId: string): void {
    this.tagIds.update((ids) => toggleId(ids, tagId));
  }

  search(): void {
    const query = this.query.trim();
    if (query && !this.session.searching()) {
      this.voiceError.set('');
      this.session.search(query, {
        notebookId: this.notebookId || null,
        tagIds: knownTagIds(this.tagIds(), this.tags()),
      });
    }
  }

  heard(text: string): void {
    this.query = text;
    this.search();
  }

  startOver(): void {
    this.query = '';
    this.session.startOver();
  }

  showPassage(number: number): void {
    document
      .getElementById(`passage-${number}`)
      ?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    this.flashed.set(number);
    setTimeout(() => this.flashed.update((current) => (current === number ? null : current)), 2000);
  }
}
