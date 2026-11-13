import { Message, MessageCitation } from '../api';
import { conversationTurns, isPagedCitation } from './conversation-turns';

const citation = (
  number: number,
  sourceId: string,
  changes: Partial<MessageCitation> = {},
): MessageCitation => ({
  number,
  sourceChunkId: null,
  sourceId,
  pageNumber: 1,
  pageLabel: null,
  section: null,
  content: `passage ${number}`,
  ...changes,
});

const message = (
  id: string,
  role: Message['role'],
  content: string,
  citations: MessageCitation[] = [],
): Message => ({
  id,
  role,
  content,
  createdAtUtc: '2026-01-01T00:00:00Z',
  citations,
});

const titles = new Map([
  ['book', 'A book'],
  ['notes', 'Some notes'],
]);

describe('isPagedCitation', () => {
  it('treats a printed label or a page after the first as paged', () => {
    expect(isPagedCitation({ pageNumber: 1, pageLabel: 'i' })).toBe(true);
    expect(isPagedCitation({ pageNumber: 12, pageLabel: null })).toBe(true);
  });

  it('treats page 1 without a label as unpaged, which is what Markdown and text report', () => {
    expect(isPagedCitation({ pageNumber: 1, pageLabel: null })).toBe(false);
  });
});

describe('conversationTurns', () => {
  it('pairs each question with the summary after it and groups passages by source in rank order', () => {
    const turns = conversationTurns(
      [
        message('q1', 'user', 'what is a signal', [
          citation(2, 'notes', { section: 'Guide > Signals' }),
          citation(1, 'book', { pageNumber: 40, pageLabel: '22', section: 'Events' }),
          citation(3, 'book', { pageNumber: 41 }),
        ]),
        message('a1', 'assistant', 'A signal is an event [1].'),
        message('q2', 'user', 'and a slot', []),
      ],
      titles,
    );

    expect(turns.map((turn) => [turn.id, turn.question, turn.answer])).toEqual([
      ['q1', 'what is a signal', 'A signal is an event [1].'],
      ['q2', 'and a slot', null],
    ]);
    expect(turns[0].groups).toEqual([
      {
        sourceId: 'book',
        title: 'A book',
        citations: [
          { number: 1, location: 'Events · p. 22', page: 40, content: 'passage 1' },
          { number: 3, location: 'p. 41', page: 41, content: 'passage 3' },
        ],
      },
      {
        sourceId: 'notes',
        title: 'Some notes',
        citations: [{ number: 2, location: 'Guide › Signals', page: null, content: 'passage 2' }],
      },
    ]);
    expect(turns[1].groups).toEqual([]);
  });

  it('keeps citations stored on the summary that the question does not already have', () => {
    const [turn] = conversationTurns(
      [
        message('q', 'user', 'q', [citation(1, 'book', { pageNumber: 3 })]),
        message('a', 'assistant', 'a', [
          citation(1, 'book', { pageNumber: 3 }),
          citation(4, 'gone', { pageNumber: 9 }),
        ]),
      ],
      titles,
    );
    expect(
      turn.groups.map((group) => [
        group.sourceId,
        group.title,
        group.citations.map((item) => item.number),
      ]),
    ).toEqual([
      ['book', 'A book', [1]],
      ['gone', null, [4]],
    ]);
  });

  it('gives a summary without a question before it a turn of its own', () => {
    const turns = conversationTurns(
      [message('a1', 'assistant', 'first'), message('a2', 'assistant', 'second')],
      titles,
    );
    expect(turns.map((turn) => [turn.question, turn.answer])).toEqual([
      [null, 'first'],
      [null, 'second'],
    ]);
  });
});
