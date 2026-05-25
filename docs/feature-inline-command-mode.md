# Inline Command Mode

## Purpose
Allow users to issue editing and navigation commands verbally *while* dictating, without stopping the recording or switching applications. Commands such as "delete that," "capitalize that," "insert comma," or "undo" are interpreted as actions rather than text.

## Problem Solved
Traditional dictation is a one-way pipe: speech → text. If the user makes a mistake, sees a typo, or wants to restructure, they must stop dictating, reach for the keyboard or mouse, fix the issue, and restart. Inline commands make the voice interface bidirectional.

## How It Works

Because Prompter currently processes audio as a single monolithic block (record → stop → transcribe → format → inject), true inline commands require architectural changes. The proposed phased implementation is:

### Phase 1: Post-Recording Command Interpreter
1. After the full transcription is produced, a lightweight classifier (heuristic or small local model) scans the text for command phrases.
2. If commands are detected, the pipeline splits the transcription into **segments** separated by commands.
3. Each segment is formatted normally. Commands are translated into a sequence of `SendInput` keystrokes that operate on the already-injected text.
4. The command text itself is never injected.

**Supported Commands (Phase 1):**
| Command | Action |
|---------|--------|
| "delete that" / "scratch that" | Sends `Ctrl+Shift+Left` + `Delete` to remove the last word/phrase. |
| "delete last sentence" | Sends `Ctrl+Shift+Up` or repeated `Ctrl+Shift+Left` to remove the last sentence. |
| "capitalize that" | Selects last word and sends `Shift+F3` (or reapplies with correct casing). |
| "insert comma" / "insert period" | Sends `End` + `,` / `.` |
| "new line" / "new paragraph" | Sends `Enter` / `Enter Enter` |
| "undo" | Sends `Ctrl+Z` |

### Phase 2: Streaming / Chunked Processing
1. Audio is segmented by voice activity detection (VAD) pauses.
2. Each chunk is transcribed and injected as soon as the user pauses.
3. Commands detected in a new chunk are applied to the *previously injected* text immediately.
4. This gives near-real-time command responsiveness, matching Wispr Flow's Pro-tier behavior.

## Challenges
- **Keystroke Simulation Reliability:** `SendInput` operates on the active window. If focus shifts mid-dictation, commands may land in the wrong application. Mitigation: warn user or abort command if focus changes.
- **State Tracking:** Prompter does not currently track how much text was injected. Commands like "delete last sentence" require the app to remember the length of the last inserted block.

## User Benefit
- Closes the loop: users can perform basic editing entirely by voice.
- Reduces hand-keyboard dependency to near zero for text composition.
- Essential for **accessibility** users who cannot easily operate a keyboard.
