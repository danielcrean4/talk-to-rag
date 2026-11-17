import { Directive, computed, inject, input } from '@angular/core';
import { SourcesApi } from './api/sources-api';
import { FileTarget, sourceFileUrl } from './passages';

/**
 * A link that opens a source's file in a new tab. A tab cannot send the sign-in header, so a click
 * asks the API for a short-lived link to this one file and sends the tab there.
 */
@Directive({
  selector: 'a[appSourceFile]',
  host: {
    '[href]': 'href()',
    target: '_blank',
    rel: 'noopener',
    '(click)': 'open($event)',
  },
})
export class SourceFileLink {
  private readonly api = inject(SourcesApi);

  /** The source's id. */
  readonly appSourceFile = input.required<string>();
  /** Where in the file to open it. */
  readonly at = input<FileTarget>({});

  /** Shown on hover; it does not open the file without the token a click fetches. */
  protected readonly href = computed(() =>
    sourceFileUrl(`/api/sources/${this.appSourceFile()}/file`, this.at()),
  );

  protected open(event: MouseEvent): void {
    event.preventDefault();
    // Opened during the click, which a popup blocker allows, and sent to the file once the link arrives.
    const tab = window.open('', '_blank');
    if (!tab) {
      return;
    }
    tab.opener = null;
    const at = this.at();
    this.api.sourceFileLink(this.appSourceFile()).subscribe({
      next: (link) => {
        tab.location.href = new URL(sourceFileUrl(link.url, at), document.baseURI).href;
      },
      error: () => tab.close(),
    });
  }
}
