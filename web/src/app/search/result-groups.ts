import { Component, inject, input } from '@angular/core';
import { PassageGroup, PassageHit } from '../api';
import { Auth } from '../auth';
import { isPaged, locationLabel, sourceFileUrl } from '../passages';
import { Snippet } from '../snippet';

/** Search hits grouped by source; each passage can be opened in the source at its page. */
@Component({
  selector: 'app-result-groups',
  imports: [Snippet],
  template: `
    @for (group of groups(); track group.source.id) {
      <section class="group" [attr.aria-label]="group.source.title">
        <header>
          <a class="title" [href]="fileUrl(group)" target="_blank" rel="noopener">{{
            group.source.title
          }}</a>
          <span class="quiet"
            >{{ group.passages.length }}
            {{ group.passages.length === 1 ? 'passage' : 'passages' }}</span
          >
        </header>
        <ol>
          @for (passage of group.passages; track passage.number) {
            <li
              class="passage"
              [id]="'passage-' + passage.number"
              [class.flash]="passage.number === flashed()"
            >
              <div class="where">
                <span class="number">[{{ passage.number }}]</span>
                <span class="place">{{ where(group, passage) }}</span>
                <a [href]="fileUrl(group, passage)" target="_blank" rel="noopener">
                  {{
                    paged(group)
                      ? 'Open at p. ' + (passage.pageLabel ?? passage.pageNumber)
                      : 'Open'
                  }}
                </a>
                <span class="matched">
                  @for (how of passage.matchedBy; track how) {
                    <span class="badge">{{ how }}</span>
                  }
                  <span class="quiet" title="Reciprocal rank fusion score">{{
                    passage.score.toFixed(4)
                  }}</span>
                </span>
              </div>
              <app-snippet [snippet]="passage.snippet" />
            </li>
          }
        </ol>
      </section>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: 1rem;
    }
    .group {
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 0.75rem 1rem;
    }
    header {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: 1rem;
      margin-bottom: 0.5rem;
    }
    .title {
      font-weight: 700;
      color: var(--text);
      overflow-wrap: anywhere;
    }
    ol {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    .where {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: 0.25rem 0.6rem;
      font-size: 0.8125rem;
      margin-bottom: 0.35rem;
    }
    .number {
      font-weight: 700;
      color: var(--accent);
    }
    .place {
      color: var(--muted);
      overflow-wrap: anywhere;
    }
    .matched {
      margin-left: auto;
      display: flex;
      gap: 0.35rem;
      align-items: baseline;
    }
    app-snippet {
      font-size: 0.875rem;
    }
  `,
})
export class ResultGroups {
  private readonly auth = inject(Auth);

  readonly groups = input.required<PassageGroup[]>();
  readonly flashed = input<number | null>(null);

  paged(group: PassageGroup): boolean {
    return isPaged(group.source.contentType);
  }

  where(group: PassageGroup, passage: PassageHit): string {
    return locationLabel(passage, this.paged(group));
  }

  fileUrl(group: PassageGroup, passage?: PassageHit): string {
    if (!passage) {
      return sourceFileUrl(group.source.id, this.auth.token());
    }
    return sourceFileUrl(
      group.source.id,
      this.auth.token(),
      this.paged(group) ? { page: passage.pageNumber } : { text: passage.snippet },
    );
  }
}
