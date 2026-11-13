import { readSummary } from './summary-stream';

describe('readSummary', () => {
  it('joins the tokens and picks up the cited passages when done', () => {
    const stream =
      'data: {"token": "Signals "}\n\ndata: {"token": "decouple [1]."}\n\ndata: {"done": true, "cited": [1]}\n\n';

    expect(readSummary(stream)).toEqual({
      answer: 'Signals decouple [1].',
      cited: [1],
      done: true,
      error: null,
    });
  });

  it('waits for a line to finish before reading it', () => {
    expect(readSummary('data: {"token": "Hel"}\n\ndata: {"tok').answer).toBe('Hel');
  });

  it('keeps tokens that contain newlines, which arrive escaped', () => {
    expect(readSummary('data: {"token": "one\\n\\ntwo"}\n\n').answer).toBe('one\n\ntwo');
  });

  it('reports an error event and is not done without a done event', () => {
    const state = readSummary(
      'data: {"token": "Part"}\n\ndata: {"error": "model unavailable"}\n\n',
    );

    expect(state.error).toBe('model unavailable');
    expect(state.done).toBe(false);
    expect(state.cited).toBeNull();
  });

  it('reads nothing from an empty stream', () => {
    expect(readSummary('')).toEqual({ answer: '', cited: null, done: false, error: null });
  });
});
