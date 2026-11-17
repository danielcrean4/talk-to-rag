import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { ConversationDetails, ConversationsApi } from '../api/conversations-api';
import { SourcesApi } from '../api/sources-api';
import { HistoryPage } from './history-page';

const conversation = (id: string): ConversationDetails => ({
  id,
  title: id,
  createdAtUtc: '2026-11-16T12:00:00Z',
  updatedAtUtc: '2026-11-16T12:00:00Z',
  messages: [],
});

describe('HistoryPage', () => {
  const responses = new Map<string, Subject<ConversationDetails>>();

  beforeEach(() => {
    responses.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        {
          provide: ConversationsApi,
          useValue: {
            conversations: () => of([]),
            conversation: (id: string) => {
              responses.set(id, new Subject());
              return responses.get(id);
            },
          },
        },
        { provide: SourcesApi, useValue: { sources: () => of([]) } },
      ],
    });
  });

  it('ignores the conversation it was loading before the route changed', () => {
    const fixture = TestBed.createComponent(HistoryPage);
    fixture.componentRef.setInput('id', 'a');
    fixture.detectChanges();
    fixture.componentRef.setInput('id', 'b');
    fixture.detectChanges();
    const page = fixture.componentInstance;

    responses.get('a')!.next(conversation('a'));
    expect(page['conversation']()).toBeNull();
    expect(page['loading']()).toBe(true);

    responses.get('b')!.next(conversation('b'));
    expect(page['conversation']()?.id).toBe('b');
    expect(page['loading']()).toBe(false);
  });
});
