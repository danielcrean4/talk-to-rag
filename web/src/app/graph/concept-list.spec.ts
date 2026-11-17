import { TestBed } from '@angular/core/testing';
import { Concept, Prerequisite } from '../api/concepts-api';
import { ConceptList } from './concept-list';
import { layoutGraph } from './graph-layout';

const concept = (id: string, level: 'New' | 'Learning' = 'New'): Concept => ({
  id,
  name: id,
  summary: null,
  state: 'Accepted',
  proposedByModel: false,
  passageCount: 0,
  mastery: { level, attempts: 0, recentScore: null, lastPractisedAtUtc: null },
});
const needs = (conceptId: string, prerequisiteId: string): Prerequisite => ({
  conceptId,
  prerequisiteId,
  state: 'Accepted',
});

describe('ConceptList', () => {
  it('groups the tiles by depth, with mastery and due cards, and picks the one clicked', () => {
    const fixture = TestBed.createComponent(ConceptList);
    const layout = layoutGraph(
      [concept('pooling', 'Learning'), concept('arithmetic'), concept('matrices')],
      [needs('pooling', 'matrices')],
    );
    fixture.componentRef.setInput('nodes', layout.nodes);
    fixture.componentRef.setInput('selectedId', 'pooling');
    fixture.componentRef.setInput(
      'due',
      new Map([['pooling', { conceptId: 'pooling', conceptName: 'pooling', due: 2, new: 0 }]]),
    );
    fixture.detectChanges();
    const picked: string[] = [];
    fixture.componentInstance.pick.subscribe((item) => picked.push(item.id));

    const sections = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('section'),
    ).map((section) => [
      section.querySelector('h3')!.textContent!.trim(),
      Array.from(section.querySelectorAll('button')).map((button) =>
        Array.from(button.querySelectorAll(':scope > span'))
          .map((span) => span.textContent!.replace(/\s+/g, ' ').trim())
          .join(' | '),
      ),
    ]);
    expect(sections).toEqual([
      ['Foundations', ['arithmetic | New', 'matrices | New']],
      ['Level 2', ['pooling | Learning · 2 due']],
    ]);

    const pooling = fixture.nativeElement.querySelector('[aria-pressed="true"]') as HTMLElement;
    pooling.click();
    expect(picked).toEqual(['pooling']);
  });
});
