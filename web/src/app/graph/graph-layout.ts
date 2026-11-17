import { Concept, Prerequisite } from '../api/concepts-api';

export const NODE_WIDTH = 164;
export const NODE_HEIGHT = 56;
const GAP_X = 18;
const GAP_Y = 72;
/** Between the lines a wide level wraps onto. */
const WRAP_GAP_Y = 28;
const PADDING = 32;
/** Barycenter passes over the levels, alternately upwards and downwards. */
const SWEEPS = 4;
const MAX_PER_ROW = 6;

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

export interface LayoutOptions {
  /** Barycenter passes to reduce crossing links; 0 keeps each level in name order. */
  sweeps?: number;
  /** A level with more concepts than this wraps onto several lines. */
  maxPerRow?: number;
}

export interface LevelGroup {
  level: number;
  concepts: Concept[];
}

/** A link as a straight line, with `y1` above `y2`. */
interface Segment {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

/**
 * Places concepts in rows by how much they build on: foundations along the bottom,
 * and each concept one row above its deepest prerequisite. Each row is ordered to cross
 * as few links as it can, and a row wider than `maxPerRow` wraps onto more lines.
 */
export function layoutGraph(
  concepts: Concept[],
  prerequisites: Prerequisite[],
  options: LayoutOptions = {},
): GraphLayout {
  const maxPerRow = Math.max(1, options.maxPerRow ?? MAX_PER_ROW);
  const ids = new Set(concepts.map((concept) => concept.id));
  const links = prerequisites.filter(
    (link) => ids.has(link.conceptId) && ids.has(link.prerequisiteId),
  );
  const levels = conceptLevels(concepts, links);
  const rows = orderRows(concepts, links, levels, options.sweeps ?? SWEEPS);

  // Top level first, so lines are listed in the order they are drawn down the page.
  const lines: { level: number; concepts: Concept[] }[] = [];
  for (let level = rows.length - 1; level >= 0; level--) {
    // A level is empty only when a loop in bad data skipped it.
    for (const line of rows[level].length ? wrap(rows[level], maxPerRow) : []) {
      lines.push({ level, concepts: line });
    }
  }

  const widest = Math.max(1, ...lines.map((line) => line.concepts.length));
  const width = PADDING * 2 + widest * NODE_WIDTH + (widest - 1) * GAP_X;
  const nodes: PlacedConcept[] = [];
  let y = PADDING;
  lines.forEach((line, index) => {
    if (index > 0) {
      y += NODE_HEIGHT + (lines[index - 1].level === line.level ? WRAP_GAP_Y : GAP_Y);
    }
    const lineWidth = line.concepts.length * NODE_WIDTH + (line.concepts.length - 1) * GAP_X;
    const start = (width - lineWidth) / 2;
    line.concepts.forEach((concept, slot) =>
      nodes.push({ concept, level: line.level, x: start + slot * (NODE_WIDTH + GAP_X), y }),
    );
  });
  const height = y + NODE_HEIGHT + PADDING;

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

/** The concepts of a layout by level, foundations first and each level in name order. */
export function levelGroups(nodes: PlacedConcept[]): LevelGroup[] {
  const groups = new Map<number, Concept[]>();
  for (const node of nodes) {
    groups.set(node.level, [...(groups.get(node.level) ?? []), node.concept]);
  }
  return [...groups.entries()]
    .sort(([a], [b]) => a - b)
    .map(([level, concepts]) => ({ level, concepts: concepts.sort(byName) }));
}

/** How many pairs of links cross, drawing each link as a straight line between tile centres. */
export function layoutCrossings(layout: GraphLayout): number {
  const centre = new Map(
    layout.nodes.map((node) => [
      node.concept.id,
      { x: node.x + NODE_WIDTH / 2, y: node.y + NODE_HEIGHT / 2 },
    ]),
  );
  return countCrossings(
    layout.links.map(({ link }) => {
      const from = centre.get(link.conceptId)!;
      const to = centre.get(link.prerequisiteId)!;
      return { x1: from.x, y1: from.y, x2: to.x, y2: to.y };
    }),
  );
}

/**
 * The level of each concept, found without recursion so a long chain cannot overflow the stack.
 * The API refuses loops; a link that closes one in bad data counts as a foundation.
 */
function conceptLevels(concepts: Concept[], links: Prerequisite[]): Map<string, number> {
  const prerequisitesOf = new Map<string, string[]>();
  for (const link of links) {
    prerequisitesOf.set(link.conceptId, [
      ...(prerequisitesOf.get(link.conceptId) ?? []),
      link.prerequisiteId,
    ]);
  }

  const levels = new Map<string, number>();
  const onPath = new Set<string>();
  for (const concept of concepts) {
    if (levels.has(concept.id)) {
      continue;
    }
    const stack = [{ id: concept.id, next: 0, deepest: -1 }];
    onPath.add(concept.id);
    while (stack.length > 0) {
      const frame = stack[stack.length - 1];
      const below = prerequisitesOf.get(frame.id) ?? [];
      if (frame.next < below.length) {
        const prerequisite = below[frame.next++];
        const known = levels.get(prerequisite);
        if (known !== undefined) {
          frame.deepest = Math.max(frame.deepest, known);
        } else if (onPath.has(prerequisite)) {
          frame.deepest = Math.max(frame.deepest, 0);
        } else {
          onPath.add(prerequisite);
          stack.push({ id: prerequisite, next: 0, deepest: -1 });
        }
        continue;
      }
      const level = frame.deepest + 1;
      levels.set(frame.id, level);
      onPath.delete(frame.id);
      stack.pop();
      const parent = stack[stack.length - 1];
      if (parent) {
        parent.deepest = Math.max(parent.deepest, level);
      }
    }
  }
  return levels;
}

/**
 * The concepts of each level, ordered by barycenter sweeps: each concept moves towards the
 * average position of the concepts it links to in the levels already placed. The order with
 * the fewest crossings wins, and name order breaks ties.
 */
function orderRows(
  concepts: Concept[],
  links: Prerequisite[],
  levels: Map<string, number>,
  sweeps: number,
): Concept[][] {
  const top = Math.max(0, ...levels.values());
  const rows: Concept[][] = Array.from({ length: top + 1 }, () => []);
  for (const concept of concepts) {
    rows[levels.get(concept.id)!].push(concept);
  }
  rows.forEach((row) => row.sort(byName));
  if (sweeps <= 0 || rows.every((row) => row.length < 2)) {
    return rows;
  }

  const below = new Map<string, string[]>();
  const above = new Map<string, string[]>();
  for (const link of links) {
    below.set(link.conceptId, [...(below.get(link.conceptId) ?? []), link.prerequisiteId]);
    above.set(link.prerequisiteId, [...(above.get(link.prerequisiteId) ?? []), link.conceptId]);
  }
  const position = new Map<string, number>();
  const place = (row: Concept[]) =>
    row.forEach((concept, index) => position.set(concept.id, index - (row.length - 1) / 2));
  rows.forEach(place);
  const crossings = () =>
    countCrossings(
      links.map((link) => ({
        x1: position.get(link.conceptId)!,
        y1: -levels.get(link.conceptId)!,
        x2: position.get(link.prerequisiteId)!,
        y2: -levels.get(link.prerequisiteId)!,
      })),
    );

  let best = rows.map((row) => [...row]);
  let fewest = crossings();
  for (let sweep = 0; sweep < sweeps && fewest > 0; sweep++) {
    const upwards = sweep % 2 === 0;
    const neighbours = upwards ? below : above;
    for (let step = 1; step <= top; step++) {
      const level = upwards ? step : top - step;
      const barycenter = new Map(
        rows[level].map((concept) => {
          const linked = neighbours.get(concept.id) ?? [];
          const mean =
            linked.length === 0
              ? position.get(concept.id)!
              : linked.reduce((sum, id) => sum + position.get(id)!, 0) / linked.length;
          return [concept.id, mean];
        }),
      );
      rows[level] = [...rows[level]].sort(
        (a, b) =>
          barycenter.get(a.id)! - barycenter.get(b.id)! ||
          position.get(a.id)! - position.get(b.id)!,
      );
      place(rows[level]);
    }
    const count = crossings();
    if (count < fewest) {
      fewest = count;
      best = rows.map((row) => [...row]);
    }
  }
  return best;
}

/** Spreads a row over as few lines as hold it, taking every n-th concept so each line keeps the row's left-to-right order. */
function wrap(row: Concept[], maxPerRow: number): Concept[][] {
  const count = Math.ceil(row.length / maxPerRow);
  if (count <= 1) {
    return [row];
  }
  return Array.from({ length: count }, (_, line) =>
    row.filter((_, index) => index % count === line),
  );
}

function countCrossings(segments: Segment[]): number {
  const lines = segments.map((segment) =>
    segment.y1 <= segment.y2
      ? segment
      : { x1: segment.x2, y1: segment.y2, x2: segment.x1, y2: segment.y1 },
  );
  const xAt = (line: Segment, y: number) =>
    line.y2 === line.y1
      ? line.x1
      : line.x1 + ((line.x2 - line.x1) * (y - line.y1)) / (line.y2 - line.y1);
  let count = 0;
  for (let i = 0; i < lines.length; i++) {
    for (let j = i + 1; j < lines.length; j++) {
      const a = lines[i];
      const b = lines[j];
      const from = Math.max(a.y1, b.y1);
      const to = Math.min(a.y2, b.y2);
      if (to <= from) {
        continue;
      }
      const start = xAt(a, from) - xAt(b, from);
      const end = xAt(a, to) - xAt(b, to);
      if (start * end < 0) {
        count++;
      }
    }
  }
  return count;
}

function byName(a: Concept, b: Concept): number {
  return a.name.localeCompare(b.name);
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
