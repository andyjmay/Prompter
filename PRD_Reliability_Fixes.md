# Prompter Reliability & Correctness PRD
**Version:** 1.0  
**Date:** 2026-05-25  
**Status:** Draft — ready for estimation  

---

## 1. Objective

Address the critical bugs, threading hazards, data-loss risks, and safeguard misfires identified in the 2025-05-25 full-codebase review. The goal is to eliminate crashes, prevent system-wide keyboard freezes, stop valid formatted text from being discarded, and harden the configuration / model-lifecycle layers against corruption and hangs.

---

## 2. Scope

| In Scope | Out of Scope |
|----------|--------------|
| All High-severity findings (system freeze, data loss, hang, crash, corrupted templates, math bug) | New product features (e.g., new formatting modes, new UI panels) |
| Medium-severity findings that directly relate to the High items (lifetime, disposal, threading, cancellation) | Cross-platform support |
| Guard/safeguard redesign (`RejectIfHallucinated`, `StripTrailingArtifacts`) | `.sln` migration (repo intentionally uses `.slnx`) |
| Config I/O locking, migration robustness, null guards | Major UX redesign |
| Eval-scorer math fix (`WordF1Scorer`) | Changing the installer technology |

---

## 3. Work Streams

### Stream A — Hotkey & Input Injection Safety *(P0 — System Stability)*
**Goal:** Remove the risk of a system-wide keyboard freeze and clean up the low-level hook lifecycle.

#### A1. Remove lock-held-during-invocation in `HotkeyService`
- **Requirement:** `HookCallback` must **release** `_stateLock` before raising `RecordingStarted?.Invoke()` and `RecordingStopped?.Invoke()`.
- **Rationale:** `WH_KEYBOARD_LL` runs on a system-critical thread; any blocking inside the callback stalls keyboard input for the entire OS.
- **Acceptance Criteria:**
  1. `HookCallback` no longer invokes event handlers while holding `_stateLock`.
  2. Existing `StartReleasePolling` / `StopReleasePolling` state transitions remain correct (unit-testable via a test seam or manual QA).
  3. No regression in minimum-hold-time (300ms) or release-detection behavior.

#### A2. Fix `HotkeyService` disposal / shutdown
- **Requirement:** `HotkeyService` must unregister its low-level keyboard hook on clean shutdown.
- **Current Gap:** The service implements `IAsyncDisposable` but not `IDisposable`; MS.DI’s `ServiceProvider` never calls `IAsyncDisposable.DisposeAsync()` on singletons.
- **Implementation:**
  - Add `IDisposable` implementation that calls `Unregister()` synchronously (or `DisposeAsync().GetAwaiter().GetResult()` if unavoidable).
  - Alternatively, dispose the service manually in `App.OnExit` before the service provider is disposed.
- **Acceptance Criteria:**
  1. After a normal app exit, `SetWindowsHookEx` handle is released (verifiable with a debug assertion or via Spy++ if manual).
  2. No `ObjectDisposedException` from the old polling task when a new recording starts (see A3).

#### A3. Fix CancellationTokenSource dispose race in `StartReleasePolling`
- **Requirement:** Do not dispose the old `_pollCts` until the old polling task has terminated or is known to have exited its loop.
- **Acceptance Criteria:**
  1. `_pollCts.Dispose()` only happens after awaiting `_pollTask` (with a short timeout) or after the task is observed complete.
  2. No unobserved `ObjectDisposedException` from the polling task.

---

### Stream B — Text Formatter Safeguards Redesign *(P0 — Core Product Value)*
**Goal:** Stop the formatter from throwing away its own correct output.

#### B1. Redesign `RejectIfHallucinated` to permit spelling correction
- **Problem:** Exact-word overlap between raw and formatted text penalizes the model for fixing typos, which is the formatter’s primary purpose.
- **Requirement:** The safeguard must accept outputs where words are corrected, not just preserved verbatim.
- **Proposed Direction (to be validated):**
  - Option A: **Fuzzy/semantic overlap** — Use Levenshtein distance or substring containment instead of exact token set intersection.
  - Option B: **Lower the threshold dynamically** when `rawSet` contains tokens that are not dictionary words (indicating probable misspellings).
  - Option C: **Preserve-ratio gate only** when the raw text is already well-formed (high confidence); otherwise skip the safeguard entirely.
