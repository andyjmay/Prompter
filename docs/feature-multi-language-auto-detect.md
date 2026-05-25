# Multi-Language Auto-Detection

## Purpose
Remove the single-language lock so that users can dictate in any language Whisper supports, or even switch languages mid-session, without manually changing a config setting.

## Problem Solved
Prompter currently stores a single `Language` string (default `"en"`) in `AppConfig`. Whisper is forced to decode in that language, which causes catastrophic failures when the user switches languages. Multilingual users, language learners, and non-English speakers are forced to constantly toggle settings.

## How It Works
1. **Remove Forced Language:** When `Language` is set to `"auto"` (new default), Prompter does not pass a `language` parameter to Whisper. Whisper's built-in language detection determines the dominant language of each utterance automatically.
2. **Language Hint (Optional):** For users who *mostly* speak one language but occasionally use loanwords, keep an optional **Primary Language** hint. This is passed as a soft preference to Whisper, not a hard constraint.
3. **UI Updates:**
   - The Settings language dropdown gains an **Auto-Detect** option at the top.
   - The `Language` config field remains backward-compatible: existing `"en"`, `"es"`, etc. values still work as hard constraints for users who want them.

### Supported Languages
Whisper `tiny`–`medium` supports ~99 languages. The feature is automatically available for all of them without additional model downloads, provided the chosen Whisper model is multilingual (all official OpenAI Whisper checkpoints are).

## Edge Cases
- **Code-Switching:** If a user dictates a sentence with two languages, Whisper may output the entire text in the detected dominant language or produce mixed output. This is an upstream model limitation; there is no planned custom polyglot model.
- **Formatter Mismatch:** The chat formatter's system prompt is currently in English. For non-English transcripts, the prompt should either:
  - Be translated dynamically, or
  - Be written language-agnostically (e.g., "Fix spelling and punctuation only.")

## Integration
- One-line change in `TranscriptionService` / `WhisperNetTranscriptionProvider` to conditionally omit the language parameter.
- UI update in `SettingsWindow.xaml` and `SettingsWindow.xaml.cs`.

## User Benefit
- Zero-configuration multilingual support.
- Matches the out-of-box experience of cloud competitors (Wispr Flow, Google Voice Typing).
- Expands the user base to non-English markets without dedicated localization work.
