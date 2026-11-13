import { Message, MessageCitation } from '../api';
import { locationLabel } from '../passages';

export interface CitationView {
  number: number;
  /** "Section · p. xii" for paged sources, the section otherwise. */
  location: string;
  /** The physical page to open, or null when the source has no pages. */
  page: number | null;
  content: string;
}

export interface CitationGroup {
  sourceId: string;
  /** Null when the source is no longer in the list, for example after it was deleted. */
  title: string | null;
  citations: CitationView[];
}

/** A question with the passages found for it and, when one was asked for, the summary. */
export interface Turn {
  id: string;
  question: string | null;
  askedAtUtc: string;
  answer: string | null;
  groups: CitationGroup[];
}

/** Markdown and text sources report every chunk on page 1, so only a label or a later page shows a source has pages. */
export function isPagedCitation(
  citation: Pick<MessageCitation, 'pageNumber' | 'pageLabel'>,
): boolean {
  return citation.pageLabel !== null || citation.pageNumber > 1;
}

/** Groups citations by source, in the order their first passage ranked. */
function groupCitations(
  citations: MessageCitation[],
  titles: ReadonlyMap<string, string>,
): CitationGroup[] {
  const groups = new Map<string, CitationGroup>();
  for (const citation of [...citations].sort((a, b) => a.number - b.number)) {
    let group = groups.get(citation.sourceId);
    if (!group) {
      group = {
        sourceId: citation.sourceId,
        title: titles.get(citation.sourceId) ?? null,
        citations: [],
      };
      groups.set(citation.sourceId, group);
    }
    const paged = isPagedCitation(citation);
    group.citations.push({
      number: citation.number,
      location: locationLabel(citation, paged),
      page: paged ? citation.pageNumber : null,
      content: citation.content,
    });
  }
  return [...groups.values()];
}

/**
 * Pairs each question with the summary that follows it. A search stores the passages it found on the
 * question; the summary's [n] markers refer to those numbers.
 */
export function conversationTurns(
  messages: Message[],
  titles: ReadonlyMap<string, string>,
): Turn[] {
  const turns: {
    id: string;
    question: string | null;
    askedAtUtc: string;
    answer: string | null;
    citations: MessageCitation[];
  }[] = [];
  for (const message of messages) {
    const current = turns.at(-1);
    if (message.role === 'user' || !current || current.answer !== null) {
      turns.push({
        id: message.id,
        question: message.role === 'user' ? message.content : null,
        askedAtUtc: message.createdAtUtc,
        answer: message.role === 'user' ? null : message.content,
        citations: [...message.citations],
      });
      continue;
    }
    current.answer = message.content;
    const known = new Set(current.citations.map((citation) => citation.number));
    current.citations.push(...message.citations.filter((citation) => !known.has(citation.number)));
  }
  return turns.map(({ citations, ...turn }) => ({
    ...turn,
    groups: groupCitations(citations, titles),
  }));
}
