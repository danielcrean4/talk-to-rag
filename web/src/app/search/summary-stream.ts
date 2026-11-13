export interface SummaryState {
  answer: string;
  /** The passage numbers the model cited, known once the stream says it is done. */
  cited: number[] | null;
  done: boolean;
  error: string | null;
}

interface SummaryEvent {
  token?: string;
  done?: boolean;
  cited?: number[];
  error?: string;
}

/**
 * Reads the summary from the server-sent events received so far. The events are
 * `data: {"token": "..."}` lines, then `data: {"done": true, "cited": [..]}`, or `data: {"error": "..."}`.
 */
export function readSummary(received: string): SummaryState {
  const state: SummaryState = { answer: '', cited: null, done: false, error: null };
  const complete = received.slice(0, received.lastIndexOf('\n') + 1);
  for (const line of complete.split('\n')) {
    if (!line.startsWith('data:')) {
      continue;
    }
    let event: SummaryEvent;
    try {
      event = JSON.parse(line.slice(5));
    } catch {
      continue;
    }
    if (event.token) {
      state.answer += event.token;
    }
    if (event.done) {
      state.done = true;
      state.cited = event.cited ?? [];
    }
    if (event.error) {
      state.error = event.error;
    }
  }
  return state;
}
