import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PassageGroup } from '../api/search-api';
import { SearchSession } from './search-session';

const FOUND: PassageGroup[] = [
  {
    source: { id: 's1', title: 'Tides', contentType: 'text/markdown' },
    passages: [
      {
        number: 1,
        chunkId: 'c1',
        pageNumber: 1,
        pageLabel: null,
        section: null,
        snippet: 'The moon pulls the sea.',
        score: 1,
        matchedBy: ['semantic'],
      },
    ],
  },
];

describe('SearchSession', () => {
  let http: HttpTestingController;
  let session: SearchSession;

  function search(groups: PassageGroup[]): void {
    session.search('tides', { notebookId: null, tagIds: [] });
    http.expectOne('/api/search').flush({ conversationId: 'c', query: 'tides', groups });
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SearchSession);
  });

  afterEach(() => http.verify());

  it('streams the summary once the passages are in, without being asked', () => {
    search(FOUND);

    expect(session.results()?.groups).toEqual(FOUND);
    expect(session.summarizing()).toBe(true);
    http
      .expectOne('/api/search/c/summary/stream')
      .flush('data: {"token": "The moon [1]."}\n\ndata: {"done": true, "cited": [1]}\n\n');
    expect(session.summary()?.answer).toBe('The moon [1].');
    expect(session.summarizing()).toBe(false);
  });

  it('asks for no summary when nothing was found', () => {
    search([]);

    expect(session.summary()).toBeNull();
    expect(session.summarizing()).toBe(false);
  });

  it('drops a search that a newer one replaced, so its summary is never asked for', () => {
    session.search('first', { notebookId: null, tagIds: [] });
    const first = http.expectOne('/api/search');
    session.search('second', { notebookId: null, tagIds: [] });
    const second = http.expectOne('/api/search');

    expect(first.cancelled).toBe(true);
    second.flush({ conversationId: 'c2', query: 'second', groups: FOUND });
    expect(session.conversationId()).toBe('c2');
    http.expectOne('/api/search/c2/summary/stream').flush('data: {"done": true, "cited": []}\n\n');
  });

  it('drops a search in flight when the user starts over', () => {
    session.search('tides', { notebookId: null, tagIds: [] });
    const request = http.expectOne('/api/search');

    session.startOver();

    expect(request.cancelled).toBe(true);
    expect(session.searching()).toBe(false);
    expect(session.results()).toBeNull();
    expect(session.conversationId()).toBeNull();
  });

  it('keeps the passages and reports the error when the summary fails', () => {
    search(FOUND);
    http
      .expectOne('/api/search/c/summary/stream')
      .flush('no model', { status: 503, statusText: 'Service Unavailable' });

    expect(session.results()?.groups).toEqual(FOUND);
    expect(session.summary()?.error).toBeTruthy();
    expect(session.summarizing()).toBe(false);
  });
});
