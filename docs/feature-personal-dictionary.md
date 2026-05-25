# Personal Dictionary / Custom Vocabulary

## Purpose
Teach Prompter the user's unique words—names, product terms, jargon, neologisms, and unconventional spellings—so they are transcribed and formatted correctly every time.

## Problem Solved
Whisper and local chat models are trained on general corpora. They frequently hallucinate spellings of proper nouns (e.g., "Vercel" → "Versal", "Nguyen" → "Win"), technical acronyms, or invented brand names. Manually correcting these after every dictation breaks flow.

## How It Works
1. The user maintains an editable word list via Settings.
2. Before the chat model formats the text, the raw Whisper output is scanned. Any fuzzy match to a dictionary entry is locked to the exact user-provided spelling.
3. The system prompt sent to the chat model includes an appended instruction: *"Preserve the exact spelling of the following words: Vercel, Supabase, Nguyen, SaaS, Caltrain..."*

### Example Workflow
1. User dictates: "I talked to Nguyen about the SaaS roadmap."
2. Whisper outputs: "I talked to win about the sass roadmap."
3. Dictionary pass forces: "I talked to Nguyen about the SaaS roadmap."
4. Chat formatter receives the corrected text and is instructed not to alter those spellings.

## Scope
- **Per-user, per-device** (local config). Future cloud sync is out of scope for the initial implementation.
- Entries can include multi-word phrases ("Santa Clara Valley Transportation Authority").

## User Benefit
- Eliminates the most common source of post-dictation editing.
- Makes the tool viable for professionals in specialized fields (medicine, law, engineering, developer tooling).
