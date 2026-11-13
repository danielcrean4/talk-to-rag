import { Source, Tag } from './api';

/** Adds the id to the picked ones, or takes it out if it is already there. */
export function toggleId(ids: string[], id: string): string[] {
  return ids.includes(id) ? ids.filter((item) => item !== id) : [...ids, id];
}

/** The picked ids that still name a tag, so a deleted tag does not empty a filter or a search. */
export function knownTagIds(ids: string[], tags: Tag[]): string[] {
  return ids.filter((id) => tags.some((tag) => tag.id === id));
}

/** The sources with any of the picked tags, or all of them when none is picked. */
export function withAnyTag(sources: Source[], tagIds: string[]): Source[] {
  if (tagIds.length === 0) {
    return sources;
  }
  return sources.filter((source) => source.tagIds.some((id) => tagIds.includes(id)));
}

/** The tags on a source, in the order of the tag list. */
export function tagsOn(source: Source, tags: Tag[]): Tag[] {
  return tags.filter((tag) => source.tagIds.includes(tag.id));
}

/** The tags a source does not have yet, offered when adding one. */
export function tagsOff(source: Source, tags: Tag[]): Tag[] {
  return tags.filter((tag) => !source.tagIds.includes(tag.id));
}

/** How many of the sources carry the tag. */
export function tagCount(sources: Source[], tagId: string): number {
  return sources.filter((source) => source.tagIds.includes(tagId)).length;
}
