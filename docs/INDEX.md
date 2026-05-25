# Prompter Feature Roadmap Index

This index catalogs all planned feature specifications for Prompter, ordered by implementation priority. Each document describes the feature's purpose, how it fits into the existing pipeline, and the user benefit it delivers.

## Priority Legend

| Priority | Meaning |
|----------|---------|
| **P0 - Critical** | Immediate UX upgrade, low effort, high adoption impact |
| **P1 - High** | Significant user value, moderate complexity |
| **P2 - Medium** | Nice-to-have that differentiates from basic dictation |
| **P3 - Lower** | Advanced capability requiring architectural change |

---

## Feature Specifications

### P0 - Critical

| # | Feature | File | Effort | Rationale |
|---|---------|------|--------|-----------|
| 1 | **Spoken Punctuation Commands** | [feature-spoken-punctuation.md](./feature-spoken-punctuation.md) | Low | Pure text substitution; instant precision improvement; works in every mode |
| 2 | **Personal Dictionary** | [feature-personal-dictionary.md](./feature-personal-dictionary.md) | Low | Fixes the #1 quality complaint (names/jargon); no model changes |

### P1 - High

| # | Feature | File | Effort | Rationale |
|---|---------|------|--------|-----------|
| 3 | **Text Snippets (Voice Shortcuts)** | [feature-text-snippets.md](./feature-text-snippets.md) | Medium | Power-user differentiator; programmable voice macros |
| 4 | **Filler-Word Removal Mode** | [feature-filler-word-removal.md](./feature-filler-word-removal.md) | Low | Complements existing Formal mode; dedicated prompt + post-filter |
| 5 | **List Formatting Mode** | [feature-list-formatting.md](./feature-list-formatting.md) | Low | Low effort, high value for structured note-taking |
| 6 | **Developer / Syntax-Aware Mode** | [feature-developer-syntax-mode.md](./feature-developer-syntax-mode.md) | Low | New `ModeConfig` only; massive value for dev audience |

### P2 - Medium

| # | Feature | File | Effort | Rationale |
|---|---------|------|--------|-----------|
| 7 | **Backtracking / Self-Correction Detection** | [feature-backtracking-correction.md](./feature-backtracking-correction.md) | Medium | Prompt engineering + rule-based parsing; careful QA needed |
| 8 | **Multi-Language Auto-Detection** | [feature-multi-language-auto-detection.md](./feature-multi-language-auto-detect.md) | Low | Remove hardcoded `Language` constraint; expands TAM significantly |
| 9 | **Whisper / Quiet-Space Recording** | [feature-whisper-quiet-recording.md](./feature-whisper-quiet-recording.md) | Medium | Audio preprocessing chain; constrained by upstream model limits |

### P3 - Lower

| # | Feature | File | Effort | Rationale |
|---|---------|------|--------|-----------|
| 10 | **Inline Command Mode** | [feature-inline-command-mode.md](./feature-inline-command-mode.md) | High | Requires architectural change to chunked/streaming pipeline; highest complexity |

---

## Dependency Graph

```
Spoken Punctuation (P0)
    └── List Formatting (P1) — uses punctuation tokens for item breaks

Personal Dictionary (P0)
    ├── Developer Mode (P1) — protects technical identifiers
    ├── Filler-Word Removal (P1) — avoids stripping protected terms
    └── Backtracking (P2) — preserves dictionary entries during pruning

Text Snippets (P1)
    └── Inline Commands (P3) — snippets are simpler macros; commands are dynamic

Multi-Language (P2)
    └── All Modes — formatter prompts need language-agnostic rewrite
```

## Recommended Sprint Plan

### Sprint 1: Foundation (P0)
- Spoken Punctuation Commands
- Personal Dictionary

### Sprint 2: Modes & Polish (P1)
- Filler-Word Removal Mode
- List Formatting Mode
- Developer / Syntax-Aware Mode

### Sprint 3: Power Features (P1 → P2)
- Text Snippets
- Multi-Language Auto-Detection
- Backtracking / Self-Correction Detection

### Sprint 4: Quality of Life (P2)
- Whisper / Quiet-Space Recording

### Sprint 5: Architecture (P3)
- Inline Command Mode (requires chunked pipeline refactor)

---

## Design Principles

1. **Privacy First** — All features run locally; no cloud dependency for any capability.
2. **Composable Modes** — Features are implemented as orthogonal capabilities that can be combined (e.g., Developer Mode + Personal Dictionary + Spoken Punctuation).
3. **Graceful Degradation** — Post-filter safety nets ensure that even if the chat model fails, the user gets usable output.
4. **Minimal Intrusion** — New features should not require breaking changes to `PipelineOrchestrator`, `AppConfig`, or the DI container unless unavoidable.

## Status Key

When implementation begins, update this index with a status column:

| Status | Meaning |
|--------|---------|
| `spec` | Document only — not yet started |
| `wip`  | In progress |
| `pr`   | Pull request open |
| `done` | Merged and released |

---

*Generated 2026-05-24. Update this file as features shift in priority or scope.*
