# AGENTS.md — Prompter

Compact guidance for future OpenCode sessions in this repo.

## Project basics
- **Multi-project .NET 10 WPF desktop app solution** containing four projects: the WPF main app (`Prompter`), an xUnit-based unit test suite (`Prompter.Tests`), an offline quality evaluation benchmark CLI tool (`Prompter.Eval`), and a WiX-based MSI installer (`Prompter.Setup`).
- Solution uses the new XML format: `Prompter.slnx` (not `.sln`).
- Target: `net10.0-windows10.0.26100.0`; RIDs: `win-x64`, `win-arm64`.
- `TreatWarningsAsErrors` is **enabled** — warnings are build errors.
- `Nullable` and `ImplicitUsings` are enabled.
- **Configuration & Logs:**
  - Config file: `%LocalAppData%\Prompter\config.json`
  - Logs directory: `%LocalAppData%\Prompter\logs\prompter-debug-YYYYMMDD.txt` (with a 7-day retention policy)

## Build & run
```bash
# Build the entire solution
dotnet build Prompter.slnx

# Run the WPF app
dotnet run --project Prompter/Prompter.csproj

# Run unit tests
dotnet test Prompter.Tests/Prompter.Tests.csproj

# Run offline benchmark evaluation
dotnet run --project Prompter.Eval/Prompter.Eval.csproj [--smoke | --full]

# Build the MSI installer (x64, self-contained)
dotnet build Prompter.Setup/Prompter.Setup.wixproj -c Release
```

## Architecture
- **Entry point:** `App.xaml.cs` builds the DI container (`Microsoft.Extensions.DependencyInjection`), enforces single-instance via a named `Mutex` (`Prompter_SingleInstance_Mutex`), wires global exception handlers, and initializes the tray UI.
- **Layers:** `Services/`, `ViewModels/`, `Views/`, `Models/`.
- **Core flow:** `HotkeyService` (global keyboard hook) → `AppEventCoordinator` → `PipelineOrchestrator` → `PipelineProcessor` (coordinates raw text, snippets, dictionary, spoken punctuation, formatting/safeguards) → `InputInjectorService` / `ClipboardService`.
- **UI Notifications:** `RecordingOverlay` (overlay UI while recording) and `PreviewToast` (non-intrusive preview UI for final output) are managed via `RecordingUIManager`. Balloon tips from system tray are coordinated by `AppEventCoordinator`.
- **Audio Feedback:** Synthesized sine-wave chimes are played via `AudioFeedbackService` (using `NAudio`) to indicate recording start/stop.

## Critical implementation details
- **Low-level Keyboard Hook & Release Polling:** `HotkeyService` uses `SetWindowsHookEx` with `WH_KEYBOARD_LL` to hook keyboard events. On match, it starts a high-frequency background task (`StartReleasePolling`) calling `GetAsyncKeyState` every 50ms to detect release.
- **Minimum Hold Time:** Hotkey must be held for >= 300ms. If released earlier, the recording continues polling and stops at the 300ms mark.
- **Maximum Recording Duration:** Hardcoded to 5 minutes max duration inside `PipelineOrchestrator`.
- **Snippet Matching:** Intercepts transcription immediately; if a snippet keyword is matched, it returns the expansion directly and exits the processing pipeline.
- **Personal Dictionary & Spoken Punctuation:** If snippets are not matched, dictionary word replacements and spoken punctuation translations are performed.
- **Input Injection & Clipboard Integration:** Outputs text via `InputInjectorService` using P/Invoke `SendInput` for direct typing. If text exceeds `PasteThresholdCharacters` (default 150) and `UseClipboardPaste` is enabled, it copies to clipboard, sends Ctrl+V paste keypresses, and restores the original clipboard backup.
- **Foundry Local (on-device ML):** Uses `Microsoft.AI.Foundry.Local.WinML`. `FoundryLocalManagerAccessor` wraps the `FoundryLocalManager` singleton; it must be initialized before any model access. Models load lazily and auto-unload after an idle TTL (default 5 min).
- **Model Download Progress:** Models (Whisper and Chat) are downloaded on first access, with progress reported to the user via system tray balloons.
- **`Betalgo.Ranul.OpenAI` is local, not cloud:** The OpenAI SDK types (`ChatMessage`, `CompleteChatAsync`) are used through Foundry Local’s chat client abstraction (`model.GetChatClientAsync()`). Do not assume cloud API usage.
- **Custom GGUF Chat Provider (LlamaSharp):** Users can opt into local GGUF chat models via `UseCustomChat` + `CustomChatModelPath`, using `LLamaSharp` with a Vulkan/CPU backend. The `IChatClient` abstraction hides the difference between Foundry (`FoundryChatClient`) and LlamaSharp (`LlamaSharpChatClient`) from `TextFormatter`.
- **Formatting Modes & Safeguards:** Supports `Standard`, `Formal`, `Raw`, `Debug`, and `Code` (developer syntax-aware) formatting. An optional add-on ("Clean") removes filler words dynamically. An optional "List" mode formats dictated text into markdown lists. The pipeline includes `RejectIfHallucinated`, `StripOutputWrappers`, `StripTrailingArtifactsByRawAlignment`, and `StripSpecialTokens` to protect against chat model hallucinations or layout prefixes (e.g., reverting to raw text if preservation ratio is low).
- **Power awareness:** `App.xaml.cs` handles `PowerModeChanged` to unload models on suspend and re-initialize on resume.
- **Audio/mic contention is a known failure mode:** `PipelineOrchestrator` shows a `MessageBox` if `StartRecording` fails because the microphone is in use.

## Testing & Evaluation
- **xUnit Test Suite (`Prompter.Tests`):** Exhaustive unit and integration tests covering configuration, exceptions, dictionary, spoken punctuation, text formatting, and safeguards.
- **Offline Benchmark Runner (`Prompter.Eval`):** Grid/smoke evaluation CLI checking whisper transcription accuracy and text formatting composite fidelity against a preset local dataset.
- **Model Testing Dashboard:** Real-time visual testing and evaluation panel (`ModelTestingWindow`) in the WPF application showing metrics and detailed diagnostic output.

## What to avoid
- Do not create a `.sln` file — the repo intentionally uses `.slnx`.
- Do not introduce warnings; they will break the build.
- Do not add cross-platform logic — this is Windows-only WPF with heavy P/Invoke.
