using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Prompter.Services;

namespace Prompter.Views;

public partial class CustomModelManagerWindow : Window
{
    private readonly IConfigService _configService;
    private readonly IFileLogger _logger;
    private readonly IHuggingFaceService _hfService;
    private readonly IGgufModelStore _ggufStore;
    private readonly string _whisperDir;
    private readonly string _foundryDir;
    private static readonly HttpClient _httpClient = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });

    static CustomModelManagerWindow()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Prompter/1.0");
    }
    private CancellationTokenSource? _downloadCts;
    private CancellationTokenSource? _hubSearchCts;
    private bool _isLlmMode = false;

    private readonly ObservableCollection<DownloadedModelInfo> _downloadedModels = new();
    private readonly ObservableCollection<RecommendedModelInfo> _recommendedModels = new();
    private readonly ObservableCollection<HubRepoViewModel> _hubRepos = new();
    private readonly ObservableCollection<HubFileViewModel> _hubFiles = new();
    private readonly ObservableCollection<HubRepoViewModel> _advancedRepos = new();
    private readonly ObservableCollection<HubFileViewModel> _advancedFiles = new();

    private string? _downloadedSortBy;
    private ListSortDirection _downloadedSortDirection = ListSortDirection.Ascending;
    private string? _recommendedSortBy;
    private ListSortDirection _recommendedSortDirection = ListSortDirection.Ascending;
    private string? _hubRepoSortBy;
    private ListSortDirection _hubRepoSortDirection = ListSortDirection.Ascending;
    private string? _hubFileSortBy;
    private ListSortDirection _hubFileSortDirection = ListSortDirection.Ascending;
    private CancellationTokenSource? _advancedSearchCts;
    private string? _advancedRepoSortBy;
    private ListSortDirection _advancedRepoSortDirection = ListSortDirection.Ascending;
    private string? _advancedFileSortBy;
    private ListSortDirection _advancedFileSortDirection = ListSortDirection.Ascending;

    public CustomModelManagerWindow(IConfigService configService, IFileLogger logger, IHuggingFaceService hfService, IGgufModelStore ggufStore)
    {
        InitializeComponent();
        _configService = configService;
        _logger = logger;
        _hfService = hfService;
        _ggufStore = ggufStore;

        _whisperDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prompter", "models", "ggml");

        _foundryDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "AI.Foundry.Local", "models");

        DownloadedListView.ItemsSource = _downloadedModels;
        RecommendedListView.ItemsSource = _recommendedModels;
        HubRepoListView.ItemsSource = _hubRepos;
        HubFileListView.ItemsSource = _hubFiles;
        AdvancedRepoListView.ItemsSource = _advancedRepos;
        AdvancedFileListView.ItemsSource = _advancedFiles;

        Loaded += CustomModelManagerWindow_Loaded;
    }

    private async void CustomModelManagerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await UpdateUIModeAsync();
    }

    private async void ModeButton_Checked(object sender, RoutedEventArgs e)
    {
        if (WhisperModeButton == null || LlmModeButton == null) return;
        _isLlmMode = LlmModeButton.IsChecked == true;
        await UpdateUIModeAsync();
    }

    private async Task UpdateUIModeAsync()
    {
        if (_downloadCts != null) return; // Ignore if downloading

        // Configure lists and UI based on Mode
        if (_isLlmMode)
        {
            RepoIdTextBox.Text = "Qwen/Qwen2.5-0.5B-Instruct-ONNX";
            FileNameContainer.Visibility = Visibility.Collapsed;
            LlmDownloadInfoContainer.Visibility = Visibility.Visible;

            InitializeRecommendedLlms();
        }
        else
        {
            RepoIdTextBox.Text = "distil-whisper/distil-large-v3-ggml";
            FileNameTextBox.Text = "ggml-distil-large-v3.bin";
            FileNameContainer.Visibility = Visibility.Visible;
            LlmDownloadInfoContainer.Visibility = Visibility.Collapsed;

            InitializeRecommendedWhispers();
        }

        if (HubTabItem != null)
        {
            if (_isLlmMode)
            {
                if (!ManagerTabControl.Items.Contains(HubTabItem))
                    ManagerTabControl.Items.Insert(2, HubTabItem);
            }
            else
            {
                if (ManagerTabControl.Items.Contains(HubTabItem))
                    ManagerTabControl.Items.Remove(HubTabItem);
            }
        }

        _advancedRepos.Clear();
        _advancedFiles.Clear();
        AdvancedRepoEmptyText.Text = "Enter a search term to find repositories.";
        AdvancedRepoEmptyText.Visibility = Visibility.Visible;
        AdvancedFileEmptyText.Text = "Select a repository above to see its files.";
        AdvancedFileEmptyText.Visibility = Visibility.Visible;

        ConfigureDownloadedColumns();
        await RefreshListsAsync();
    }

    private void ConfigureDownloadedColumns()
    {
        var gridView = DownloadedListView.View as GridView;
        if (gridView == null) return;

        gridView.Columns.Clear();
        if (_isLlmMode)
        {
            gridView.Columns.Add(new GridViewColumn { Header = "Folder Name", DisplayMemberBinding = new System.Windows.Data.Binding("FileName"), Width = 240 });
            gridView.Columns.Add(new GridViewColumn { Header = "Storage Path", DisplayMemberBinding = new System.Windows.Data.Binding("RelativePath"), Width = 160 });
            gridView.Columns.Add(new GridViewColumn { Header = "Size", DisplayMemberBinding = new System.Windows.Data.Binding("SizeDescription"), Width = 100 });
        }
        else
        {
            gridView.Columns.Add(new GridViewColumn { Header = "File Name", DisplayMemberBinding = new System.Windows.Data.Binding("FileName"), Width = 240 });
            gridView.Columns.Add(new GridViewColumn { Header = "File Path", DisplayMemberBinding = new System.Windows.Data.Binding("RelativePath"), Width = 160 });
            gridView.Columns.Add(new GridViewColumn { Header = "Size", DisplayMemberBinding = new System.Windows.Data.Binding("SizeDescription"), Width = 100 });
        }
    }

    private void InitializeRecommendedWhispers()
    {
        _recommendedModels.Clear();
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Distil-Whisper Large v3",
            Description = "Distilled English model. Exceptional speed & high accuracy.",
            RepoId = "distil-whisper/distil-large-v3-ggml",
            FileName = "ggml-distil-large-v3.bin",
            SizeDescription = "~465 MB",
            Status = "Checking...",
            SizeMegabytes = 465
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Whisper Large v3 Turbo",
            Description = "Fastest full-sized model. Multilingual support.",
            RepoId = "ggerganov/whisper.cpp",
            FileName = "ggml-large-v3-turbo.bin",
            SizeDescription = "~809 MB",
            Status = "Checking...",
            SizeMegabytes = 809
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Whisper Small (English)",
            Description = "Balanced lightweight model for English only.",
            RepoId = "ggerganov/whisper.cpp",
            FileName = "ggml-small.en.bin",
            SizeDescription = "~463 MB",
            Status = "Checking...",
            SizeMegabytes = 463
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Whisper Base (English)",
            Description = "Extremely fast, low resource English-only model.",
            RepoId = "ggerganov/whisper.cpp",
            FileName = "ggml-base.en.bin",
            SizeDescription = "~141 MB",
            Status = "Checking...",
            SizeMegabytes = 141
        });
    }

    private void InitializeRecommendedLlms()
    {
        _recommendedModels.Clear();
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Qwen 2.5 0.5B Instruct",
            Description = "Very lightweight, fast text corrector (INT4 quantized).",
            RepoId = "Qwen/Qwen2.5-0.5B-Instruct-ONNX",
            FileName = "onnx/cpu_and_mobile/cpu-int4-rtn-block-32",
            SizeDescription = "~380 MB",
            Status = "Checking...",
            SizeMegabytes = 380
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Qwen 2.5 1.5B Instruct",
            Description = "Strong balance of speed and correction quality (INT4).",
            RepoId = "Qwen/Qwen2.5-1.5B-Instruct-ONNX",
            FileName = "onnx/cpu_and_mobile/cpu-int4-rtn-block-32",
            SizeDescription = "~950 MB",
            Status = "Checking...",
            SizeMegabytes = 950
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Phi-3.5 Mini Instruct",
            Description = "Highly accurate Microsoft model. Requires GPU/Strong NPU (INT4).",
            RepoId = "microsoft/Phi-3.5-mini-instruct-onnx",
            FileName = "onnx/cpu_and_mobile/cpu-int4-rtn-block-32",
            SizeDescription = "~2.2 GB",
            Status = "Checking...",
            SizeMegabytes = 2200
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Gemma 2 2B Instruct",
            Description = "Strong Google model, optimized for text correction (INT4).",
            RepoId = "keisuke-miyako/gemma-2-2B-it-onnx-int4",
            FileName = "",
            SizeDescription = "~1.6 GB",
            Status = "Checking...",
            SizeMegabytes = 1600
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Gemma 3 4B Instruct",
            Description = "Google's latest state-of-the-art model. High accuracy (INT4).",
            RepoId = "onnxruntime/Gemma-3-ONNX",
            FileName = "gemma-3-4b-it/cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4",
            SizeDescription = "~2.5 GB",
            Status = "Checking...",
            SizeMegabytes = 2500
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Llama 3.2 1B Instruct",
            Description = "Meta's fast small model. Good instruction following (INT4).",
            RepoId = "onnx-community/Llama-3.2-1B-Instruct-GENAI-ONNX",
            FileName = "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4",
            SizeDescription = "~700 MB",
            Status = "Checking...",
            SizeMegabytes = 700
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Llama 3.2 3B Instruct",
            Description = "Meta's balanced model. Stronger correction than 1B (INT4).",
            RepoId = "onnx-community/Llama-3.2-3B-Instruct-GENAI-ONNX",
            FileName = "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4",
            SizeDescription = "~1.8 GB",
            Status = "Checking...",
            SizeMegabytes = 1800
        });
    }

    private async Task RefreshListsAsync()
    {
        try
        {
            _downloadedModels.Clear();

            if (_isLlmMode)
            {
                if (!Directory.Exists(_foundryDir))
                {
                    Directory.CreateDirectory(_foundryDir);
                }

                var dirs = Directory.GetDirectories(_foundryDir);
                foreach (var dir in dirs)
                {
                    var dirInfo = new DirectoryInfo(dir);
                    var sizeMb = GetDirectorySizeInMb(dir);
                    _downloadedModels.Add(new DownloadedModelInfo
                    {
                        FileName = dirInfo.Name,
                        RelativePath = "Microsoft\\AI.Foundry.Local\\models\\" + dirInfo.Name,
                        SizeDescription = sizeMb >= 1000 ? $"~{sizeMb / 1000:F1} GB" : $"~{sizeMb:F0} MB",
                        FullPath = dir,
                        SizeMegabytes = sizeMb
                    });
                }

                var ggufs = await _ggufStore.GetInstalledModelsAsync(CancellationToken.None);
                foreach (var gguf in ggufs)
                {
                    double mb = gguf.FileSizeBytes / 1024.0 / 1024.0;
                    _downloadedModels.Add(new DownloadedModelInfo
                    {
                        FileName = "[GGUF] " + gguf.FileName,
                        RelativePath = gguf.FullPath.Replace(_ggufStore.BaseDirectory + "\\", ""),
                        SizeDescription = mb >= 1000 ? $"~{mb / 1000:F1} GB" : $"~{mb:F0} MB",
                        FullPath = gguf.FullPath,
                        SizeMegabytes = mb
                    });
                }

                // Update Recommended
                foreach (var rec in _recommendedModels)
                {
                    var alias = rec.RepoId.Split('/').Last().ToLowerInvariant();
                    var targetPath = Path.Combine(_foundryDir, alias);
                    rec.Status = Directory.Exists(targetPath) && Directory.GetFiles(targetPath, "*.onnx").Length > 0
                        ? "Cached ✓"
                        : "Available";
                }
            }
            else
            {
                if (!Directory.Exists(_whisperDir))
                {
                    Directory.CreateDirectory(_whisperDir);
                }

                var files = Directory.GetFiles(_whisperDir, "*.bin");
                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    double mb = info.Length / 1024.0 / 1024.0;
                    _downloadedModels.Add(new DownloadedModelInfo
                    {
                        FileName = info.Name,
                        RelativePath = "models\\ggml\\" + info.Name,
                        SizeDescription = $"{mb:F1} MB",
                        FullPath = file,
                        SizeMegabytes = mb
                    });
                }

                // Update Recommended
                foreach (var rec in _recommendedModels)
                {
                    var targetPath = Path.Combine(_whisperDir, rec.FileName);
                    rec.Status = File.Exists(targetPath) ? "Cached ✓" : "Available";
                }
            }

            RecommendedListView.Items.Refresh();
            ApplySortIfActive(DownloadedListView);
            ApplySortIfActive(RecommendedListView);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Failed to refresh models list");
        }
    }

    private static double GetDirectorySizeInMb(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            var size = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                                .Sum(f => new FileInfo(f).Length);
            return size / 1024.0 / 1024.0;
        }
        catch
        {
            return 0;
        }
    }

    private async void RefreshDownloadedButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshListsAsync();
    }

    private async void DeleteModelButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = DownloadedListView.SelectedItem as DownloadedModelInfo;
        if (selected == null)
        {
            MessageBox.Show("Please select an item to delete.", "Delete Model", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Are you sure you want to delete '{selected.FileName}'?\nThis will remove the file or model folder.", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var config = _configService.Load();
            if (_isLlmMode)
            {
                if (config.ChatModelId == selected.FileName)
                {
                    MessageBox.Show("Cannot delete this model directory because it is currently selected as your active text correction model. Please change it in Settings first.", "Directory Locked", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (config.UseCustomChat && config.CustomChatModelPath == selected.FullPath)
                {
                    MessageBox.Show("Cannot delete this GGUF file because it is currently selected as your active text correction model. Please change it in Settings first.", "File Locked", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (Directory.Exists(selected.FullPath))
                {
                    Directory.Delete(selected.FullPath, true);
                }
                else if (File.Exists(selected.FullPath))
                {
                    File.Delete(selected.FullPath);
                }
            }
            else
            {
                if (config.UseCustomWhisper && config.CustomWhisperModelPath == selected.FullPath)
                {
                    MessageBox.Show("Cannot delete this model file because it is currently selected as your active speech model. Please change it in Settings first.", "File Locked", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (File.Exists(selected.FullPath))
                {
                    File.Delete(selected.FullPath);
                }
            }

            await RefreshListsAsync();
            MessageBox.Show("Deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, $"Failed to delete model: {selected.FullPath}");
            MessageBox.Show($"Failed to delete:\n{ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DownloadCuratedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = RecommendedListView.SelectedItem as RecommendedModelInfo;
        if (selected == null)
        {
            MessageBox.Show("Please select a recommended model from the list first.", "Select Model", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.Status == "Cached ✓")
        {
            MessageBox.Show("This model is already cached locally.", "Already Downloaded", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_isLlmMode)
        {
            await StartLlmDownloadAsync(selected.RepoId, selected.FileName);
        }
        else
        {
            await StartWhisperDownloadAsync(selected.RepoId, selected.FileName);
        }
    }

    private async void DownloadAdvancedButton_Click(object sender, RoutedEventArgs e)
    {
        var repoId = RepoIdTextBox.Text.Trim();
        if (string.IsNullOrEmpty(repoId))
        {
            MessageBox.Show("Please fill out the Repository ID.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_isLlmMode)
        {
            var subdir = LlmSubdirTextBox.Text.Trim();
            await StartLlmDownloadAsync(repoId, subdir);
        }
        else
        {
            var fileName = FileNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Please enter a valid GGML filename ending in .bin", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await StartWhisperDownloadAsync(repoId, fileName);
        }
    }

    private async Task StartWhisperDownloadAsync(string repoId, string fileName)
    {
        SetUIEnabled(false);
        _downloadCts = new CancellationTokenSource();

        try
        {
            if (!Directory.Exists(_whisperDir))
            {
                Directory.CreateDirectory(_whisperDir);
            }

            var targetPath = Path.Combine(_whisperDir, fileName);

            ProgressStatusTextBlock.Text = "Connecting to Hugging Face...";
            DownloadProgressBar.Value = 0;
            ProgressPctTextBlock.Text = "0%";

            var stopwatch = Stopwatch.StartNew();
            var progress = new Progress<(long Received, long Total)>(p =>
            {
                if (p.Total > 0)
                {
                    var pct = (double)p.Received / p.Total * 100;
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    var speed = elapsed > 0 ? (p.Received / 1024.0 / 1024.0) / elapsed : 0;
                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgressBar.Value = pct;
                        ProgressPctTextBlock.Text = $"{pct:F0}%";
                        ProgressStatusTextBlock.Text = $"Downloading {fileName} ({speed:F1} MB/s)";
                    });
                }
            });

            await _hfService.DownloadAsync(repoId, fileName, targetPath, progress, _downloadCts.Token);

            ProgressStatusTextBlock.Text = "Download complete ✓";
            DownloadProgressBar.Value = 100;
            ProgressPctTextBlock.Text = "100%";

            await RefreshListsAsync();
            MessageBox.Show($"Successfully downloaded '{fileName}'.", "Download Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            ProgressStatusTextBlock.Text = "Download cancelled";
            DownloadProgressBar.Value = 0;
            ProgressPctTextBlock.Text = "";
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, $"Failed to download whisper model {fileName}");
            ProgressStatusTextBlock.Text = "Download failed ⚠";
            MessageBox.Show($"Download failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _downloadCts.Dispose();
            _downloadCts = null;
            Dispatcher.Invoke(() => SetUIEnabled(true));
        }
    }

    private class HuggingFaceRepoInfo
    {
        public List<HuggingFaceSibling>? siblings { get; set; }
    }

    private class HuggingFaceSibling
    {
        public string rpath { get; set; } = "";
    }

    private async Task StartLlmDownloadAsync(string repoId, string specifiedSubdir)
    {
        SetUIEnabled(false);
        _downloadCts = new CancellationTokenSource();

        try
        {
            ProgressStatusTextBlock.Text = $"Querying Hugging Face repository metadata...";
            DownloadProgressBar.Value = 0;
            ProgressPctTextBlock.Text = "0%";

            // Get repo file structure from Hugging Face API
            var apiUrl = $"https://huggingface.co/api/models/{repoId}";
            using var apiRequest = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            var token = _configService.Load().HuggingFaceToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                apiRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            using var apiResponse = await _httpClient.SendAsync(apiRequest, _downloadCts.Token);
            apiResponse.EnsureSuccessStatusCode();
            var jsonResponse = await apiResponse.Content.ReadAsStringAsync(_downloadCts.Token);
            var repoInfo = JsonSerializer.Deserialize<HuggingFaceRepoInfo>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (repoInfo?.siblings == null || repoInfo.siblings.Count == 0)
            {
                throw new InvalidOperationException("Failed to retrieve file list from Hugging Face repository.");
            }

            string detectedPrefix = "";
            if (!string.IsNullOrWhiteSpace(specifiedSubdir))
            {
                detectedPrefix = specifiedSubdir.Trim().Replace('\\', '/');
                if (!detectedPrefix.EndsWith("/")) detectedPrefix += "/";
            }
            else
            {
                // Auto-detect directory containing genai_config.json
                var genaiConfigSibling = repoInfo.siblings.FirstOrDefault(s => s.rpath.EndsWith("genai_config.json", StringComparison.OrdinalIgnoreCase));
                if (genaiConfigSibling == null)
                {
                    throw new FileNotFoundException("Could not auto-detect a valid ONNX GenAI model directory (genai_config.json not found). Please specify the folder path in Advanced options.");
                }

                var idx = genaiConfigSibling.rpath.LastIndexOf("genai_config.json", StringComparison.OrdinalIgnoreCase);
                detectedPrefix = genaiConfigSibling.rpath.Substring(0, idx);
            }

            // Filter files that are in the detected subdirectory
            var filesToDownload = repoInfo.siblings
                .Where(s => s.rpath.StartsWith(detectedPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filesToDownload.Count == 0)
            {
                throw new FileNotFoundException($"No files found in directory prefix: '{detectedPrefix}'");
            }

            var modelAlias = repoId.Split('/').Last().ToLowerInvariant();
            var targetDir = Path.Combine(_foundryDir, modelAlias);
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            _logger.Log($"Starting LLM download for {repoId}. Target directory: {targetDir}. Files count: {filesToDownload.Count}");

            int count = 0;
            foreach (var sibling in filesToDownload)
            {
                count++;
                var relativeFileName = sibling.rpath.Substring(detectedPrefix.Length);
                if (string.IsNullOrWhiteSpace(relativeFileName)) continue;

                var fileTargetPath = Path.Combine(targetDir, relativeFileName.Replace('/', '\\'));
                var fileTargetDir = Path.GetDirectoryName(fileTargetPath);
                if (fileTargetDir != null && !Directory.Exists(fileTargetDir))
                {
                    Directory.CreateDirectory(fileTargetDir);
                }

                ProgressStatusTextBlock.Text = $"Downloading file {count} of {filesToDownload.Count}: {relativeFileName}...";
                DownloadProgressBar.Value = (double)(count - 1) / filesToDownload.Count * 100;
                ProgressPctTextBlock.Text = $"{DownloadProgressBar.Value:F0}%";

                var fileProgress = new Progress<(long Received, long Total)>(p =>
                {
                    if (p.Total > 0)
                    {
                        var filePct = (double)p.Received / p.Total;
                        var overallPct = (((double)(count - 1) + filePct) / filesToDownload.Count) * 100;
                        Dispatcher.Invoke(() =>
                        {
                            DownloadProgressBar.Value = overallPct;
                            ProgressPctTextBlock.Text = $"{overallPct:F0}%";
                            ProgressStatusTextBlock.Text = $"Downloading file {count} of {filesToDownload.Count}: {relativeFileName} ({filePct * 100:F0}%)";
                        });
                    }
                });

                await _hfService.DownloadAsync(repoId, sibling.rpath, fileTargetPath, fileProgress, _downloadCts.Token);
            }

            // Write the inference_model.json template automatically based on name autodetection
            WriteInferenceModelJsonTemplate(targetDir, modelAlias);

            ProgressStatusTextBlock.Text = "All files downloaded successfully ✓";
            DownloadProgressBar.Value = 100;
            ProgressPctTextBlock.Text = "100%";

            await RefreshListsAsync();
            MessageBox.Show($"Successfully downloaded '{modelAlias}' and generated its template. You can now select it in Settings.", "Download Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            ProgressStatusTextBlock.Text = "Download cancelled";
            DownloadProgressBar.Value = 0;
            ProgressPctTextBlock.Text = "";
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, $"Failed to download LLM directory from {repoId}");
            ProgressStatusTextBlock.Text = "Download failed ⚠";
            MessageBox.Show($"Download failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _downloadCts.Dispose();
            _downloadCts = null;
            Dispatcher.Invoke(() => SetUIEnabled(true));
        }
    }

    internal static Dictionary<string, string> BuildChatTemplate(string modelAlias)
    {
        var template = new Dictionary<string, string>();
        var alias = modelAlias.ToLowerInvariant();
        if (alias.Contains("qwen"))
        {
            template["system"] = "<|im_start|>system\n{content}\n";
            template["user"] = "<|im_start|>user\n{content}\n";
            template["assistant"] = "<|im_start|>assistant\n{content}\n";
        }
        else if (alias.Contains("phi"))
        {
            template["system"] = "<|system|>\n{content}<|end|>\n";
            template["user"] = "<|user|>\n{content}<|end|>\n";
            template["assistant"] = "<|assistant|>\n{content}<|end|>\n";
        }
        else if (alias.Contains("llama"))
        {
            template["system"] = "<|start_header_id|>system<|end_header_id|>\n\n{content}<|eot_id|>";
            template["user"] = "<|start_header_id|>user<|end_header_id|>\n\n{content}<|eot_id|>";
            template["assistant"] = "<|start_header_id|>assistant<|end_header_id|>\n\n{content}<|eot_id|>";
        }
        else if (alias.Contains("gemma"))
        {
            template["system"] = "<start_of_turn>user\nSystem instructions: {content}<end_of_turn>\n";
            template["user"] = "<start_of_turn>user\n{content}<end_of_turn>\n";
            template["assistant"] = "<start_of_turn>model\n{content}<end_of_turn>\n";
        }
        else
        {
            // Fallback to standard ChatML
            template["system"] = "<|im_start|>system\n{content}\n";
            template["user"] = "<|im_start|>user\n{content}\n";
            template["assistant"] = "<|im_start|>assistant\n{content}\n";
        }
        return template;
    }

    internal void WriteInferenceModelJsonTemplate(string folderPath, string modelAlias)
    {
        try
        {
            var jsonPath = Path.Combine(folderPath, "inference_model.json");
            if (File.Exists(jsonPath)) return; // Don't overwrite if model already has one

            var promptTemplate = BuildChatTemplate(modelAlias);
            var template = new
            {
                model_type = "chat",
                prompt_template = promptTemplate
            };

            var jsonText = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonPath, jsonText);
            _logger.Log($"Wrote auto-detected chat template to: {jsonPath}");
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Failed to write inference_model.json template");
        }
    }

    private async void HubSearchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunHubSearchAsync();
    }

    private void HubSearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            _ = RunHubSearchAsync();
        }
    }

    private async void AdvancedSearchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAdvancedSearchAsync();
    }

    private void AdvancedSearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            _ = RunAdvancedSearchAsync();
        }
    }

    private async Task RunAdvancedSearchAsync()
    {
        var query = AdvancedSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show("Please enter a search term.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _advancedSearchCts?.Cancel();
        _advancedSearchCts?.Dispose();
        _advancedSearchCts = new CancellationTokenSource();
        var ct = _advancedSearchCts.Token;

        ProgressStatusTextBlock.Text = "Searching Hugging Face...";
        DownloadProgressBar.Value = 0;
        ProgressPctTextBlock.Text = "";
        _advancedRepos.Clear();
        _advancedFiles.Clear();
        SetUIEnabled(false);
        AdvancedRepoEmptyText.Text = "No repositories found. Try a different search term.";

        try
        {
            var repos = await _hfService.SearchRepositoriesAsync(query, 20, ct);
            foreach (var repo in repos)
            {
                if (ct.IsCancellationRequested) break;
                _advancedRepos.Add(new HubRepoViewModel(repo));
            }
            ProgressStatusTextBlock.Text = $"Found {_advancedRepos.Count} repositories.";
            AdvancedRepoEmptyText.Visibility = _advancedRepos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplySortIfActive(AdvancedRepoListView);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Advanced search failed");
            ProgressStatusTextBlock.Text = "Search failed";
            AdvancedRepoEmptyText.Text = "Search failed.";
            AdvancedRepoEmptyText.Visibility = Visibility.Visible;
            MessageBox.Show($"Search failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetUIEnabled(true);
        }
    }

    private async Task RunHubSearchAsync()
    {
        var query = HubSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show("Please enter a search term.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _hubSearchCts?.Cancel();
        _hubSearchCts?.Dispose();
        _hubSearchCts = new CancellationTokenSource();
        var ct = _hubSearchCts.Token;

        ProgressStatusTextBlock.Text = "Searching Hugging Face...";
        DownloadProgressBar.Value = 0;
        ProgressPctTextBlock.Text = "";
        _hubRepos.Clear();
        _hubFiles.Clear();
        SetUIEnabled(false);

        try
        {
            var repos = await _hfService.SearchAsync(query, 20, ct);
            foreach (var repo in repos)
            {
                if (ct.IsCancellationRequested) break;
                _hubRepos.Add(new HubRepoViewModel(repo));
            }
            ProgressStatusTextBlock.Text = $"Found {_hubRepos.Count} GGUF repositories.";
            HubRepoEmptyText.Visibility = _hubRepos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplySortIfActive(HubRepoListView);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Hub search failed");
            ProgressStatusTextBlock.Text = "Search failed";
            HubRepoEmptyText.Visibility = Visibility.Visible;
            MessageBox.Show($"Search failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetUIEnabled(true);
        }
    }

    private async void HubRepoListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _hubFiles.Clear();
        HubDownloadButton.IsEnabled = false;
        var selected = HubRepoListView.SelectedItem as HubRepoViewModel;
        if (selected == null)
        {
            HubFileEmptyText.Text = "Select a repository above to see its GGUF files.";
            HubFileEmptyText.Visibility = Visibility.Visible;
            return;
        }

        HubFileEmptyText.Text = "Loading files...";
        HubFileEmptyText.Visibility = Visibility.Visible;

        try
        {
            ProgressStatusTextBlock.Text = $"Listing files for {selected.RepoId}...";
            var expectedRepoId = selected.RepoId;
            var files = await _hfService.ListGgufFilesAsync(expectedRepoId, CancellationToken.None);
            var current = HubRepoListView.SelectedItem as HubRepoViewModel;
            if (current == null || current.RepoId != expectedRepoId)
                return;
            foreach (var file in files)
            {
                long? size = null;
                try
                {
                    size = await _hfService.GetFileSizeAsync(expectedRepoId, file.FilePath, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogException(ex, $"Failed to get size for {expectedRepoId}/{file.FilePath}");
                }
                var cur = HubRepoListView.SelectedItem as HubRepoViewModel;
                if (cur == null || cur.RepoId != expectedRepoId)
                    break;
                _hubFiles.Add(new HubFileViewModel(file.FilePath, size));
            }
            ProgressStatusTextBlock.Text = $"Found {_hubFiles.Count} GGUF file(s).";
            HubFileEmptyText.Visibility = _hubFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_hubFiles.Count == 0)
            {
                HubFileEmptyText.Text = "No GGUF files found in this repository.";
            }
            ApplySortIfActive(HubFileListView);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Hub file list failed");
            ProgressStatusTextBlock.Text = "Failed to list files";
            HubFileEmptyText.Text = "Failed to list files.";
            HubFileEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void HubFileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HubDownloadButton.IsEnabled = HubFileListView.SelectedItem != null && _downloadCts == null;
    }

    private async void AdvancedRepoListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _advancedFiles.Clear();
        var selected = AdvancedRepoListView.SelectedItem as HubRepoViewModel;
        if (selected == null)
        {
            AdvancedFileEmptyText.Text = "Select a repository above to see its files.";
            AdvancedFileEmptyText.Visibility = Visibility.Visible;
            return;
        }

        RepoIdTextBox.Text = selected.RepoId;
        if (!_isLlmMode)
        {
            FileNameTextBox.Text = "";
        }
        else
        {
            LlmSubdirTextBox.Text = "";
        }

        AdvancedFileEmptyText.Text = "Loading files...";
        AdvancedFileEmptyText.Visibility = Visibility.Visible;

        try
        {
            ProgressStatusTextBlock.Text = $"Listing files for {selected.RepoId}...";
            var files = await _hfService.ListRepoFilesAsync(selected.RepoId, CancellationToken.None);
            var current = AdvancedRepoListView.SelectedItem as HubRepoViewModel;
            if (current == null || current.RepoId != selected.RepoId)
                return;

            var filteredFiles = _isLlmMode ? files : files.Where(f => f.FilePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

            foreach (var file in filteredFiles)
            {
                long? size = null;
                try
                {
                    size = await _hfService.GetFileSizeAsync(selected.RepoId, file.FilePath, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogException(ex, $"Failed to get size for {selected.RepoId}/{file.FilePath}");
                }
                var cur = AdvancedRepoListView.SelectedItem as HubRepoViewModel;
                if (cur == null || cur.RepoId != selected.RepoId)
                    break;
                _advancedFiles.Add(new HubFileViewModel(file.FilePath, size));
            }

            ProgressStatusTextBlock.Text = $"Found {_advancedFiles.Count} file(s).";
            AdvancedFileEmptyText.Visibility = _advancedFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_advancedFiles.Count == 0)
            {
                AdvancedFileEmptyText.Text = _isLlmMode ? "No files found in this repository." : "No .bin files found in this repository.";
            }
            ApplySortIfActive(AdvancedFileListView);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Advanced file list failed");
            ProgressStatusTextBlock.Text = "Failed to list files";
            AdvancedFileEmptyText.Text = "Failed to list files.";
            AdvancedFileEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void AdvancedFileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = AdvancedFileListView.SelectedItem as HubFileViewModel;
        if (selected == null) return;

        if (_isLlmMode)
        {
            var lastSlash = selected.FilePath.LastIndexOf('/');
            var dir = lastSlash > 0 ? selected.FilePath.Substring(0, lastSlash + 1) : "";
            LlmSubdirTextBox.Text = dir;
        }
        else
        {
            FileNameTextBox.Text = selected.FileName;
        }
    }

    private void OpenHuggingFace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string repoId)
        {
            var url = $"https://huggingface.co/{repoId}";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Failed to open Hugging Face URL: {url}");
            }
        }
    }

    private async void HubDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedRepo = HubRepoListView.SelectedItem as HubRepoViewModel;
        var selectedFile = HubFileListView.SelectedItem as HubFileViewModel;
        if (selectedRepo == null || selectedFile == null) return;

        var destination = _ggufStore.GetDownloadPath(selectedRepo.RepoId, selectedFile.FileName);
        if (File.Exists(destination))
        {
            var overwrite = MessageBox.Show($"'{selectedFile.FileName}' already exists locally. Overwrite?", "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes) return;
        }

        _downloadCts = new CancellationTokenSource();
        SetUIEnabled(false);

        try
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            ProgressStatusTextBlock.Text = $"Downloading {selectedFile.FileName}...";
            DownloadProgressBar.Value = 0;
            ProgressPctTextBlock.Text = "0%";

            var progress = new Progress<(long Received, long Total)>(p =>
            {
                if (p.Total > 0)
                {
                    var pct = (double)p.Received / p.Total * 100;
                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgressBar.Value = pct;
                        ProgressPctTextBlock.Text = $"{pct:F0}%";
                        var mb = p.Received / 1024.0 / 1024.0;
                        var totalMb = p.Total / 1024.0 / 1024.0;
                        ProgressStatusTextBlock.Text = $"Downloading... {mb:F1} / {totalMb:F1} MB";
                    });
                }
            });

            await _hfService.DownloadAsync(selectedRepo.RepoId, selectedFile.FilePath, destination, progress, _downloadCts.Token);

            ProgressStatusTextBlock.Text = "Download completed successfully";
            DownloadProgressBar.Value = 100;
            ProgressPctTextBlock.Text = "100%";
            await RefreshListsAsync();
            MessageBox.Show($"Successfully downloaded '{selectedFile.FileName}'.", "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            ProgressStatusTextBlock.Text = "Download cancelled";
            DownloadProgressBar.Value = 0;
            ProgressPctTextBlock.Text = "";
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, $"Hub download failed: {selectedFile.FilePath}");
            ProgressStatusTextBlock.Text = "Download failed";
            MessageBox.Show($"Download failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
            Dispatcher.Invoke(() => SetUIEnabled(true));
        }
    }

    private void SetUIEnabled(bool enabled)
    {
        WhisperModeButton.IsEnabled = enabled;
        LlmModeButton.IsEnabled = enabled;

        DownloadedListView.IsHitTestVisible = enabled;
        DownloadedListView.Opacity = enabled ? 1.0 : 0.6;
        RecommendedListView.IsHitTestVisible = enabled;
        RecommendedListView.Opacity = enabled ? 1.0 : 0.6;

        RepoIdTextBox.IsEnabled = enabled;
        FileNameTextBox.IsEnabled = enabled;
        LlmSubdirTextBox.IsEnabled = enabled;
        DeleteModelButton.IsEnabled = enabled;
        RefreshDownloadedButton.IsEnabled = enabled;
        DownloadCuratedButton.IsEnabled = enabled;
        DownloadAdvancedButton.IsEnabled = enabled;
        HubSearchTextBox.IsEnabled = enabled;
        HubSearchButton.IsEnabled = enabled;
        HubRepoListView.IsHitTestVisible = enabled;
        HubRepoListView.Opacity = enabled ? 1.0 : 0.6;
        HubFileListView.IsHitTestVisible = enabled;
        HubFileListView.Opacity = enabled ? 1.0 : 0.6;
        HubDownloadButton.IsEnabled = enabled && HubFileListView.SelectedItem != null;
        AdvancedSearchTextBox.IsEnabled = enabled;
        AdvancedSearchButton.IsEnabled = enabled;
        AdvancedRepoListView.IsHitTestVisible = enabled;
        AdvancedRepoListView.Opacity = enabled ? 1.0 : 0.6;
        AdvancedFileListView.IsHitTestVisible = enabled;
        AdvancedFileListView.Opacity = enabled ? 1.0 : 0.6;
    }

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        var headerClicked = e.OriginalSource as GridViewColumnHeader;
        if (headerClicked == null || headerClicked.Column == null)
            return;

        string? headerText = headerClicked.Column.Header as string;
        if (string.IsNullOrEmpty(headerText))
            return;

        var listView = sender as ListView;
        if (listView == null)
            return;

        string? sortBy = GetSortPropertyByHeader(listView, headerText);
        if (string.IsNullOrEmpty(sortBy))
            return;

        var (activeSortBy, activeSortDirection) = GetSortState(listView);

        ListSortDirection direction;
        if (activeSortBy == sortBy)
        {
            direction = activeSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            direction = ListSortDirection.Ascending;
        }

        SetSortState(listView, sortBy, direction);
        Sort(listView, sortBy, direction);
        UpdateHeaderSymbols(listView, headerClicked, direction);
    }

    private string? GetSortPropertyByHeader(ListView listView, string headerText)
    {
        string cleanText = headerText.Replace(" ▲", "").Replace(" ▼", "").Trim();

        if (listView == DownloadedListView)
        {
            return cleanText switch
            {
                "File Name" or "Folder Name" => "FileName",
                "File Path" or "Storage Path" => "RelativePath",
                "Size" => "SizeMegabytes",
                _ => null
            };
        }
        else if (listView == RecommendedListView)
        {
            return cleanText switch
            {
                "Model Name" => "DisplayName",
                "Description" => "Description",
                "Size" => "SizeMegabytes",
                "Status" => "Status",
                _ => null
            };
        }
        else if (listView == HubRepoListView)
        {
            return cleanText switch
            {
                "Model" => "DisplayName",
                "Downloads" => "Downloads",
                "Tags" => "TagsText",
                _ => null
            };
        }
        else if (listView == HubFileListView)
        {
            return cleanText switch
            {
                "File" => "FileName",
                "Size" => "SizeBytes",
                _ => null
            };
        }
        else if (listView == AdvancedRepoListView)
        {
            return cleanText switch
            {
                "Model" => "DisplayName",
                "Downloads" => "Downloads",
                "Tags" => "TagsText",
                _ => null
            };
        }
        else if (listView == AdvancedFileListView)
        {
            return cleanText switch
            {
                "File" => "FileName",
                "Size" => "SizeBytes",
                _ => null
            };
        }

        return null;
    }

    private (string? SortBy, ListSortDirection Direction) GetSortState(ListView listView)
    {
        if (listView == DownloadedListView) return (_downloadedSortBy, _downloadedSortDirection);
        if (listView == RecommendedListView) return (_recommendedSortBy, _recommendedSortDirection);
        if (listView == HubRepoListView) return (_hubRepoSortBy, _hubRepoSortDirection);
        if (listView == HubFileListView) return (_hubFileSortBy, _hubFileSortDirection);
        if (listView == AdvancedRepoListView) return (_advancedRepoSortBy, _advancedRepoSortDirection);
        if (listView == AdvancedFileListView) return (_advancedFileSortBy, _advancedFileSortDirection);
        return (null, ListSortDirection.Ascending);
    }

    private void SetSortState(ListView listView, string sortBy, ListSortDirection direction)
    {
        if (listView == DownloadedListView) { _downloadedSortBy = sortBy; _downloadedSortDirection = direction; }
        else if (listView == RecommendedListView) { _recommendedSortBy = sortBy; _recommendedSortDirection = direction; }
        else if (listView == HubRepoListView) { _hubRepoSortBy = sortBy; _hubRepoSortDirection = direction; }
        else if (listView == HubFileListView) { _hubFileSortBy = sortBy; _hubFileSortDirection = direction; }
        else if (listView == AdvancedRepoListView) { _advancedRepoSortBy = sortBy; _advancedRepoSortDirection = direction; }
        else if (listView == AdvancedFileListView) { _advancedFileSortBy = sortBy; _advancedFileSortDirection = direction; }
    }

    private void Sort(ListView listView, string sortBy, ListSortDirection direction)
    {
        var dataView = CollectionViewSource.GetDefaultView(listView.ItemsSource);
        if (dataView is ListCollectionView listCollectionView)
        {
            listCollectionView.SortDescriptions.Clear();
            listCollectionView.CustomSort = null;

            if (listView == HubFileListView && sortBy == "SizeBytes")
            {
                listCollectionView.CustomSort = new SizeBytesComparer(direction);
            }
            else if (listView == AdvancedFileListView && sortBy == "SizeBytes")
            {
                listCollectionView.CustomSort = new SizeBytesComparer(direction);
            }
            else
            {
                listCollectionView.SortDescriptions.Add(new SortDescription(sortBy, direction));
            }

            listCollectionView.Refresh();
        }
        else if (dataView != null)
        {
            dataView.SortDescriptions.Clear();
            dataView.SortDescriptions.Add(new SortDescription(sortBy, direction));
            dataView.Refresh();
        }
    }

    private void UpdateHeaderSymbols(ListView listView, GridViewColumnHeader clickedHeader, ListSortDirection direction)
    {
        if (listView.View is not GridView gridView)
            return;

        foreach (var column in gridView.Columns)
        {
            if (column.Header is string headerText)
            {
                headerText = headerText.Replace(" ▲", "").Replace(" ▼", "");

                if (column == clickedHeader.Column)
                {
                    headerText += (direction == ListSortDirection.Ascending) ? " ▲" : " ▼";
                }

                column.Header = headerText;
            }
        }
    }

    private void ApplySortIfActive(ListView listView)
    {
        var (sortBy, direction) = GetSortState(listView);
        if (!string.IsNullOrEmpty(sortBy))
        {
            Sort(listView, sortBy, direction);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_downloadCts != null)
        {
            var result = MessageBox.Show("A model download is currently in progress. Closing this window will cancel the download.\n\nClose anyway?", "Cancel Download", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _downloadCts.Cancel();
            }
            else
            {
                e.Cancel = true;
                return;
            }
        }
        _hubSearchCts?.Cancel();
        _hubSearchCts?.Dispose();
        _advancedSearchCts?.Cancel();
        _advancedSearchCts?.Dispose();
        base.OnClosing(e);
    }
}

public class DownloadedModelInfo
{
    public required string FileName { get; set; }
    public required string RelativePath { get; set; }
    public required string SizeDescription { get; set; }
    public required string FullPath { get; set; }
    public double SizeMegabytes { get; set; }
}

public class RecommendedModelInfo
{
    public required string DisplayName { get; set; }
    public required string Description { get; set; }
    public required string RepoId { get; set; }
    public required string FileName { get; set; }
    public required string SizeDescription { get; set; }
    public required string Status { get; set; }
    public double SizeMegabytes { get; set; }
}

public class HubRepoViewModel
{
    public string RepoId { get; }
    public string DisplayName { get; }
    public long Downloads { get; }
    public string DownloadsText { get; }
    public string TagsText { get; }

    public HubRepoViewModel(HfRepoInfo repo)
    {
        RepoId = repo.RepoId;
        DisplayName = repo.DisplayName;
        Downloads = repo.Downloads;
        DownloadsText = repo.Downloads >= 1000 ? $"{repo.Downloads / 1000.0:F1}k" : repo.Downloads.ToString();
        TagsText = string.Join(", ", repo.Tags.Where(t => !t.Equals("gguf", StringComparison.OrdinalIgnoreCase)).Take(5));
    }
}

public class HubFileViewModel
{
    public string FilePath { get; }
    public string FileName { get; }
    public string SizeText { get; }
    public long? SizeBytes { get; }

    public HubFileViewModel(string filePath, long? sizeBytes)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        SizeBytes = sizeBytes;
        if (sizeBytes.HasValue)
        {
            var mb = sizeBytes.Value / 1024.0 / 1024.0;
            SizeText = mb >= 1024 ? $"{mb / 1024:F1} GB" : $"{mb:F0} MB";
        }
        else
        {
            SizeText = "Unknown";
        }
    }
}

public class SizeBytesComparer : IComparer
{
    private readonly ListSortDirection _direction;
    public SizeBytesComparer(ListSortDirection direction) => _direction = direction;

    public int Compare(object? x, object? y)
    {
        if (x is not HubFileViewModel a || y is not HubFileViewModel b)
            return 0;

        var av = a.SizeBytes ?? long.MaxValue;
        var bv = b.SizeBytes ?? long.MaxValue;
        var result = av.CompareTo(bv);
        return _direction == ListSortDirection.Ascending ? result : -result;
    }
}
