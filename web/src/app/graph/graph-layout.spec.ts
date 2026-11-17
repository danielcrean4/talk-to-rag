import { Concept, Prerequisite } from '../api/concepts-api';
import { NODE_HEIGHT, NODE_WIDTH, layoutCrossings, layoutGraph, levelGroups } from './graph-layout';

const concept = (id: string): Concept => ({
  id,
  name: id,
  summary: null,
  state: 'Accepted',
  proposedByModel: false,
  passageCount: 0,
  mastery: { level: 'New', attempts: 0, recentScore: null, lastPractisedAtUtc: null },
});
const needs = (conceptId: string, prerequisiteId: string): Prerequisite => ({
  conceptId,
  prerequisiteId,
  state: 'Accepted',
});

describe('layoutGraph', () => {
  it('puts each concept one row above its deepest prerequisite, foundations at the bottom', () => {
    const layout = layoutGraph(
      [concept('cnn'), concept('convolution'), concept('matrices'), concept('arithmetic')],
      [
        needs('cnn', 'convolution'),
        needs('cnn', 'arithmetic'),
        needs('convolution', 'matrices'),
        needs('matrices', 'arithmetic'),
      ],
    );
    const node = (id: string) => layout.nodes.find((placed) => placed.concept.id === id)!;

    expect(['arithmetic', 'matrices', 'convolution', 'cnn'].map((id) => node(id).level)).toEqual([
      0, 1, 2, 3,
    ]);
    expect(node('arithmetic').y).toBeGreaterThan(node('cnn').y);
    expect(layout.height).toBeGreaterThan(node('arithmetic').y + NODE_HEIGHT);
    expect(layout.links).toHaveLength(4);
  });

  it('ignores links to concepts that are not shown, and survives a loop in bad data', () => {
    const layout = layoutGraph(
      [concept('a'), concept('b')],
      [needs('a', 'b'), needs('b', 'a'), needs('a', 'gone')],
    );

    expect(layout.nodes).toHaveLength(2);
    expect(layout.links).toHaveLength(2);
  });

  it('lays out an empty graph', () => {
    expect(layoutGraph([], []).nodes).toEqual([]);
  });

  it('orders each level to cross fewer links than name order does', () => {
    const concepts = ['a', 'b', 'c', 'x', 'y', 'z'].map(concept);
    const prerequisites = [needs('x', 'c'), needs('y', 'b'), needs('z', 'a'), needs('y', 'a')];

    const byName = layoutCrossings(layoutGraph(concepts, prerequisites, { sweeps: 0 }));
    const swept = layoutCrossings(layoutGraph(concepts, prerequisites));

    expect(byName).toBeGreaterThan(0);
    expect(swept).toBeLessThan(byName);
  });

  it('wraps a wide level onto several lines that keep within the widest line', () => {
    const concepts = Array.from({ length: 13 }, (_, index) => concept(`c${index}`));
    const layout = layoutGraph(concepts, [], { maxPerRow: 5 });

    const lines = new Set(layout.nodes.map((node) => node.y));
    expect(lines.size).toBe(3);
    expect(layout.width).toBeLessThan(6 * NODE_WIDTH);
    for (const node of layout.nodes) {
      expect(node.x).toBeGreaterThanOrEqual(0);
      expect(node.x + NODE_WIDTH).toBeLessThanOrEqual(layout.width);
      expect(node.y + NODE_HEIGHT).toBeLessThanOrEqual(layout.height);
    }
  });

  it('lays out a chain far deeper than the call stack', () => {
    const depth = 20000;
    const concepts = Array.from({ length: depth }, (_, index) => concept(`c${index}`));
    const prerequisites = concepts
      .slice(1)
      .map((item, index) => needs(item.id, concepts[index].id));

    const layout = layoutGraph(concepts, prerequisites);

    expect(layout.nodes.find((node) => node.concept.id === `c${depth - 1}`)!.level).toBe(depth - 1);
  });
});

describe('levelGroups', () => {
  it('lists concepts by level, foundations first and each level by name', () => {
    const layout = layoutGraph(
      [concept('cnn'), concept('pooling'), concept('arithmetic'), concept('matrices')],
      [needs('cnn', 'pooling'), needs('cnn', 'matrices'), needs('pooling', 'arithmetic')],
    );

    expect(
      levelGroups(layout.nodes).map((group) => [
        group.level,
        group.concepts.map((item) => item.name),
      ]),
    ).toEqual([
      [0, ['arithmetic', 'matrices']],
      [1, ['pooling']],
      [2, ['cnn']],
    ]);
  });
});
