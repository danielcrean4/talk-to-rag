import {
  DEFAULT_SETTINGS,
  describeSettings,
  parseSettings,
  searchRequest,
} from './search-settings';

describe('searchRequest', () => {
  it('sends the mode, expansion and limit, and leaves the cutoff to the server when unset', () => {
    expect(
      searchRequest('signals', DEFAULT_SETTINGS, {
        notebookId: null,
        tagIds: [],
        conversationId: null,
      }),
    ).toEqual({
      query: 'signals',
      mode: 'hybrid',
      expand: false,
      limit: 20,
      notebookIds: null,
      tagIds: null,
      conversationId: null,
    });
  });

  it('scopes to a notebook and tags, continues a conversation and sends a cutoff that is set', () => {
    const request = searchRequest(
      'signals',
      { ...DEFAULT_SETTINGS, mode: 'semantic', expand: true, cutoff: 0.45 },
      { notebookId: 'nb', tagIds: ['t1', 't2'], conversationId: 'c1' },
    );

    expect(request).toMatchObject({
      mode: 'semantic',
      expand: true,
      notebookIds: ['nb'],
      tagIds: ['t1', 't2'],
      conversationId: 'c1',
      maxCosineDistance: 0.45,
    });
  });
});

describe('parseSettings', () => {
  it('fills in what was never stored and survives garbage', () => {
    expect(parseSettings(null)).toEqual(DEFAULT_SETTINGS);
    expect(parseSettings('{"expand":true}')).toEqual({ ...DEFAULT_SETTINGS, expand: true });
    expect(parseSettings('not json')).toEqual(DEFAULT_SETTINGS);
  });
});

describe('describeSettings', () => {
  it('summarises the settings in a line', () => {
    expect(describeSettings({ mode: 'keyword', limit: 10, expand: true, cutoff: 0.5 })).toBe(
      'Keyword · expanded · top 10 · cutoff 0.5',
    );
  });
});
