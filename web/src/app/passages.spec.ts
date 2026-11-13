import {
  answerParts,
  citedNumbers,
  isPaged,
  locationLabel,
  snippetParts,
  sourceFileUrl,
  spokenText,
} from './passages';

describe('snippetParts', () => {
  it('marks the words Postgres highlighted and keeps everything else as text', () => {
    expect(snippetParts('in <mark>Godot</mark>, use <mark>signals</mark>.')).toEqual([
      { text: 'in ', marked: false },
      { text: 'Godot', marked: true },
      { text: ', use ', marked: false },
      { text: 'signals', marked: true },
      { text: '.', marked: false },
    ]);
  });

  it('leaves other markup in the document as plain text', () => {
    expect(snippetParts('a <b>bold</b> claim')).toEqual([
      { text: 'a <b>bold</b> claim', marked: false },
    ]);
  });
});

describe('answerParts', () => {
  it('splits paragraphs and turns markers into citations, including grouped ones', () => {
    expect(
      answerParts('Signals decouple nodes [1].\n\nThey follow the observer pattern [2, 4].'),
    ).toEqual([
      [{ text: 'Signals decouple nodes ' }, { cite: 1 }, { text: '.' }],
      [{ text: 'They follow the observer pattern ' }, { cite: 2 }, { cite: 4 }, { text: '.' }],
    ]);
  });

  it('returns nothing for an empty answer', () => {
    expect(answerParts('  \n\n ')).toEqual([]);
  });
});

describe('citedNumbers', () => {
  it('lists each cited passage once, in order', () => {
    expect(citedNumbers('B [3]. A [1, 3]. C [2]')).toEqual([1, 2, 3]);
  });
});

describe('spokenText', () => {
  it('drops citation markers and the space before the punctuation they leave behind', () => {
    expect(spokenText('Signals decouple nodes [1]. They follow a pattern [2, 4].')).toBe(
      'Signals decouple nodes. They follow a pattern.',
    );
  });
});

describe('locationLabel', () => {
  const place = {
    pageNumber: 112,
    pageLabel: '106',
    section: 'Book > Linear algebra > Eigenvalues',
  };

  it('shows the printed page label and the last two section levels for a PDF', () => {
    expect(locationLabel(place, true)).toBe('Linear algebra › Eigenvalues · p. 106');
  });

  it('falls back to the physical page when there is no label', () => {
    expect(locationLabel({ pageNumber: 3, pageLabel: null, section: null }, true)).toBe('p. 3');
  });

  it('omits the page for sources without pages', () => {
    expect(
      locationLabel({ pageNumber: 1, pageLabel: null, section: 'Docs > Signals' }, false),
    ).toBe('Docs › Signals');
  });

  it('knows which content types have pages', () => {
    expect(isPaged('application/pdf')).toBe(true);
    expect(isPaged('text/markdown')).toBe(false);
  });
});

describe('sourceFileUrl', () => {
  it('opens a PDF at the physical page', () => {
    expect(sourceFileUrl('s1', 'a b', { page: 112 })).toBe(
      '/api/sources/s1/file?token=a%20b#page=112',
    );
  });

  it('points at the first words of the passage when there is no page', () => {
    expect(sourceFileUrl('s1', 't', { text: 'use <mark>signals</mark>\n  here' })).toBe(
      '/api/sources/s1/file?token=t#:~:text=use%20signals%20here',
    );
  });

  it('opens the whole file otherwise', () => {
    expect(sourceFileUrl('s1', null)).toBe('/api/sources/s1/file?token=');
  });
});
