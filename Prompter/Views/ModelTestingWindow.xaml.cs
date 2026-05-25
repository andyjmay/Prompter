using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Collections.Generic;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views;

public class TestRunInfo : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isCompareTarget;

    public string Label { get; set; } = "";
    public string ConfigSummary { get; set; } = "";
    public string SpeedLabel { get; set; } = "";
    public string PreviewText { get; set; } = "";
    
    public string RawText { get; set; } = "";
    public string FormattedText { get; set; } = "";
    public string WhisperAlias { get; set; } = "";
    public string ChatAlias { get; set; } = "";
    public string ModeId { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    
    public double WhisperLoadSec { get; set; }
    public double WhisperRunSec { get; set; }
    public double ChatLoadSec { get; set; }
    public double ChatRunSec { get; set; }
    public double TotalSec { get; set; }
    public double WordsPerSecond { get; set; }
    public double CharactersPerSecond { get; set; }

    public bool IsCompareTarget
    {
        get => _isCompareTarget;
        set
        {
            if (_isCompareTarget != value)
            {
                _isCompareTarget = value;
                OnPropertyChanged(nameof(IsCompareTarget));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public partial class ModelTestingWindow : Window
{
    private readonly IConfigService _configService;
    private readonly IModelCatalogService _modelCatalog;
    private readonly IModelManager _modelManager;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IAudioRecorderService _audioRecorderService;
    private readonly ITextFormatter _textFormatter;
    private readonly IFileLogger _logger;
    private readonly IGgufModelStore _ggufStore;

    private AppConfig _config;
    private readonly ObservableCollection<TestRunInfo> _history = new();
    private string? _tempWavPath;
    private IRecordingSession? _recordingSession;
    private DispatcherTimer? _recordingTimer;
    private Stopwatch? _recordingStopwatch;
    private CancellationTokenSource? _pipelineCts;
    private Action<double>? _audioLevelHandler;
    private Action<Exception>? _recordingErrorHandler;

    private readonly Dictionary<string, string> _chatDisplayNameToAlias = new();
    private readonly Dictionary<string, string> _whisperDisplayNameToAlias = new();
    
    private int _runCounter = 0;

    public ModelTestingWindow(
        IConfigService configService,
        IModelCatalogService modelCatalog,
        IModelManager modelManager,
        ITranscriptionService transcriptionService,
        IAudioRecorderService audioRecorderService,
        ITextFormatter textFormatter,
        IFileLogger logger,
        IGgufModelStore ggufStore)
    {
        InitializeComponent();
        _configService = configService;
        _modelCatalog = modelCatalog;
        _modelManager = modelManager;
        _transcriptionService = transcriptionService;
        _audioRecorderService = audioRecorderService;
        _textFormatter = textFormatter;
        _logger = logger;
        _ggufStore = ggufStore;

        _config = _configService.Load();
        HistoryListView.ItemsSource = _history;

        Loaded += ModelTestingWindow_Loaded;
        Closed += ModelTestingWindow_Closed;
        
        _modelManager.ModelDownloadProgress += OnModelDownloadProgress;
    }

    private async void ModelTestingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate Dropdowns
        await PopulateModelDropdownsAsync();

        // Populate Modes Combo
        ModeComboBox.Items.Clear();
        foreach (var m in _config.Modes)
        {
            ModeComboBox.Items.Add(m.Name);
        }
        if (ModeComboBox.Items.Count > 0)
        {
            var defaultMode = _config.Modes.FirstOrDefault(m => m.Id.Equals(_config.DefaultModeId, StringComparison.OrdinalIgnoreCase));
            ModeComboBox.SelectedItem = defaultMode?.Name ?? ModeComboBox.Items[0];
        }

        UpdateWizardSteps();
    }

    private void ModelTestingWindow_Closed(object? sender, EventArgs e)
    {
        _modelManager.ModelDownloadProgress -= OnModelDownloadProgress;
        StopRecording();
        _recordingTimer?.Stop();
        _pipelineCts?.Cancel();
        _pipelineCts?.Dispose();
        _pipelineCts = null;
        CleanupTempFile();
    }


    private void CleanupTempFile()
    {
        if (_recordingSession != null)
        {
            try { _recordingSession.Dispose(); } catch { }
            _recordingSession = null;
        }
        if (!string.IsNullOrEmpty(_tempWavPath) && File.Exists(_tempWavPath))
        {
            try { File.Delete(_tempWavPath); } catch { }
            _tempWavPath = null;
        }
    }

    private async Task PopulateModelDropdownsAsync()
    {
        try
        {
            // Whisper models
            WhisperModelComboBox.Items.Clear();
            _whisperDisplayNameToAlias.Clear();
            var whispers = await _modelCatalog.ListAvailableWhisperModelsAsync();
            foreach (var (alias, displayName) in whispers)
            {
                _whisperDisplayNameToAlias[displayName] = alias;
                WhisperModelComboBox.Items.Add(displayName);
            }

            var ggmlDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prompter", "models", "ggml");
            if (Directory.Exists(ggmlDir))
            {
                var files = Directory.GetFiles(ggmlDir, "*.bin");
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    var displayName = $"Custom: {name}";
                    _whisperDisplayNameToAlias[displayName] = file;
                    WhisperModelComboBox.Items.Add(displayName);
                }
            }
            
            var currentWhisper = _config.UseCustomWhisper ? $"Custom: {Path.GetFileName(_config.CustomWhisperModelPath)}" : _config.WhisperModelId;
            var wDisp = _whisperDisplayNameToAlias.FirstOrDefault(x => x.Value == currentWhisper || Path.GetFileName(x.Value) == Path.GetFileName(currentWhisper)).Key;
            if (wDisp != null) WhisperModelComboBox.SelectedItem = wDisp;
            else if (WhisperModelComboBox.Items.Count > 0) WhisperModelComboBox.SelectedIndex = 0;

            // Chat models
            ChatModelComboBox.Items.Clear();
            _chatDisplayNameToAlias.Clear();

            // Add None option
            _chatDisplayNameToAlias["None"] = "none";
            ChatModelComboBox.Items.Add("None");

            var chats = await _modelCatalog.ListAvailableChatModelsAsync();
            foreach (var (alias, displayName, sizeDescription) in chats)
            {
                var displayNameWithSize = $"{displayName} ({sizeDescription})";
                _chatDisplayNameToAlias[displayNameWithSize] = alias;
                ChatModelComboBox.Items.Add(displayNameWithSize);
            }

            var ggufDir = _ggufStore.BaseDirectory;
            if (Directory.Exists(ggufDir))
            {
                var files = Directory.GetFiles(ggufDir, "*.gguf", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    var fileInfo = new FileInfo(file);
                    string sizeStr = fileInfo.Length >= 1024L * 1024 * 1024
                        ? $"~{fileInfo.Length / (1024.0 * 1024 * 1024):F1} GB"
                        : $"~{fileInfo.Length / (1024.0 * 1024):F0} MB";
                    var displayName = $"Custom: {name}";
                    var displayNameWithSize = $"{displayName} ({sizeStr})";
                    _chatDisplayNameToAlias[displayNameWithSize] = file;
                    ChatModelComboBox.Items.Add(displayNameWithSize);
                }
            }
            
            var currentChat = _config.UseCustomChat ? _config.CustomChatModelPath : _config.ChatModelId;
            var cDisp = _chatDisplayNameToAlias.FirstOrDefault(x => x.Value == currentChat || Path.GetFileName(x.Value) == Path.GetFileName(currentChat)).Key;
            if (cDisp != null) ChatModelComboBox.SelectedItem = cDisp;
            else if (ChatModelComboBox.Items.Count > 0) ChatModelComboBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Sandbox model dropdown population failed");
        }
    }

    private async void WhisperModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = WhisperModelComboBox.SelectedItem as string;
        if (selected == null) return;
        var alias = _whisperDisplayNameToAlias.TryGetValue(selected, out var val) ? val : selected;

        bool isCustom = selected.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase);
        if (isCustom)
        {
            WhisperModelStatus.Text = "Custom model file (Whisper.net). Ready.";
            DownloadWhisperButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            bool isCached = await _modelCatalog.IsModelCachedAsync(alias);
            if (isCached)
            {
                WhisperModelStatus.Text = "Cached locally. Ready.";
                DownloadWhisperButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                WhisperModelStatus.Text = "Not cached. Download required.";
                DownloadWhisperButton.Visibility = Visibility.Visible;
                DownloadWhisperButton.IsEnabled = true;
            }
        }
        UpdateWizardSteps();
    }

    private async void ChatModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ChatModelComboBox.SelectedItem as string;
        if (selected == null) return;
        var alias = _chatDisplayNameToAlias.TryGetValue(selected, out var val) ? val : selected;

        bool isCustom = selected.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase);
        if (alias == "none")
        {
            ChatModelStatus.Text = "No correction model selected (Skip formatting).";
            DownloadChatButton.Visibility = Visibility.Collapsed;
        }
        else if (isCustom)
        {
            ChatModelStatus.Text = "Custom GGUF model file (LlamaSharp). Ready.";
            DownloadChatButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            bool isCached = await _modelCatalog.IsModelCachedAsync(alias);
            if (isCached)
            {
                ChatModelStatus.Text = "Cached locally (DirectML GPU/NPU active). Ready.";
                DownloadChatButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                ChatModelStatus.Text = "Not cached. Download required.";
                DownloadChatButton.Visibility = Visibility.Visible;
                DownloadChatButton.IsEnabled = true;
            }
        }
        UpdateWizardSteps();
    }

    private void OnModelDownloadProgress(string alias, float progress)
    {
        Dispatcher.Invoke(() =>
        {
            var selectedWhisper = WhisperModelComboBox.SelectedItem as string;
            var selectedChat = ChatModelComboBox.SelectedItem as string;
            
            var whisperAlias = selectedWhisper != null && _whisperDisplayNameToAlias.TryGetValue(selectedWhisper, out var wa) ? wa : null;
            var chatAlias = selectedChat != null && _chatDisplayNameToAlias.TryGetValue(selectedChat, out var ca) ? ca : null;

            if (alias == whisperAlias)
            {
                WhisperProgressBar.Visibility = Visibility.Visible;
                WhisperProgressBar.Value = progress;
                WhisperModelStatus.Text = $"Downloading... {progress:F0}%";
                if (progress >= 100)
                {
                    WhisperProgressBar.Visibility = Visibility.Collapsed;
                    WhisperModelStatus.Text = "Cached locally. Ready.";
                    DownloadWhisperButton.Visibility = Visibility.Collapsed;
                }
            }
            else if (alias == chatAlias)
            {
                ChatProgressBar.Visibility = Visibility.Visible;
                ChatProgressBar.Value = progress;
                ChatModelStatus.Text = $"Downloading... {progress:F0}%";
                if (progress >= 100)
                {
                    ChatProgressBar.Visibility = Visibility.Collapsed;
                    ChatModelStatus.Text = "Cached locally. Ready.";
                    DownloadChatButton.Visibility = Visibility.Collapsed;
                }
            }
            UpdateWizardSteps();
        });
    }

    private async void DownloadWhisperButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = WhisperModelComboBox.SelectedItem as string;
        if (selected == null) return;
        var alias = _whisperDisplayNameToAlias.TryGetValue(selected, out var val) ? val : selected;

        DownloadWhisperButton.IsEnabled = false;
        WhisperProgressBar.Visibility = Visibility.Visible;
        WhisperProgressBar.Value = 0;

        try
        {
            WhisperModelStatus.Text = "Connecting to Hugging Face...";
            await _modelManager.DownloadModelAsync(alias);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Sandbox Whisper model download failed");
            WhisperModelStatus.Text = $"Download failed: {ex.Message}";
            DownloadWhisperButton.IsEnabled = true;
            WhisperProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void DownloadChatButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ChatModelComboBox.SelectedItem as string;
        if (selected == null) return;
        var alias = _chatDisplayNameToAlias.TryGetValue(selected, out var val) ? val : selected;

        DownloadChatButton.IsEnabled = false;
        ChatProgressBar.Visibility = Visibility.Visible;
        ChatProgressBar.Value = 0;

        try
        {
            ChatModelStatus.Text = "Connecting to Hugging Face...";
            await _modelManager.DownloadModelAsync(alias);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Sandbox Chat model download failed");
            ChatModelStatus.Text = $"Download failed: {ex.Message}";
            DownloadChatButton.IsEnabled = true;
            ChatProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedName = ModeComboBox.SelectedItem as string;
        if (selectedName == null) return;

        var mode = _config.Modes.FirstOrDefault(m => m.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
        if (mode == null) return;

        CleanCheckBox.IsChecked = _config.CleanEnabled;
        ListCheckBox.IsChecked = _config.ListFormattingEnabled;
        PunctuationCheckBox.IsChecked = _config.SpokenPunctuationEnabled;
        SystemPromptTextBox.Text = mode.SystemPrompt;
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (RecordButton.Tag as string == "Recording")
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        CleanupTempFile();

        try
        {
            _recordingSession = _audioRecorderService.StartRecording();
            _audioLevelHandler = (level) =>
            {
                Dispatcher.Invoke(() => AudioLevelBar.Value = Math.Clamp(level * 100, 0, 100));
            };
            _recordingErrorHandler = (ex) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StopRecording(success: false);
                    MessageBox.Show($"Recording error: {ex.Message}", "Microphone Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            };
            _recordingSession.AudioLevelAvailable += _audioLevelHandler;
            _recordingSession.RecordingError += _recordingErrorHandler;

            _recordingSession.Begin();
            _tempWavPath = _recordingSession.RecordedFilePath;

            RecordButton.Tag = "Recording";
            RecordStatusText.Text = "Recording... Click to stop";
            RecordingTimerText.Text = "00:00";
            RecordingTimerText.Visibility = Visibility.Visible;
            AudioLevelBar.Visibility = Visibility.Visible;

            _recordingStopwatch = Stopwatch.StartNew();
            _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _recordingTimer.Tick += (s, ev) =>
            {
                if (_recordingStopwatch != null)
                {
                    RecordingTimerText.Text = _recordingStopwatch.Elapsed.ToString(@"mm\:ss");
                }
            };
            _recordingTimer.Start();
            UpdateWizardSteps();
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Sandbox start recording failed");
            MessageBox.Show("Could not start microphone recording. Make sure your microphone is not in use.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StopRecording(success: false);
        }
    }

    private void StopRecording(bool success = true)
    {
        _recordingTimer?.Stop();
        _recordingTimer = null;
        _recordingStopwatch?.Stop();
        _recordingStopwatch = null;

        if (_recordingSession != null)
        {
            if (_audioLevelHandler != null) _recordingSession.AudioLevelAvailable -= _audioLevelHandler;
            if (_recordingErrorHandler != null) _recordingSession.RecordingError -= _recordingErrorHandler;
            _audioLevelHandler = null;
            _recordingErrorHandler = null;
            try { _recordingSession.StopRecording(); } catch { }
        }

        RecordButton.Tag = "Idle";
        RecordStatusText.Text = success
            ? "Audio recorded successfully. Click to record again."
            : "Recording failed. Click to try again.";
        RecordingTimerText.Visibility = Visibility.Collapsed;
        AudioLevelBar.Visibility = Visibility.Collapsed;
        UpdateWizardSteps();
    }

    private async void RunPipelineButton_Click(object sender, RoutedEventArgs e)
    {
        var isAudio = InputTabControl.SelectedIndex == 0;
        if (isAudio)
        {
            if (RecordButton.Tag as string == "Recording")
            {
                MessageBox.Show("Please stop recording before running the pipeline.", "Sandbox Pipeline", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(_tempWavPath) || !File.Exists(_tempWavPath))
            {
                MessageBox.Show("Please record some audio first.", "Sandbox Pipeline", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // UI feedback
        RunPipelineButton.IsEnabled = false;
        RunPipelineButton.Content = "⏳ Processing Sandbox Run...";
        RawOutputTextBox.Text = "Running transcription...";
        FormattedOutputTextBox.Text = "Waiting for transcription...";
        
        // Cancel and dispose any active pipeline task
        _pipelineCts?.Cancel();
        _pipelineCts?.Dispose();
        _pipelineCts = new CancellationTokenSource();
        var ct = _pipelineCts.Token;

        // Track detailed timings
        double whisperLoadSec = 0;
        double whisperRunSec = 0;
        double chatLoadSec = 0;
        double chatRunSec = 0;
        double totalSec = 0;

        var overallSw = Stopwatch.StartNew();

        try
        {
            // Selected model mappings
            var whisperSel = WhisperModelComboBox.SelectedItem as string;
            var chatSel = ChatModelComboBox.SelectedItem as string;
            
            if (string.IsNullOrEmpty(whisperSel) || string.IsNullOrEmpty(chatSel))
            {
                throw new InvalidOperationException("Please select models first.");
            }

            var whisperAlias = _whisperDisplayNameToAlias.TryGetValue(whisperSel, out var wv) ? wv : whisperSel;
            var chatAlias = _chatDisplayNameToAlias.TryGetValue(chatSel, out var cv) ? cv : chatSel;

            bool whisperIsCustom = whisperSel.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase);
            bool chatIsCustom = chatSel.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase);

            var selectedModeName = ModeComboBox.SelectedItem as string ?? "Standard";
            var baseMode = _config.Modes.FirstOrDefault(m => m.Name.Equals(selectedModeName, StringComparison.OrdinalIgnoreCase)) ?? ModeDefaults.Standard;

            // Configure temporary test config with the sandbox prompt
            var testModes = _config.Modes.ToList();
            var modeIdx = testModes.FindIndex(m => m.Id.Equals(baseMode.Id, StringComparison.OrdinalIgnoreCase));
            var testMode = baseMode with { SystemPrompt = SystemPromptTextBox.Text };
            if (modeIdx >= 0)
            {
                testModes[modeIdx] = testMode;
            }
            else
            {
                testModes.Add(testMode);
            }

            var tempConfig = _configService.Load() with
            {
                UseCustomWhisper = whisperIsCustom,
                CustomWhisperModelPath = whisperIsCustom ? whisperAlias : "",
                WhisperModelId = whisperIsCustom ? "" : whisperAlias,
                
                UseCustomChat = chatIsCustom,
                CustomChatModelPath = chatIsCustom ? chatAlias : "",
                ChatModelId = chatIsCustom ? "" : chatAlias,

                CleanEnabled = CleanCheckBox.IsChecked == true,
                ListFormattingEnabled = ListCheckBox.IsChecked == true,
                SpokenPunctuationEnabled = PunctuationCheckBox.IsChecked == true,
                Modes = testModes
            };

            using (_configService.PushTemporaryConfig(tempConfig))
            {
                // Timing: Whisper model load
                var whisperLoadSw = Stopwatch.StartNew();
                if (!_modelManager.WhisperReady || _modelManager.LoadedWhisperModelAlias != Path.GetFileName(whisperAlias))
                {
                    RawOutputTextBox.Text = $"Loading Speech model ({whisperSel})...";
                    await _modelManager.UnloadWhisperModelAsync();
                }
                await _modelManager.EnsureModelsLoadedAsync(baseMode.Id);
                whisperLoadSw.Stop();
                whisperLoadSec = whisperLoadSw.Elapsed.TotalSeconds;

                // Input determination
                string rawText;
                if (isAudio)
                {
                    // Timing: Transcription run
                    RawOutputTextBox.Text = "Transcribing recorded audio...";
                    var whisperRunSw = Stopwatch.StartNew();
                    rawText = await _transcriptionService.TranscribeAsync(_tempWavPath!, tempConfig.Language, ct);
                    whisperRunSw.Stop();
                    whisperRunSec = whisperRunSw.Elapsed.TotalSeconds;
                    
                    rawText = rawText.Trim();
                    RawOutputTextBox.Text = rawText;
                }
                else
                {
                    rawText = ManualInputTextBox.Text.Trim();
                    RawOutputTextBox.Text = rawText;
                    whisperLoadSec = 0; // bypassed
                    whisperRunSec = 0;
                }

                if (string.IsNullOrWhiteSpace(rawText))
                {
                    throw new InvalidOperationException("Transcription was empty. Please check your mic/input.");
                }

                // Apply Dictionary & Spoken Punctuation logic as in standard pipeline
                if (tempConfig.DictionaryEntries.Count > 0)
                {
                    rawText = PersonalDictionaryProcessor.Process(rawText, tempConfig.DictionaryEntries, _logger);
                }
                if (tempConfig.SpokenPunctuationEnabled)
                {
                    rawText = SpokenPunctuationProcessor.Process(rawText, tempConfig.Language, _logger);
                }

                // Timing: Chat model load
                var chatLoadSw = Stopwatch.StartNew();
                if (chatAlias == "none")
                {
                    chatLoadSec = 0;
                    chatRunSec = 0;
                }
                else if (chatIsCustom)
                {
                    if (!_modelManager.ChatReady || _modelManager.LoadedChatModelAlias != Path.GetFileName(chatAlias))
                    {
                        FormattedOutputTextBox.Text = $"Loading custom Chat model ({Path.GetFileName(chatAlias)})...";
                        await _modelManager.EnsureChatModelLoadedAsync(chatAlias);
                    }
                }
                else
                {
                    if (!_modelManager.ChatReady || _modelManager.LoadedChatModelAlias != chatAlias)
                    {
                        FormattedOutputTextBox.Text = $"Loading Chat model ({chatSel})...";
                        await _modelManager.EnsureChatModelLoadedAsync(chatAlias);
                    }
                }
                if (chatAlias != "none")
                {
                    chatLoadSw.Stop();
                    chatLoadSec = chatLoadSw.Elapsed.TotalSeconds;
                }

                // Timing: Formatting run
                string formattedText;
                if (chatAlias == "none")
                {
                    formattedText = rawText;
                }
                else
                {
                    FormattedOutputTextBox.Text = "Formatting text with Chat model...";
                    var chatRunSw = Stopwatch.StartNew();
                    formattedText = await _textFormatter.CleanupAsync(rawText, baseMode.Id, ct);
                    chatRunSw.Stop();
                    chatRunSec = chatRunSw.Elapsed.TotalSeconds;
                }

                FormattedOutputTextBox.Text = formattedText;
                overallSw.Stop();
                totalSec = overallSw.Elapsed.TotalSeconds;

                // Render metrics visualizers
                RenderTimeline(whisperLoadSec, whisperRunSec, chatLoadSec, chatRunSec, totalSec);
                
                // Speed calculations
                int wordCount = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                double wordsPerSec = whisperRunSec > 0 ? wordCount / whisperRunSec : 0;
                double charPerSec = chatRunSec > 0 ? formattedText.Length / chatRunSec : 0;

                WhisperTimingsText.Text = isAudio ? $"{whisperRunSec:F2}s ({wordsPerSec:F1} words/s)" : "Bypassed (Manual Text)";
                WhisperLoadText.Text = isAudio ? $" [Load: {whisperLoadSec:F2}s]" : "";
                WhisperLoadText.Visibility = isAudio && whisperLoadSec > 0.05 ? Visibility.Visible : Visibility.Collapsed;

                ChatTimingsText.Text = $"{chatRunSec:F2}s ({charPerSec:F1} char/s)";
                ChatLoadText.Text = $" [Load: {chatLoadSec:F2}s]";
                ChatLoadText.Visibility = chatLoadSec > 0.05 ? Visibility.Visible : Visibility.Collapsed;
                TotalTimingsText.Text = $"{totalSec:F2}s";

                // Add to history
                _runCounter++;
                var run = new TestRunInfo
                {
                    Label = $"Run {_runCounter} ({(isAudio ? "Mic" : "Text")})",
                    ConfigSummary = $"{whisperSel.Replace("Custom: ", "")} / {chatSel.Replace("Custom: ", "")} ({selectedModeName})",
                    SpeedLabel = $"{totalSec:F1}s",
                    PreviewText = formattedText,
                    
                    RawText = rawText,
                    FormattedText = formattedText,
                    WhisperAlias = whisperAlias,
                    ChatAlias = chatAlias,
                    ModeId = baseMode.Id,
                    SystemPrompt = SystemPromptTextBox.Text,
                    
                    WhisperLoadSec = whisperLoadSec,
                    WhisperRunSec = whisperRunSec,
                    ChatLoadSec = chatLoadSec,
                    ChatRunSec = chatRunSec,
                    TotalSec = totalSec,
                    WordsPerSecond = wordsPerSec,
                    CharactersPerSecond = charPerSec
                };

                _history.Insert(0, run);
                HistoryListView.SelectedItem = run;
                UpdateWizardSteps();
            }
        }
        catch (OperationCanceledException)
        {
            if (IsLoaded)
            {
                FormattedOutputTextBox.Text = "Cancelled";
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Sandbox pipeline run failed");
            if (IsLoaded)
            {
                FormattedOutputTextBox.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Pipeline execution failed:\n{ex.Message}", "Sandbox Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            RunPipelineButton.IsEnabled = true;
            RunPipelineButton.Content = "🚀 Run Test Sandbox";
        }
    }

    private void RenderTimeline(double whisperLoad, double whisperRun, double chatLoad, double chatRun, double total)
    {
        if (total <= 0) return;

        // Set grid columns widths proportionally
        WhisperLoadCol.Width = new GridLength(whisperLoad, GridUnitType.Star);
        WhisperRunCol.Width = new GridLength(whisperRun, GridUnitType.Star);
        ChatLoadCol.Width = new GridLength(chatLoad, GridUnitType.Star);
        ChatRunCol.Width = new GridLength(chatRun, GridUnitType.Star);
        TimelineRemainingCol.Width = new GridLength(Math.Max(0, total - whisperLoad - whisperRun - chatLoad - chatRun), GridUnitType.Star);

        // timeline size representation logic
        TimelineGrid.UpdateLayout();
    }

    private void HistoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = HistoryListView.SelectedItem as TestRunInfo;
        if (selected == null) return;

        // Display outputs
        RawOutputTextBox.Text = selected.RawText;
        FormattedOutputTextBox.Text = selected.FormattedText;
        SystemPromptTextBox.Text = selected.SystemPrompt;

        // Update selectors/options to match the run's config
        var wDisp = _whisperDisplayNameToAlias.FirstOrDefault(x => x.Value == selected.WhisperAlias || Path.GetFileName(x.Value) == Path.GetFileName(selected.WhisperAlias)).Key;
        if (wDisp != null) WhisperModelComboBox.SelectedItem = wDisp;

        var cDisp = _chatDisplayNameToAlias.FirstOrDefault(x => x.Value == selected.ChatAlias || Path.GetFileName(x.Value) == Path.GetFileName(selected.ChatAlias)).Key;
        if (cDisp != null) ChatModelComboBox.SelectedItem = cDisp;

        var mode = _config.Modes.FirstOrDefault(m => m.Id.Equals(selected.ModeId, StringComparison.OrdinalIgnoreCase));
        if (mode != null) ModeComboBox.SelectedItem = mode.Name;

        // Restore performance metrics visual details
        RenderTimeline(selected.WhisperLoadSec, selected.WhisperRunSec, selected.ChatLoadSec, selected.ChatRunSec, selected.TotalSec);
        
        WhisperTimingsText.Text = selected.WhisperRunSec > 0 ? $"{selected.WhisperRunSec:F2}s ({selected.WordsPerSecond:F1} words/s)" : "Bypassed (Manual Text)";
        WhisperLoadText.Text = $" [Load: {selected.WhisperLoadSec:F2}s]";
        WhisperLoadText.Visibility = selected.WhisperLoadSec > 0.05 ? Visibility.Visible : Visibility.Collapsed;

        ChatTimingsText.Text = $"{selected.ChatRunSec:F2}s ({selected.CharactersPerSecond:F1} char/s)";
        ChatLoadText.Text = $" [Load: {selected.ChatLoadSec:F2}s]";
        ChatLoadText.Visibility = selected.ChatLoadSec > 0.05 ? Visibility.Visible : Visibility.Collapsed;
        TotalTimingsText.Text = $"{selected.TotalSec:F2}s";

        UpdateWizardSteps();
    }

    private async void ApplyToModeButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedModeName = ModeComboBox.SelectedItem as string;
        if (string.IsNullOrEmpty(selectedModeName)) return;

        var mode = _config.Modes.FirstOrDefault(m => m.Name.Equals(selectedModeName, StringComparison.OrdinalIgnoreCase));
        if (mode == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to permanently update the system prompt and options for Mode '{selectedModeName}' with current Sandbox selections?",
            "Confirm Save Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var newModes = _config.Modes.ToList();
            var idx = newModes.FindIndex(m => m.Id.Equals(mode.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                newModes[idx] = mode with
                {
                    SystemPrompt = SystemPromptTextBox.Text.Trim()
                };
            }

            _config = _config with
            {
                Modes = newModes,
                CleanEnabled = CleanCheckBox.IsChecked == true,
                ListFormattingEnabled = ListCheckBox.IsChecked == true,
                SpokenPunctuationEnabled = PunctuationCheckBox.IsChecked == true
            };

            await _configService.SaveAsync(_config);
            MessageBox.Show($"Settings applied to mode '{selectedModeName}' successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Failed to save sandbox config back to mode");
            MessageBox.Show($"Failed to save:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CompareCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is TestRunInfo run)
        {
            var selectedCompareRuns = _history.Where(r => r.IsCompareTarget).ToList();
            if (selectedCompareRuns.Count > 2)
            {
                // Find the oldest compare run in history that is currently checked, excluding the one we just toggled
                var oldestToUncheck = _history
                    .Cast<TestRunInfo>()
                    .LastOrDefault(r => r.IsCompareTarget && r != run);

                if (oldestToUncheck != null)
                {
                    oldestToUncheck.IsCompareTarget = false;
                }
            }
        }

        var currentSelected = _history.Where(r => r.IsCompareTarget).ToList();
        CompareButton.Content = $"Compare ({currentSelected.Count}/2)";
        CompareButton.IsEnabled = currentSelected.Count == 2;
    }

    private void CompareButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedCompareRuns = _history.Where(r => r.IsCompareTarget).ToList();
        if (selectedCompareRuns.Count != 2)
        {
            MessageBox.Show("Please select exactly two runs from history to compare.", "Compare Runs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var left = selectedCompareRuns[1]; // older run
        var right = selectedCompareRuns[0]; // newer run

        // Populate comparison details
        CompLeftLabel.Text = left.Label;
        CompLeftConfig.Text = left.ConfigSummary;
        CompLeftRaw.Text = left.RawText;
        CompLeftFormatted.Text = left.FormattedText;
        CompLeftTimings.Text = $"Total: {left.TotalSec:F2}s (Whisper: {left.WhisperRunSec:F2}s | Chat: {left.ChatRunSec:F2}s)";
        CompLeftSpeeds.Text = $"Speed: {left.WordsPerSecond:F1} words/s | {left.CharactersPerSecond:F1} char/s";

        CompRightLabel.Text = right.Label;
        CompRightConfig.Text = right.ConfigSummary;
        CompRightRaw.Text = right.RawText;
        CompRightFormatted.Text = right.FormattedText;
        CompRightTimings.Text = $"Total: {right.TotalSec:F2}s (Whisper: {right.WhisperRunSec:F2}s | Chat: {right.ChatRunSec:F2}s)";
        CompRightSpeeds.Text = $"Speed: {right.WordsPerSecond:F1} words/s | {right.CharactersPerSecond:F1} char/s";

        // Open overlay panel
        ComparisonOverlay.Visibility = Visibility.Visible;
    }

    private void CloseComparison_Click(object sender, RoutedEventArgs e)
    {
        ComparisonOverlay.Visibility = Visibility.Collapsed;
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        _runCounter = 0;
        CompareButton.Content = "Compare (0/2)";
        CompareButton.IsEnabled = false;
        UpdateWizardSteps();
    }

    private void InputTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateWizardSteps();
    }

    private void ManualInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateWizardSteps();
    }

    private void UpdateWizardSteps()
    {
        if (!IsLoaded) return;

        var activeBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF00BFFF")!;
        var completedBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF30D158")!;
        var inactiveBadgeBg = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF2D2D2D")!;
        var inactiveTextBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF888888")!;
        var activeTextBrush = System.Windows.Media.Brushes.White;
        var inactiveCardBorder = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF2D2D2D")!;
        var finishedLineBrush = completedBrush;
        var unfinishedLineBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF444444")!;

        // 1. Evaluate Step 1: Select AI Models
        bool step1Done = WhisperModelComboBox.SelectedItem != null && ChatModelComboBox.SelectedItem != null;
        if (step1Done)
        {
            var whisperStatus = WhisperModelStatus.Text ?? "";
            var chatStatus = ChatModelStatus.Text ?? "";
            if (whisperStatus.Contains("Downloading") || whisperStatus.Contains("Not cached") ||
                chatStatus.Contains("Downloading") || chatStatus.Contains("Not cached"))
            {
                step1Done = false;
            }
        }

        // Update Step 1 visual elements
        if (step1Done)
        {
            Step1Badge.Background = completedBrush;
            Step1BadgeText.Foreground = activeTextBrush;
            Step1Text.Foreground = completedBrush;
            Step1Card.BorderBrush = completedBrush;
            Step1Card.Opacity = 1.0;
            StepLine1.Stroke = finishedLineBrush;
        }
        else
        {
            Step1Badge.Background = activeBrush;
            Step1BadgeText.Foreground = activeTextBrush;
            Step1Text.Foreground = activeBrush;
            Step1Card.BorderBrush = activeBrush;
            Step1Card.Opacity = 1.0;
            StepLine1.Stroke = unfinishedLineBrush;
        }

        // 2. Evaluate Step 2: Provide Test Input
        bool hasInput = false;
        if (InputTabControl.SelectedIndex == 0) // Mic
        {
            hasInput = !string.IsNullOrEmpty(_tempWavPath) && File.Exists(_tempWavPath);
        }
        else // Text
        {
            hasInput = !string.IsNullOrWhiteSpace(ManualInputTextBox.Text);
        }

        bool step2Active = step1Done;
        bool step2Done = step2Active && hasInput;

        if (step2Done)
        {
            Step2Badge.Background = completedBrush;
            Step2BadgeText.Foreground = activeTextBrush;
            Step2Text.Foreground = completedBrush;
            Step2Card.BorderBrush = completedBrush;
            Step2Card.Opacity = 1.0;
            Step2PanelBadge.Background = completedBrush;
            StepLine2.Stroke = finishedLineBrush;
        }
        else if (step2Active)
        {
            Step2Badge.Background = activeBrush;
            Step2BadgeText.Foreground = activeTextBrush;
            Step2Text.Foreground = activeBrush;
            Step2Card.BorderBrush = activeBrush;
            Step2Card.Opacity = 1.0;
            Step2PanelBadge.Background = activeBrush;
            StepLine2.Stroke = unfinishedLineBrush;
        }
        else
        {
            Step2Badge.Background = inactiveBadgeBg;
            Step2BadgeText.Foreground = inactiveTextBrush;
            Step2Text.Foreground = inactiveTextBrush;
            Step2Card.BorderBrush = inactiveCardBorder;
            Step2Card.Opacity = 0.5;
            Step2PanelBadge.Background = inactiveBadgeBg;
            StepLine2.Stroke = unfinishedLineBrush;
        }

        // 3. Evaluate Step 3: Configure & Run Sandbox
        bool step3Active = step2Done;
        bool step3Done = step3Active && _runCounter > 0;

        if (step3Done)
        {
            Step3Badge.Background = completedBrush;
            Step3BadgeText.Foreground = activeTextBrush;
            Step3Text.Foreground = completedBrush;
            Step3Card.BorderBrush = completedBrush;
            Step3Card.Opacity = 1.0;
            Step3PanelBadge.Background = completedBrush;
            RunPipelineButton.Background = completedBrush;
            RunPipelineButton.BorderBrush = completedBrush;
            StepLine3.Stroke = finishedLineBrush;
        }
        else if (step3Active)
        {
            Step3Badge.Background = activeBrush;
            Step3BadgeText.Foreground = activeTextBrush;
            Step3Text.Foreground = activeBrush;
            Step3Card.BorderBrush = activeBrush;
            Step3Card.Opacity = 1.0;
            Step3PanelBadge.Background = activeBrush;
            RunPipelineButton.Background = activeBrush;
            RunPipelineButton.BorderBrush = activeBrush;
            StepLine3.Stroke = unfinishedLineBrush;
        }
        else
        {
            Step3Badge.Background = inactiveBadgeBg;
            Step3BadgeText.Foreground = inactiveTextBrush;
            Step3Text.Foreground = inactiveTextBrush;
            Step3Card.BorderBrush = inactiveCardBorder;
            Step3Card.Opacity = 0.5;
            Step3PanelBadge.Background = inactiveBadgeBg;
            RunPipelineButton.Background = inactiveBadgeBg;
            RunPipelineButton.BorderBrush = inactiveCardBorder;
            StepLine3.Stroke = unfinishedLineBrush;
        }

        // 4. Evaluate Step 4: Analyze Results & Save Prompt
        bool step4Active = step3Done;

        if (step4Active)
        {
            Step4Badge.Background = completedBrush;
            Step4BadgeText.Foreground = activeTextBrush;
            Step4Text.Foreground = completedBrush;
            Step4Card.BorderBrush = completedBrush;
            Step4Card.Opacity = 1.0;
            Step4PanelBadge.Background = completedBrush;
        }
        else
        {
            Step4Badge.Background = inactiveBadgeBg;
            Step4BadgeText.Foreground = inactiveTextBrush;
            Step4Text.Foreground = inactiveTextBrush;
            Step4Card.BorderBrush = inactiveCardBorder;
            Step4Card.Opacity = 0.5;
            Step4PanelBadge.Background = inactiveBadgeBg;
        }
    }
}

