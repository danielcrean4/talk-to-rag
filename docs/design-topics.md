# Design: Topics and Progress Tracking

*Design-only specification for study topic management and learning progress. No implementation code is committed until Dan reviews and selects a progress definition.*

---

## 1. Overview and Problem Statement

When learning technical domains (reinforcement learning, linear algebra, banjo, electronics) across multiple books and lecture notes, learners face two common hurdles:
1. **Scattered coverage:** Key concepts appear across different chapters, appendices, and multiple textbooks (e.g. "Bellman equation" in Sutton & Barto ch. 3, notes1 p. 8, and dynamic programming lectures).
2. **Abstract tracking:** Traditional flashcard apps track individual cards out of context, while generic checklists don't link back to the primary source text.

**Topics** bridge this gap by mapping *what Dan wants to learn* and *what Dan already knows* directly to the **specific textbook passages, definitions, and examples** where those concepts live.

---

## 2. Topic Data Model

```
   ┌────────────────────────────────────────────────────────┐
   │                        Topic                           │
   │  id: UUID                                              │
   │  user_id: UUID                                         │
   │  notebook_id: UUID? (optional notebook scope)          │
   │  title: string (e.g. "Normal Equation")                │
   │  origin: enum (UserManual | ExtractedCandidate)        │
   │  status: enum (WantToLearn | InProgress | Mastered)     │
   │  rank_order: integer (for reorderable study lists)     │
   │  created_at / updated_at                               │
   └───────────────────────────┬────────────────────────────┘
                               │ 1:N
                               ▼
   ┌────────────────────────────────────────────────────────┐
   │                     TopicPassage                       │
   │  topic_id: UUID                                        │
   │  source_chunk_id: UUID                                 │
   │  role: enum (Definition | CoreTheory | Example)        │
   │  relevance_score: float                                │
   │  created_at                                            │
   └────────────────────────────────────────────────────────┘
```

### Proposed Schema

```sql
CREATE TABLE topics (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    notebook_id UUID REFERENCES notebooks(id) ON DELETE SET NULL,
    title VARCHAR(200) NOT NULL,
    description TEXT,
    origin VARCHAR(30) NOT NULL DEFAULT 'UserManual', -- 'UserManual' | 'ExtractedCandidate'
    status VARCHAR(30) NOT NULL DEFAULT 'WantToLearn', -- 'WantToLearn' | 'InProgress' | 'Mastered'
    rank_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX ix_topics_user_notebook ON topics(user_id, notebook_id, rank_order);

CREATE TABLE topic_passages (
    topic_id UUID NOT NULL REFERENCES topics(id) ON DELETE CASCADE,
    source_chunk_id UUID NOT NULL REFERENCES source_chunks(id) ON DELETE CASCADE,
    role VARCHAR(30) NOT NULL DEFAULT 'CoreTheory', -- 'Definition' | 'CoreTheory' | 'Example'
    relevance_score DOUBLE PRECISION NOT NULL DEFAULT 1.0,
    created_at_utc TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (topic_id, source_chunk_id)
);

CREATE INDEX ix_topic_passages_chunk ON topic_passages(source_chunk_id);
```

### Where Topics Come From

1. **User-created (Dan):** Concepts Dan explicitly intends to learn (e.g. typed in or spoken: "add topic Normal Equation to CS229").
2. **Extracted from textbooks (Candidates):**
   - During source ingestion, Markdown headings and EPUB spine headers (e.g. `## Linear Regression and Gradient Descent`) are parsed into proposed candidate topics.
   - Distinctive glossary and theorem patterns (`Theorem 1.1:`, `Definition:`) produce candidate links to source chunks.
   - Candidate topics appear in an "Extracted Topics" review queue; Dan approves or rejects them into his active study list with one click or voice command.

---

## 3. Ranked, Reorderable Study Lists

In the UI:
- Topics are displayed in a clean study queue grouped by notebook or tag.
- Each topic card displays:
  - Topic Title and status badge (`Want to Learn`, `In Progress`, `Mastered`).
  - Linked passages badge: e.g. `3 passages (CS229 notes1 p. 8, p. 10; Linear Algebra p. 25)`.
  - Reordering handles (drag-and-drop) or quick buttons (`Move to Top`, `Snooze`).
- Clicking or asking for a topic immediately reveals its linked passages with one-click navigation to the exact page in the document viewer.

---

## 4. Definitions of "Progress" (For Dan to Decide)

Before building progress bars or tracking meters, Dan needs to choose which definition of progress matches his actual study habits:

### Option 1: Passive Engagement / Exposure Tracking (Automated)
- **How it works:** A topic is marked 100% "covered" once all its linked passages have been read aloud (listened to >= 80% duration) or kept open in the document reader for >= 30 seconds.
- **Pros:** Completely automated; requires zero manual button clicking while hands are busy.
- **Cons:** Passive listening does not guarantee comprehension; passing time in a tab doesn't mean deep learning.

### Option 2: Self-Assessed Confidence Marking (Clean & Honest)
- **How it works:** Dan self-marks topics with a simple 3-tier status:
  - 🟡 *Want to Learn* (unstudied)
  - 🔵 *In Progress* (read the passages, worked through examples)
  - 🟢 *Mastered* (can derive the math or write the code without looking)
- Can be updated via voice ("mark Normal Equation as Mastered") or with a single click.
- **Pros:** Completely honest; avoids gimmicky progress percentages or gamification; gives total control to the learner.
- **Cons:** Requires a conscious update action from Dan.

### Option 3: Active Recall / "Explain-Back" Verification (Conversational Voice)
- **How it works:** Dan speaks a short 2-3 sentence explanation of the concept ("explain back"). A small local model compares the key claims in his spoken explanation against the facts in the textbook's linked definition passages, providing constructive feedback and marking the topic verified.
- **Pros:** The highest-retention learning technique (Feynman technique); perfectly fits hands-free audio study (walking, holding a baby).
- **Cons:** Adds an LLM verification step; requires prompt tuning so the local model is neither pedantic nor lenient.

---

## 5. Next Steps

- Dan selects which Progress Definition (Option 1, 2, or 3) he prefers.
- The selected schema migration and topic CRUD endpoints will be scheduled for implementation following the Milestone 4 voice and streaming deliverables.