- **Acceptance Criteria:**
  1. A unit test with raw `"teh quikc brown fxo"` and formatted `"The quick brown fox."` must **pass** (not be rejected).
  2. A unit test with raw `"Hello world"` and formatted `"1. Hello world\n2. How are you?\n3. ..."` must still **reject** (true hallucination).
  3. The safeguard must not reject outputs that differ only in capitalization, punctuation, or corrected spelling.

#### B2. Fix `StripTrailingArtifactsByRawAlignment` negative-index bug
- **Problem:** When `matchedTrailingWords == 0` and `resultWords.Length <= 6`, `resultIdx` becomes negative, causing the slice to yield the **entire** result array. If that result contains `?` or `...`, the whole formatted text is discarded.
- **Requirement:** Clamp `resultIdx` to a non-negative value, or skip the trailing-artifact strip entirely when no trailing words match.
- **Acceptance Criteria:**
  1. A unit test with raw `"hello"` and formatted `"Hello?"` (or any short result with punctuation) must return the formatted text unchanged.
  2. The existing unit tests for legitimate extra words (`StripTrailingArtifacts_LeavesLegitimateExtraWords`) still pass.

#### B3. Harden `StripTrailingArtifacts` against punctuation-bearing artifacts
- **Problem:** The `EndsWith` check uses `searchFragment` built from raw words, which contain no punctuation. If the model appends `"Would you like help?"`, the `?` prevents the `EndsWith` match.
- **Requirement:** Normalize punctuation before the `EndsWith` test, or compare token-by-token with punctuation stripped.
- **Acceptance Criteria:**
  1. A unit test where the model appends a trailing question with punctuation (e.g., `"... Would you like help?"`) must have the artifact stripped.
  2. Legitimate user content that ends with a question must **not** be stripped.

---

### Stream C — Model Lifecycle & Initialization Hardening *(P0 — Reliability)*
**Goal:** Eliminate initialization hangs, use-after-dispose races, and idle-unload mid-inference.

#### C1. Fix `FoundryLocalManagerAccessor` initialization hang
- **Problem:** If `FoundryLocalManager.CreateAsync` or `DownloadAndRegisterEpsAsync` throws, `_initTcs` is never completed (success or exception). All callers awaiting `InitializationCompleted` hang forever.
- **Requirement:** Wrap the initialization body in `try/catch/finally` and call `_initTcs.TrySetException(ex)` on failure.
- **Acceptance Criteria:**
  1. If `CreateAsync` throws, `InitializationCompleted` transitions to **faulted** within a reasonable time.
  2. `ModelCatalogService` methods that await `InitializationCompleted` surface the exception to their caller (not hang).

#### C2. Fix `FoundryLocalManagerAccessor.DisposeAsync` for re-initialization
- **Problem:** `DisposeAsync` disposes `_initLock` and the manager, but never resets `_initialized` to `false`. A subsequent `InitializeAsync` (e.g., after power resume) hits `if (_initialized) return;` and does nothing.
- **Requirement:** Reset `_initialized = false` before returning from `DisposeAsync`. Dispose `_initLock` safely (only if not currently held by another caller).
- **Acceptance Criteria:**
  1. After `DisposeAsync` completes, a new call to `InitializeAsync` re-initializes the manager from scratch.
  2. No `ObjectDisposedException` from a concurrent `InitializeAsync` during disposal.

#### C3. Fix `WhisperNetTranscriptionProvider` use-after-dispose race
- **Problem:** `TranscribeAsync` releases `_lock` after `LoadAsync`, then continues using `_factory` while `UnloadAsync` can acquire the same lock and dispose it.
- **Requirement:** Keep `_lock` held (or use a read-count / reference-count pattern) for the duration of `processor.ProcessAsync`.
- **Acceptance Criteria:**
  1. `UnloadAsync` cannot dispose the factory while `TranscribeAsync` is actively processing.
  2. `Dispose()` on the provider acquires `_lock` before cleaning up resources.

