import { Concept, Prerequisite } from '../api';
import { NODE_HEIGHT, layoutGraph } from './graph-layout';

const concept = (id: string): Concept => ({
  id,
  name: id,
  summary: null,
  state: 'Accepted',
  proposedByModel: false,
  passageCount: 0,
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
});
