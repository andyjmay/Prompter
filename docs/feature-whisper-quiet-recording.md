# Whisper / Quiet-Space Recording

## Purpose
Improve transcription accuracy when the user speaks at very low volume (whispering) in shared or quiet environments such as libraries, open offices, or late-night home use.

## Problem Solved
Whisper's accuracy degrades significantly with low-amplitude input. Standard microphone gain may not boost whispered speech enough, while excessive gain introduces noise and clipping. Users in shared spaces avoid dictation because they cannot speak at normal volume.

## How It Works

### Audio Preprocessing Pipeline
1. **Input Gain Boost:** Apply a configurable digital gain multiplier (e.g., 1.5×–3×) to the microphone input stream before it reaches Whisper.
2. **Noise Gate:** A threshold-based gate suppresses audio below a certain dB level, preventing ambient keyboard noise or HVAC from being amplified along with the whisper.
3. **High-Pass Filter:** Remove low-frequency rumble (< 80 Hz) that becomes audible after gain boosting.
4. **AGC (Automatic Gain Control) Window:** Instead of a fixed boost, apply a slow-acting AGC that normalizes the amplitude of the *recorded segment* to a target RMS level.

### User-Facing Controls
- **Profile Selector** in Settings:
  - **Normal** (default): No special processing.
  - **Quiet / Whisper:** Enables gain boost + noise gate + high-pass filter.
  - **Noisy Environment:** Enables aggressive noise suppression without gain boost.
- **Mic Sensitivity Slider:** Allows fine-tuning of the noise-gate threshold for individual microphones.

## Constraints
- Local Whisper models (especially `tiny` and `base`) are trained primarily on normal-conversation audio. Whispering remains a harder task regardless of preprocessing. Accuracy will not match cloud models trained explicitly on whispered speech.
- Aggressive gain boosting can cause clipping on plosives ("p", "t"). A soft clipper / limiter should be the final stage in the preprocessing chain.

## Integration
- Settings live in the **Audio** section of the Settings window.
- The **Recording Overlay** can display a visual indicator when the Quiet profile is active (e.g., a muted / low-volume icon).

## User Benefit
- Makes Prompter usable in shared spaces where speaking aloud is socially inappropriate.
- Expands the addressable use cases to libraries, co-working spaces, public transit, and shared bedrooms.
