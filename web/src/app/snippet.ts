import { Component, computed, input } from '@angular/core';
import { snippetParts } from './passages';

/** A search snippet with the matched words highlighted, rendered as text so document content is never parsed as HTML. */
@Component({
  selector: 'app-snippet',
  // The text is shown with its line breaks, so the template adds no whitespace of its own.
  // prettier-ignore
  template: `@for (part of parts(); track $index) {@if (part.marked) {<mark>{{ part.text }}</mark>} @else {<span>{{ part.text }}</span>}}`,
  styles: `
    :host {
      display: block;
      white-space: pre-line;
      overflow-wrap: anywhere;
    }
    mark {
      background: var(--mark);
      color: inherit;
      border-radius: 3px;
      padding: 0 1px;
    }
  `,
})
export class Snippet {
  readonly snippet = input.required<string>();

  protected readonly parts = computed(() => snippetParts(this.snippet()));
}
