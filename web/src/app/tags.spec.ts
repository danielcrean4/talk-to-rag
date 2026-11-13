import { Source, Tag } from './api';
import { knownTagIds, tagCount, tagsOff, tagsOn, toggleId, withAnyTag } from './tags';

const tag = (id: string): Tag => ({ id, name: id, createdAtUtc: '2026-01-01T00:00:00Z' });

const source = (id: string, tagIds: string[] = []): Source => ({
  id,
  title: id,
  fileName: `${id}.pdf`,
  contentType: 'application/pdf',
  sizeBytes: 1,
  status: 'Ready',
  failureReason: null,
  chunkCount: 0,
  createdAtUtc: '2026-01-01T00:00:00Z',
  tagIds,
});

const tags = [tag('sea'), tag('land'), tag('sky')];

describe('toggleId', () => {
  it('adds an id that is not picked and removes one that is', () => {
    expect(toggleId([], 'sea')).toEqual(['sea']);
    expect(toggleId(['sea', 'land'], 'sea')).toEqual(['land']);
  });
});

describe('knownTagIds', () => {
  it('drops the ids of deleted tags', () => {
    expect(knownTagIds(['sea', 'gone', 'sky'], tags)).toEqual(['sea', 'sky']);
    expect(knownTagIds(['sea'], [])).toEqual([]);
  });
});

describe('withAnyTag', () => {
  it('keeps the sources with any of the picked tags, or all of them without one', () => {
    const all = [source('a', ['sea']), source('b', ['land', 'sky']), source('c')];

    expect(withAnyTag(all, ['sky', 'sea']).map((item) => item.id)).toEqual(['a', 'b']);
    expect(withAnyTag(all, ['land']).map((item) => item.id)).toEqual(['b']);
    expect(withAnyTag(all, [])).toBe(all);
  });
});

describe('tagsOn and tagsOff', () => {
  it('split the tag list into the tags a source has and the ones it could get', () => {
    const tagged = source('a', ['sky', 'sea', 'gone']);

    expect(tagsOn(tagged, tags).map((item) => item.id)).toEqual(['sea', 'sky']);
    expect(tagsOff(tagged, tags).map((item) => item.id)).toEqual(['land']);
  });
});

describe('tagCount', () => {
  it('counts the sources carrying a tag', () => {
    const all = [source('a', ['sea']), source('b', ['sea', 'sky']), source('c')];

    expect(tagCount(all, 'sea')).toBe(2);
    expect(tagCount(all, 'land')).toBe(0);
  });
});
