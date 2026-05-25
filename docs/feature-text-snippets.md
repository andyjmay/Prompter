# Text Snippets (Voice Shortcuts)

## Purpose
Let users define voice-triggered macros that expand into pre-written, formatted blocks of text. A single spoken cue inserts a full paragraph, URL, email template, or code block at the cursor.

## Problem Solved
Repetitive typing is tedious even when dictation is fast. Professionals re-use the same text hundreds of times a day: scheduling links, support intros, legal disclaimers, commit message templates, Slack standup updates. Snippets make voice input faster than typing not just for novel prose, but for boilerplate too.

## How It Works
1. The user creates a **Snippet** with:
   - **Trigger phrase** (e.g., "Calendar link", "Support intro", "My address")
   - **Expansion text** (rich text, including newlines, markdown, or code)
2. After Whisper transcribes the raw audio, Prompter checks if the transcription (minus punctuation and case) closely matches any trigger phrase.
3. If a match is found, the snippet text is injected directly into the target application, **bypassing** the chat formatter entirely to avoid unintended rewrites.
4. If no match is found, the pipeline proceeds to formatting as usual.

### Example Snippets
| Trigger | Expansion |
|---------|-----------|
| "Calendar link" | `You can book a 30-minute call with me here: calendly.com/username` |
| "Support intro" | `Hi! Thanks for reaching out. I'm happy to help.` |
| "Lorem ipsum" | Full paragraph of placeholder text |
| "Sign-off" | `Best,\n\n[Full Name]\n[Title]\n[Company]` |

## Trigger Matching Rules
- Case-insensitive.
- Fuzzy match within a Levenshtein distance of 1–2 characters to tolerate Whisper errors.
- Only activate if the entire transcription consists of the trigger (or the trigger plus negligible filler). Do not replace snippets inside larger dictated sentences.

## User Benefit
- Turns Prompter into a programmable voice macro engine.
- Reduces cognitive load: users don't have to remember exact URLs or paragraphs.
- Particularly valuable for **customer support**, **sales**, **medical documentation**, and **developers**.
