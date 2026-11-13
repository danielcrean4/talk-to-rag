export interface SnippetPart {
  text: string;
  marked: boolean;
}

/** Splits a search snippet on the <mark> tags Postgres puts around matched words; everything else stays text. */
export function snippetParts(snippet: string): SnippetPart[] {
  const parts: SnippetPart[] = [];
  let marked = false;
  for (const piece of snippet.split(/(<\/?mark>)/)) {
    if (piece === '<mark>') {
      marked = true;
    } else if (piece === '</mark>') {
      marked = false;
    } else if (piece) {
      parts.push({ text: piece, marked });
    }
  }
  return parts;
}

function plainSnippet(snippet: string): string {
  return snippet.replace(/<\/?mark>/g, '');
}

export type AnswerPart = { text: string } | { cite: number };

const CITATION = /\[(\d+(?:\s*,\s*\d+)*)\]/g;

/** Paragraphs of an answer, with each [n] or [n, m] marker turned into separate citation parts. */
export function answerParts(answer: string): AnswerPart[][] {
  return answer
    .trim()
    .split(/\n\s*\n/)
    .filter((paragraph) => paragraph.trim())
    .map((paragraph) => {
      const parts: AnswerPart[] = [];
      let last = 0;
      for (const match of paragraph.matchAll(CITATION)) {
        if (match.index > last) {
          parts.push({ text: paragraph.slice(last, match.index) });
        }
        for (const number of match[1].split(',')) {
          parts.push({ cite: Number(number) });
        }
        last = match.index + match[0].length;
      }
      if (last < paragraph.length) {
        parts.push({ text: paragraph.slice(last) });
      }
      return parts;
    });
}

export function citedNumbers(answer: string): number[] {
  const numbers = answerParts(answer)
    .flat()
    .flatMap((part) => ('cite' in part ? [part.cite] : []));
  return [...new Set(numbers)].sort((a, b) => a - b);
}

/** The answer as it should sound: without the citation markers. */
export function spokenText(answer: string): string {
  return answer
    .replace(CITATION, '')
    .replace(/[ \t]+([.,;:!?])/g, '$1')
    .replace(/[ \t]{2,}/g, ' ')
    .trim();
}

/** Only PDFs have pages; Markdown and text sources put every chunk on page 1. */
export function isPaged(contentType: string): boolean {
  return contentType === 'application/pdf';
}

export interface Location {
  pageNumber: number | null;
  pageLabel: string | null;
  section: string | null;
}

/** "Section › Subsection · p. xii" for paged sources, just the section otherwise. */
export function locationLabel(location: Location, paged: boolean): string {
  const section = location.section?.split(' > ').slice(-2).join(' › ') ?? '';
  if (!paged) {
    return section;
  }
  const page = `p. ${location.pageLabel ?? location.pageNumber}`;
  return section ? `${section} · ${page}` : page;
}

/** The file route takes the token as a query parameter because a new tab cannot send a header. */
export function sourceFileUrl(
  sourceId: string,
  token: string | null,
  target: { page?: number; text?: string } = {},
): string {
  const url = `/api/sources/${sourceId}/file?token=${encodeURIComponent(token ?? '')}`;
  if (target.page) {
    return `${url}#page=${target.page}`;
  }
  const fragment = textFragment(target.text ?? '');
  return fragment ? `${url}#:~:text=${encodeURIComponent(fragment)}` : url;
}

/** The first words of a passage, cut at a word boundary so the browser can find them. */
function textFragment(text: string, maxLength = 80): string {
  let fragment = '';
  for (const word of plainSnippet(text).split(/\s+/).filter(Boolean)) {
    const next = fragment ? `${fragment} ${word}` : word;
    if (next.length > maxLength) {
      break;
    }
    fragment = next;
  }
  return fragment;
}
