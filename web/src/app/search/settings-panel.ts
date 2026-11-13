import { Component, inject, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SearchMode } from '../api';
import { SearchSettingsStore } from './search-settings';

/** Retrieval settings, kept in this browser and sent with every search. */
@Component({
  selector: 'app-settings-panel',
  imports: [FormsModule],
  template: `
    <form class="settings" (ngSubmit)="save()">
      <label>
        Retrieval mode
        <select name="mode" [(ngModel)]="mode">
          <option value="hybrid">Hybrid: keywords and meaning</option>
          <option value="semantic">Semantic: meaning only</option>
          <option value="keyword">Keyword: full-text only</option>
        </select>
      </label>
      <label>
        Passages
        <select name="limit" [(ngModel)]="limit">
          @for (option of limits; track option) {
            <option [ngValue]="option">{{ option }}</option>
          }
        </select>
      </label>
      <label class="check">
        <input type="checkbox" name="expand" [(ngModel)]="expand" />
        Expand the question into 2–3 searches with the local chat model
      </label>
      <label>
        Distance cutoff
        <input
          type="number"
          name="cutoff"
          min="0.05"
          max="1.5"
          step="0.05"
          placeholder="Server default"
          [(ngModel)]="cutoff"
        />
        <small
          >Passages further from the question than this are dropped. Leave blank for the server
          default.</small
        >
      </label>
      <div class="actions">
        <button class="primary" type="submit">Save settings</button>
      </div>
    </form>
  `,
  styles: `
    .settings {
      display: grid;
      gap: 0.75rem;
      padding: 0.75rem 0 0.25rem;
      max-width: 30rem;
    }
    label {
      display: grid;
      gap: 0.3rem;
      font-size: 0.875rem;
      color: var(--muted);
    }
    label.check {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      color: var(--text);
    }
    small {
      font-size: 0.75rem;
    }
    .actions {
      display: flex;
      justify-content: flex-end;
    }
  `,
})
export class SettingsPanel {
  private readonly store = inject(SearchSettingsStore);

  readonly saved = output();

  protected readonly limits = [5, 10, 20, 50];
  protected mode: SearchMode = this.store.settings().mode;
  protected limit = this.store.settings().limit;
  protected expand = this.store.settings().expand;
  protected cutoff: number | null = this.store.settings().cutoff;

  save(): void {
    this.store.save({
      mode: this.mode,
      limit: this.limit,
      expand: this.expand,
      cutoff: this.cutoff || null,
    });
    this.saved.emit();
  }
}
