# Filler-Word Removal Mode

## Purpose
Provide a dedicated formatting mode that strips speech disfluencies—"um," "uh," "like," "you know," and similar verbal placeholders—producing clean, publication-ready text without altering meaning.

## Problem Solved
Even confident speakers produce filler words when thinking aloud. The Standard and Formal modes attempt to correct these, but a targeted mode with both a specialized system prompt and a hardcoded post-filter provides higher reliability for users who dictate long-form content, podcasts, interviews, or presentations.

## How It Works
1. **System Prompt Specialization:** The mode's prompt instructs the chat model: *"Remove filler words such as um, uh, like, you know, I mean, sort of, and basically. Do not rephrase sentences. Preserve all substantive content."*
2. **Hardcoded Post-Filter:** After the chat model returns text, a deterministic regex-based pass removes any remaining filler tokens that the model missed. This acts as a safety net.
3. **Context Preservation:** The filter only removes tokens when they appear as standalone interjections, not when they are integral to meaning (e.g., "I like pizza" is safe; "Like, I was saying" has the first "Like" removed).

### Examples
| Raw Dictation | Filler-Removal Output |
|---------------|----------------------|
| "So, um, we need to, uh, finalize the Q3 budget by Friday." | "So, we need to finalize the Q3 budget by Friday." |
| "You know, I think the, like, main issue is latency." | "I think the main issue is latency." |
| "It's sort of a hybrid approach, basically." | "It's a hybrid approach." |

## Integration
- Appears alongside existing modes (Standard, Formal, Raw, Debug) in the mode selector.
- Can be combined with the **Personal Dictionary** to ensure filler removal does not accidentally strip protected terms.

## User Benefit
- Produces instantly usable transcripts for meetings, content creation, and documentation.
- Reduces manual editing more aggressively than the generic Formal mode.