#### C4. Prevent idle unload mid-chat inference
- **Problem:** `GetChatClientAsync()` returns the chat client without holding `_loadSemaphore`. The idle timer can unload the model while `TextFormatter.CleanupAsync` is inside `chatClient.CompleteAsync`.
- **Requirement:** Extend the idle-timer logic to consider an **in-flight inference** flag, or acquire `_loadSemaphore` during `CompleteAsync`.
- **Acceptance Criteria:**
  1. If a chat completion is in progress when the idle timer fires, the model is **not** unloaded until completion finishes.
  2. The idle timer still unloads the model after the completion finishes and the TTL expires.

#### C5. Pass `CancellationToken` into `EnsureModelsLoadedAsync`
- **Requirement:** All model-loading entry points (`EnsureModelsLoadedAsync`, `ModelCatalogService` methods) must accept and honor a `CancellationToken`.
- **Acceptance Criteria:**
  1. A cancelled token causes `OperationCanceledException` to propagate within a reasonable time, even if a model download is in progress.
  2. `PipelineProcessor` links its processing timeout token to the model-load call.

---

### Stream D — Configuration I/O & Migration Robustness *(P0 — Data Integrity)*
**Goal:** Prevent config corruption, migration misfires, and null-reference crashes.

#### D1. Add file-level locking / atomic writes to `ConfigService`
- **Problem:** `_cacheLock` protects the in-memory cache but not the disk file. Concurrent writes from multiple threads/processes can interleave bytes.
- **Requirement:**
  - Option A: Write to a temp file (`config.json.tmp`) and atomically `File.Move` over the target.
  - Option B: Use a cross-process mutex (e.g., `Mutex` with a known name) for the brief write window.
- **Acceptance Criteria:**
  1. Two simultaneous `SaveAsync` calls produce a valid JSON file (not interleaved bytes).
  2. A crash during write does not leave a permanently corrupted `config.json` (temp-file pattern ensures the old file survives).

#### D2. Fix migration `TryGetProperty` case-sensitivity mismatch
- **Problem:** `JsonSerializerOptions.PropertyNameCaseInsensitive = true` makes deserialization case-insensitive, but `JsonElement.TryGetProperty("Version")` is case-sensitive. A lowercase `"version"` causes all migrations to run unconditionally.
- **Requirement:** Use a case-insensitive lookup for the raw version (e.g., enumerate properties and compare with `StringComparison.OrdinalIgnoreCase`, or deserialize into a small DTO first).
- **Acceptance Criteria:**
  1. A config file containing `"version": 4` (lowercase) is deserialized correctly and **does not** trigger any migration.
  2. Existing exact-case configs continue to work unchanged.

#### D3. Null-guard `Modes` in the final migration return
- **Requirement:** Add `?? new()` for `Modes` in the final `Migrate` return expression, matching the guards for `RecordingOverlay`, `PreviewToast`, etc.
- **Acceptance Criteria:**
  1. A hand-edited config with `"Modes": null` loads successfully and produces an empty list (not `NullReferenceException`).
  2. Unit test `Load_EnsuresNestedObjectsNotNull` is updated to assert `Modes != null`.

#### D4. Add `[JsonPropertyName]` to all serialized model properties
- **Requirement:** Add `[JsonPropertyName("...")]` to every property in `AppConfig`, `ModeConfig`, `Snippet`, `DictionaryEntry`, and any other config DTO.
- **Acceptance Criteria:**
  1. A property rename in C# does not change the serialized JSON key.
  2. All existing migration string literals that read raw JSON still match the explicit attribute values.

---

### Stream E — UI Threading & Crash Fixes *(P1 — User-Facing Stability)*
**Goal:** Eliminate crashes during model download, settings navigation, and comparison details.

#### E1. Fix `CustomModelManagerWindow` UI re-enable on wrong thread
- **Problem:** `SetUIEnabled(true)` is called in `finally` blocks after `await _hfService.DownloadAsync` without `Dispatcher.Invoke`.
- **Requirement:** Marshal the UI re-enable call to the dispatcher thread.
- **Acceptance Criteria:**
  1. Completing a model download (success or failure) does not throw `InvalidOperationException`.

