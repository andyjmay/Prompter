# Spoken Punctuation Commands

## Purpose
Allow users to dictate punctuation marks, special characters, and structural directives verbally instead of relying solely on the chat model to infer them from tone and pauses.

## Problem Solved
Local chat-formatting models often miss subtle pauses or fail to insert the exact punctuation the speaker intended—especially in complex sentences or technical writing. Cloud-based dictation tools handle this by training a dedicated punctuation model; locally, we solve it by letting the user explicitly dictate punctuation.

## How It Works
After Whisper returns raw transcription and before the text is sent to the chat formatter, a lightweight text-replacement pass scans for known spoken tokens and replaces them with their character equivalents.

### Examples
| Spoken Token | Inserted Character |
|--------------|-------------------|
| "comma" | `,` |
| "period" | `.` |
| "question mark" | `?` |
| "exclamation mark" / "exclamation point" | `!` |
| "new line" / "new paragraph" | `\n` |
| "open quote" | `"` |
| "close quote" | `"` |
| "colon" | `:` |
| "semicolon" | `;` |
| "dash" | `-` |
| "ellipsis" | `...` |
| "tab" | `\t` |
| "at sign" | `@` |
| "hashtag" | `#` |

## Edge Cases
- Ambiguity: "I want a period piece" should not become "I want a . piece." Tokens are only substituted when standing alone (surrounded by whitespace or punctuation).
- Case-insensitive matching with context awareness.
- Undoing accidental substitutions is handled by the **Backtracking** feature if implemented.

## User Benefit
- Precision control over formatting without touching the keyboard.
- Reduces dependency on the chat model for punctuation inference, lowering hallucination risk.
- Works in every mode (Standard, Formal, Code, List, etc.).
