import { Source, SourceStatus } from '../api';
import { anyWorking, failureNotice, formatBytes, inNotebook, reprocessLabel } from './source-list';

const source = (id: string, status: SourceStatus = 'Ready'): Source => ({
  id,
  title: id,
  fileName: `${id}.pdf`,
  contentType: 'application/pdf',
  sizeBytes: 1,
  status,
  failureReason: null,
  chunkCount: 0,
  createdAtUtc: '2026-01-01T00:00:00Z',
  tagIds: [],
});

describe('formatBytes', () => {
  it('picks a readable unit', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1536)).toBe('1.5 KB');
    expect(formatBytes(114386806)).toBe('109.1 MB');
  });
});

describe('anyWorking', () => {
  it('is true while a source is pending or processing', () => {
    expect(anyWorking([source('a'), source('b', 'Processing')])).toBe(true);
    expect(anyWorking([source('a', 'Pending')])).toBe(true);
    expect(anyWorking([source('a'), source('b', 'Failed')])).toBe(false);
  });
});

describe('inNotebook', () => {
  it('keeps the sources in the notebook, or all of them without one', () => {
    const all = [source('a'), source('b'), source('c')];

    expect(inNotebook(all, ['c', 'a']).map((item) => item.id)).toEqual(['a', 'c']);
    expect(inNotebook(all, null)).toBe(all);
    expect(inNotebook(all, [])).toEqual([]);
  });
});

describe('reprocessLabel', () => {
  it('offers Retry after a failure and Process again once ready', () => {
    expect(reprocessLabel(source('a', 'Failed'))).toBe('Retry');
    expect(reprocessLabel(source('a', 'Ready'))).toBe('Process again');
    expect(reprocessLabel(source('a', 'Pending'))).toBeNull();
    expect(reprocessLabel(source('a', 'Processing'))).toBeNull();
  });
});

describe('failureNotice', () => {
  it('explains a failure, including one that left a ready source with its old passages', () => {
    expect(failureNotice(source('a', 'Failed'))).toBe('Ingestion failed.');
    expect(failureNotice({ ...source('a', 'Failed'), failureReason: 'No text layer' })).toBe(
      'No text layer',
    );
    expect(failureNotice(source('a', 'Ready'))).toBeNull();
    expect(
      failureNotice({ ...source('a', 'Ready'), failureReason: 'Processing again failed' }),
    ).toBe('Processing again failed');
    expect(failureNotice({ ...source('a', 'Processing'), failureReason: 'stale' })).toBeNull();
  });
});