#### E2. Fix `ModelTestingWindow` `async void` exception crashes
- **Problem:** `WhisperModelComboBox_SelectionChanged` and `ChatModelComboBox_SelectionChanged` are `async void` with no `try/catch`.
- **Requirement:** Wrap the handler bodies in `try/catch` and log the exception (do not crash the app).
- **Acceptance Criteria:**
  1. If `IsModelCachedAsync` throws, the exception is swallowed and logged; the app stays alive.

#### E3. Fix `ViewDetails_Click` wrong Whisper model restoration
- **Problem:** Uses `item.Alias` (chat alias) instead of `item.WhisperAlias`.
- **Requirement:** Change to `item.WhisperAlias`.
- **Acceptance Criteria:**
  1. Clicking “View Details” on a comparison row restores the exact Whisper model that was used for that row.

#### E4. Fix corrupted ONNX chat templates in `WriteInferenceModelJsonTemplate`
- **Problem:** Qwen, Llama, and fallback ChatML templates contain the Chinese characters `在这` after `{content}`.
- **Requirement:** Remove the stray characters so the template reads `"<|im_start|>system\n{content}\n"` (or the correct format for each model family).
- **Acceptance Criteria:**
  1. Written `inference_model.json` files contain only valid, language-appropriate template strings.

#### E5. Fix null `CustomChatModelPath` crash in Settings
- **Problem:** `Path.GetFileName(_config.CustomChatModelPath)` throws `ArgumentNullException` when the config property is null.
- **Requirement:** Guard with `!string.IsNullOrEmpty(...)` before calling `Path.GetFileName`.
- **Acceptance Criteria:**
  1. Opening Settings with a legacy config that lacks `CustomChatModelPath` does not crash.

#### E6. Add dispatcher guards / cancellation to Settings fire-and-forget tasks
- **Requirement:** Introduce a `CancellationTokenSource` field in `SettingsWindow`, cancel it on `OnClosing`, and pass the token into `PopulateChatModelComboBoxAsync`, `PopulateWhisperModelComboBoxAsync`, `RefreshModelsDashboardAsync`, and `LoadLogsAsync`. Dispatcher callbacks must check `IsLoaded` before touching UI.
- **Acceptance Criteria:**
  1. Closing Settings immediately after opening it does not produce `InvalidOperationException` from delayed dispatcher posts.

---

### Stream F — Eval / Scoring Correctness *(P1 — Metrics Integrity)*
**Goal:** Fix the mathematically invalid F1 score and improve scoring fidelity.

#### F1. Fix `WordF1Scorer` duplicate-counting bug
- **Problem:** `matches` counts duplicate actual words against a deduplicated expected set, so F1 can exceed 1.0.
- **Requirement:** Count matches as the size of the **intersection** of the two sets (or use a frequency-limited bag-of-words). F1 must be bounded to [0, 1].
- **Acceptance Criteria:**
  1. `actual="a a a"`, `expected="a"` yields `recall = 1.0`, `precision = 1.0`, `F1 = 1.0`.
  2. `actual="a b"`, `expected="a c"` yields `recall = 0.5`, `precision = 0.5`, `F1 = 0.5`.
  3. `ConsoleReporter` no longer prints scores > 100%.

#### F2. Add punctuation / case sensitivity to formatting scorer
- **Requirement:** `FormattingScorer` should weight punctuation and capitalization mismatches, not just word identity. This is a **design decision**—the PRD does not mandate a specific algorithm, but the current pure bag-of-words approach is insufficient.
- **Suggested Direction:** Compute a secondary metric (e.g., normalized Levenshtein distance on the full string, or a punctuation-preservation ratio) and combine it with the word-F1 score.
- **Acceptance Criteria:**
  1. A formatted result missing all commas and periods scores **lower** than one that preserves them.
  2. The scorer still returns a value in [0, 1].

---

### Stream G — Testing Hardening *(P1 — Regression Prevention)*
**Goal:** Close coverage gaps for critical paths and fix fake/test discrepancies.

#### G1. Add `PushTemporaryConfig` / `PopTemporaryConfig` tests
- **Requirement:** Unit tests covering nested push/pop, disposal safety, and stack unwinding.

#### G2. Fix `FakeFileLogger.GetRecentLogs` order
- **Requirement:** Return `most-recent-first` to match the real `FileLogger`.

