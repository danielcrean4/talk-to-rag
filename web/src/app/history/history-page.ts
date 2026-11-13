import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, ConversationDetails, ConversationSummary, Source } from '../api';
import { AnswerText } from '../answer-text';
import { Auth, errorMessage } from '../auth';
import { sourceFileUrl } from '../passages';
import { CitationGroup, CitationView, conversationTurns } from './conversation-turns';

/** Past searches on the left; the one picked by the route on the right. */
@Component({
  selector: 'app-history-page',
  imports: [AnswerText, DatePipe, RouterLink],
  templateUrl: './history-page.html',
  styleUrl: './history-page.css',
})
export class HistoryPage {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);

  /** Bound from the `history/:id` route. */
  readonly id = input<string>();

  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly listLoaded = signal(false);
  protected readonly sources = signal<Source[]>([]);
  protected readonly conversation = signal<ConversationDetails | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly flashed = signal<string | null>(null);
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  private readonly titles = computed(
    () => new Map(this.sources().map((source) => [source.id, source.title])),
  );
  protected readonly turns = computed(() => {
    const conversation = this.conversation();
    return conversation ? conversationTurns(conversation.messages, this.titles()) : [];
  });

  constructor() {
    this.refresh();
    this.api.sources().subscribe({ next: (sources) => this.sources.set(sources) });
    effect(() => this.open(this.id()));
  }

  refresh(): void {
    this.api.conversations().subscribe({
      next: (conversations) => {
        this.conversations.set(conversations);
        this.listLoaded.set(true);
      },
      error: (error) => this.error.set(errorMessage(error)),
    });
  }

  fileUrl(group: CitationGroup, citation?: CitationView): string {
    if (!citation) {
      return sourceFileUrl(group.sourceId, this.auth.token());
    }
    return sourceFileUrl(
      group.sourceId,
      this.auth.token(),
      citation.page ? { page: citation.page } : { text: citation.content },
    );
  }

  citationKey(turnId: string, number: number): string {
    return `${turnId}-${number}`;
  }

  toggle(key: string): void {
    this.expanded.update((keys) => {
      const next = new Set(keys);
      if (!next.delete(key)) {
        next.add(key);
      }
      return next;
    });
  }

  showCitation(turnId: string, number: number): void {
    const key = this.citationKey(turnId, number);
    document
      .getElementById(`citation-${key}`)
      ?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    this.flashed.set(key);
    setTimeout(() => this.flashed.update((current) => (current === key ? null : current)), 2000);
  }

  private open(id: string | undefined): void {
    this.conversation.set(null);
    this.expanded.set(new Set());
    if (!id) {
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.api.conversation(id).subscribe({
      next: (conversation) => {
        if (this.id() === id) {
          this.conversation.set(conversation);
        }
        this.loading.set(false);
      },
      error: (error) => {
        this.loading.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }
}
