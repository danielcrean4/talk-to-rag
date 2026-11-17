import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { SourceFileLinkResponse, SourcesApi } from './api/sources-api';
import { SourceFileLink } from './source-file-link';

@Component({
  imports: [SourceFileLink],
  template: `<a appSourceFile="s1" [at]="{ page: 12 }">Open</a>`,
})
class Host {}

describe('SourceFileLink', () => {
  let links: Subject<SourceFileLinkResponse>;
  let asked: string[];
  let tab: { opener: unknown; location: { href: string }; close: () => void; closed: boolean };

  beforeEach(() => {
    links = new Subject();
    asked = [];
    tab = {
      opener: window,
      location: { href: 'about:blank' },
      closed: false,
      close() {
        this.closed = true;
      },
    };
    vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);
    TestBed.configureTestingModule({
      providers: [
        {
          provide: SourcesApi,
          useValue: {
            sourceFileLink: (id: string) => {
              asked.push(id);
              return links;
            },
          },
        },
      ],
    });
  });

  afterEach(() => vi.restoreAllMocks());

  function click(): HTMLAnchorElement {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const anchor = fixture.nativeElement.querySelector('a') as HTMLAnchorElement;
    anchor.click();
    return anchor;
  }

  it('carries no token in the link itself', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const anchor = fixture.nativeElement.querySelector('a') as HTMLAnchorElement;

    expect(anchor.getAttribute('href')).toBe('/api/sources/s1/file#page=12');
    expect(anchor.target).toBe('_blank');
  });

  it('opens a tab during the click and sends it to a fresh file link', () => {
    click();

    expect(window.open).toHaveBeenCalledWith('', '_blank');
    expect(tab.opener).toBeNull();
    expect(asked).toEqual(['s1']);

    links.next({ url: '/api/sources/s1/file?access=abc', expiresAtUtc: '2026-11-17T12:05:00Z' });

    expect(tab.location.href).toBe(
      new URL('/api/sources/s1/file?access=abc#page=12', document.baseURI).href,
    );
  });

  it('closes the tab when the link cannot be had', () => {
    click();

    links.error(new Error('404'));

    expect(tab.closed).toBe(true);
  });
});