#### G3. Add cancellation / progress / edge-case tests to `PipelineProcessorTests`
- **Requirement:** Tests for:
  - Unknown `modeId` (null `ModeConfig`).
  - `ChatModelId == "none"`.
  - Combined dictionary + spoken punctuation in one run.
  - `IProgress<T>` callback verification.
  - `CancellationToken` propagation (e.g., cancel during `EnsureModelsLoadedAsync`).

#### G4. Add integrity validation to `WhisperSmokeTests`
- **Requirement:** Validate file size or a known checksum after downloading `ggml-tiny.bin`. Re-download if the file is incomplete or corrupted.

#### G5. Add `FileLogger` 7-day purge test
- **Requirement:** Write an old log file, run the purge, assert it is deleted; write a recent log file, assert it is preserved.

#### G6. Fix `ConfigServiceTests` test names
- **Requirement:** Rename `MigratesToV7` tests to `MigratesToV12` to match assertions.

---

## 4. Prioritization

| Priority | Streams | Rationale |
|----------|---------|-----------|
| **P0** | A, B, C, D | System freeze, data loss, hangs, corrupted output. These are user-visible and can make the app unusable. |
| **P1** | E, F, G | Crashes in secondary UI, bad metrics, missing tests. Important but not system-crippling. |
| **P2** | Remaining Low items from review | Performance micro-optimizations (`SnippetMatcher` array lookup), dead-code cleanup (`SpokenPunctuationProcessor` regex), defensive improvements (`OverlayPositioner` DPI). |

---

## 5. Non-Functional Requirements

1. **No warnings.** `TreatWarningsAsErrors` is enabled; every change must build with zero warnings.
2. **No cross-platform logic.** Changes must remain Windows-only WPF with existing P/Invoke patterns.
3. **Backward compatibility.** Config migration must still load configs from Version 1 through 12 without data loss.
4. **Test coverage.** Every P0 fix must include a new or updated unit test that fails before the fix and passes after.
5. **No `.sln` file.** Continue using `Prompter.slnx`.

---

## 6. Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Safeguard redesign (B1) accidentally lets real hallucinations through. | Add a suite of unit tests for both true positives (reject) and true negatives (accept) before changing the algorithm. Run the existing `Prompter.Eval` smoke suite to verify no regression. |
| Model-lifecycle locking changes (C3, C4) introduce deadlocks. | Use a timeout on all semaphore waits (e.g., `WaitAsync(TimeSpan)`) and log any timeout as a warning. Review with a second engineer. |
| Config file locking (D1) breaks existing single-process assumption or adds complexity. | Use the atomic temp-file rename pattern; it does not require cross-process mutexes and is simple to reason about. |
| UI threading fixes (E) are hard to reproduce in unit tests. | Add integration-style tests using the existing `DispatcherFixture` or manual QA checklist for download / settings flows. |

---

## 7. Acceptance Criteria (Overall)

1. `dotnet build Prompter.slnx` succeeds with **zero** warnings.
2. `dotnet test Prompter.Tests/Prompter.Tests.csproj` passes with **zero** failures.
3. `dotnet run --project Prompter.Eval/Prompter.Eval.csproj -- --smoke` runs to completion with no unhandled exceptions.
4. Manual QA checklist (to be executed by the developer):
   - Trigger a 10-second recording, verify it completes and text is injected.
   - Open Settings, close it immediately, verify no crash.
   - Open Model Testing, switch Whisper and Chat combo boxes repeatedly, verify no crash.
   - Click “View Details” on a comparison row, verify the correct Whisper model is selected.
   - Simulate a corrupted `config.json` (write invalid bytes), verify the app does not hang on startup.

---

## 8. Open Questions

1. **B1 — Safeguard redesign:** Should we adopt fuzzy string similarity (Levenshtein), semantic embedding similarity, or a simpler rule-based approach? Need a spike or prototype before committing.
2. **F2 — Formatting scorer:** What is the desired balance between word accuracy and punctuation/case fidelity? Need input from the team that owns the eval dataset.
3. **C4 — Idle unload mid-inference:** Should we use a simple `Interlocked` flag, or refactor `ModelManager` to use a reader-writer pattern (many concurrent inferences, one exclusive unload)?

---

*End of PRD*
