import { Concept, Prerequisite } from '../api';

export const NODE_WIDTH = 164;
export const NODE_HEIGHT = 56;
const GAP_X = 18;
const GAP_Y = 72;
const PADDING = 32;

export interface PlacedConcept {
  concept: Concept;
  /** 0 for a concept with no prerequisites; otherwise one more than its deepest prerequisite. */
  level: number;
  x: number;
  y: number;
}

export interface PlacedLink {
  link: Prerequisite;
  path: string;
}

export interface GraphLayout {
  nodes: PlacedConcept[];
  links: PlacedLink[];
  width: number;
  height: number;
}

/**
 * Places concepts in rows by how much they build on: foundations along the bottom,
 * and each concept one row above its deepest prerequisite.
 */
export function layoutGraph(concepts: Concept[], prerequisites: Prerequisite[]): GraphLayout {
  const ids = new Set(concepts.map((concept) => concept.id));
  const links = prerequisites.filter(
    (link) => ids.has(link.conceptId) && ids.has(link.prerequisiteId),
  );
  const prerequisitesOf = new Map<string, string[]>();
  for (const link of links) {
    prerequisitesOf.set(link.conceptId, [
      ...(prerequisitesOf.get(link.conceptId) ?? []),
      link.prerequisiteId,
    ]);
  }

  const levels = new Map<string, number>();
  const levelOf = (id: string, path: Set<string>): number => {
    const known = levels.get(id);
    if (known !== undefined) {
      return known;
    }
    if (path.has(id)) {
      return 0; // The API refuses loops; this only guards the drawing against bad data.
    }
    path.add(id);
    const below = (prerequisitesOf.get(id) ?? []).map((prerequisite) =>
      levelOf(prerequisite, path),
    );
    path.delete(id);
    const level = below.length === 0 ? 0 : Math.max(...below) + 1;
    levels.set(id, level);
    return level;
  };
  concepts.forEach((concept) => levelOf(concept.id, new Set()));

  const rows = new Map<number, Concept[]>();
  for (const concept of concepts) {
    const level = levels.get(concept.id)!;
    rows.set(level, [...(rows.get(level) ?? []), concept]);
  }

  const topLevel = Math.max(0, ...rows.keys());
  const widest = Math.max(1, ...[...rows.values()].map((row) => row.length));
  const width = PADDING * 2 + widest * NODE_WIDTH + (widest - 1) * GAP_X;
  const height = PADDING * 2 + (topLevel + 1) * NODE_HEIGHT + topLevel * GAP_Y;

  const nodes: PlacedConcept[] = [];
  for (const [level, row] of rows) {
    const rowWidth = row.length * NODE_WIDTH + (row.length - 1) * GAP_X;
    const start = (width - rowWidth) / 2;
    row
      .sort((a, b) => a.name.localeCompare(b.name))
      .forEach((concept, index) =>
        nodes.push({
          concept,
          level,
          x: start + index * (NODE_WIDTH + GAP_X),
          y: PADDING + (topLevel - level) * (NODE_HEIGHT + GAP_Y),
        }),
      );
  }

  const placed = new Map(nodes.map((node) => [node.concept.id, node]));
  return {
    nodes,
    links: links.map((link) => ({
      link,
      path: curve(placed.get(link.conceptId)!, placed.get(link.prerequisiteId)!),
    })),
    width,
    height,
  };
}

/** A curve from the bottom of a concept down to the top of its prerequisite. */
function curve(from: PlacedConcept, to: PlacedConcept): string {
  const x1 = from.x + NODE_WIDTH / 2;
  const y1 = from.y + NODE_HEIGHT;
  const x2 = to.x + NODE_WIDTH / 2;
  const y2 = to.y;
  const bend = (y2 - y1) / 2;
  return `M ${x1} ${y1} C ${x1} ${y1 + bend}, ${x2} ${y2 - bend}, ${x2} ${y2}`;
}
