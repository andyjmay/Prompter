# Backtracking / Self-Correction Detection

## Purpose
Detect when the speaker changes their mind mid-sentence and automatically keep only the final, corrected version of the thought.

## Problem Solved
Spoken language is nonlinear. Speakers often say "Let's meet at 2… actually, 3" or "Send it to John—no, send it to Jane." Without correction, the transcript contains the abandoned clause, forcing manual cleanup.

## How It Works
Two complementary strategies:

### 1. Prompt-Based (Chat Model)
The system prompt for the relevant mode includes:
> "If the speaker corrects themselves using phrases like 'actually', 'wait', 'scratch that', 'I mean', 'no,', or 'let me rephrase', discard the clause before the correction and keep only the final version."

### 2. Rule-Based Post-Filter (Safety Net)
A deterministic pass scans the formatted output for explicit backtracking patterns and prunes the abandoned text:
- **Markers:** `actually,`, `wait,`, `no,`, `scratch that`, `let me rephrase`, `I mean`, `correction:`
- **Scope:** Delete everything from the start of the current sentence up to the marker, then keep the remainder.
- **Boundaries:** Only operates within the same sentence to avoid over-deletion.

### Examples
| Raw Dictation | Detected Output |
|---------------|-----------------|
| "Let's meet at 2, actually 3." | "Let's meet at 3." |
| "Email John—wait, email Jane instead." | "Email Jane instead." |
| "The budget is fifty thousand, scratch that, forty thousand." | "The budget is forty thousand." |
| "I use Vim, I mean, VS Code." | "I use VS Code." |

## Edge Cases & Safeguards
- **False Positives:** "Actually, I think that's correct" should not delete "I think that's correct." The rule only triggers when the marker follows a complete clause and precedes a contradictory replacement.
- **Mode Gating:** This behavior is enabled in Standard and Formal modes; disabled in **Raw** mode so users who want verbatim transcripts are not affected.

## User Benefit
- Dramatically reduces editing burden for conversational, iterative dictation.
- Matches the behavior of premium cloud dictation tools (Wispr Flow, Otter.ai).
