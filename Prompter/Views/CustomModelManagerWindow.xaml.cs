using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Prompter.Services;

namespace Prompter.Views;

public partial class CustomModelManagerWindow : Window
{
    private readonly IConfigService _configService;
    private readonly IFileLogger _logger;
    private readonly string _whisperDir;
    private readonly string _foundryDir;
    private static readonly HttpClient _httpClient = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });

    static CustomModelManagerWindow()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Prompter/1.0");
    }
    private CancellationTokenSource? _downloadCts;
    private bool _isLlmMode = false;

    private readonly ObservableCollection<DownloadedModelInfo> _downloadedModels = new();
    private readonly ObservableCollection<RecommendedModelInfo> _recommendedModels = new();

    public CustomModelManagerWindow(IConfigService configService, IFileLogger logger)
    {
        InitializeComponent();
        _configService = configService;
        _logger = logger;

        _whisperDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prompter", "models", "ggml");

        _foundryDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "AI.Foundry.Local", "models");

        DownloadedListView.ItemsSource = _downloadedModels;
        RecommendedListView.ItemsSource = _recommendedModels;

        Loaded += CustomModelManagerWindow_Loaded;
    }

    private void CustomModelManagerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateUIMode();
    }

    private void ModeButton_Checked(object sender, RoutedEventArgs e)
    {
        if (WhisperModeButton == null || LlmModeButton == null) return;
        _isLlmMode = LlmModeButton.IsChecked == true;
        UpdateUIMode();
    }

    private void UpdateUIMode()
    {
        if (_downloadCts != null) return; // Ignore if downloading

        // Configure lists and UI based on Mode
        if (_isLlmMode)
        {
            AdvancedTitleTextBlock.Text = "Download any ONNX Text model from Hugging Face";
            AdvancedDescTextBlock.Text = "Enter the repository ID of the ONNX GenAI model. The downloader will scan for the genai_config.json file and fetch all files.";
            RepoIdTextBox.Text = "Qwen/Qwen2.5-0.5B-Instruct-ONNX";
            FileNameContainer.Visibility = Visibility.Collapsed;
            LlmDownloadInfoContainer.Visibility = Visibility.Visible;

            InitializeRecommendedLlms();
        }
        else
        {
            AdvancedTitleTextBlock.Text = "Download any GGML Whisper model from Hugging Face";
            AdvancedDescTextBlock.Text = "Enter the repository ID and file name of the model to download. Ensure it is in the GGML format (.bin).";
            RepoIdTextBox.Text = "distil-whisper/distil-large-v3-ggml";
            FileNameTextBox.Text = "ggml-distil-large-v3.bin";
            FileNameContainer.Visibility = Visibility.Visible;
            LlmDownloadInfoContainer.Visibility = Visibility.Collapsed;

            InitializeRecommendedWhispers();
        }

        ConfigureDownloadedColumns();
        RefreshLists();
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
            Status = "Checking..."
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Whisper Large v3 Turbo",
            Description = "Fastest full-sized model. Multilingual support.",
            RepoId = "ggerganov/whisper.cpp",
            FileName = "ggml-large-v3-turbo.bin",
            SizeDescription = "~809 MB",
            Status = "Checking..."
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Whisper Small (English)",
            Description = "Balanced lightweight model for English only.",
            RepoId = "ggerganov/whisper.cpp",
            FileName = "ggml-small.en.bin",
            SizeDescription = "~463 MB",
            Status = "Checking..."
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Whisper Base (English)",
            Description = "Extremely fast, low resource English-only model.",
            RepoId = "ggerganov/whisper.cpp",
            FileName = "ggml-base.en.bin",
            SizeDescription = "~141 MB",
            Status = "Checking..."
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
            Status = "Checking..."
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Qwen 2.5 1.5B Instruct",
            Description = "Strong balance of speed and correction quality (INT4).",
            RepoId = "Qwen/Qwen2.5-1.5B-Instruct-ONNX",
            FileName = "onnx/cpu_and_mobile/cpu-int4-rtn-block-32",
            SizeDescription = "~950 MB",
            Status = "Checking..."
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Phi-3.5 Mini Instruct",
            Description = "Highly accurate Microsoft model. Requires GPU/Strong NPU (INT4).",
            RepoId = "microsoft/Phi-3.5-mini-instruct-onnx",
            FileName = "onnx/cpu_and_mobile/cpu-int4-rtn-block-32",
            SizeDescription = "~2.2 GB",
            Status = "Checking..."
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Gemma 2 2B Instruct",
            Description = "Strong Google model, optimized for text correction (INT4).",
            RepoId = "keisuke-miyako/gemma-2-2B-it-onnx-int4",
            FileName = "",
            SizeDescription = "~1.6 GB",
            Status = "Checking..."
        });
        _recommendedModels.Add(new RecommendedModelInfo
        {
            DisplayName = "Gemma 3 4B Instruct",
            Description = "Google's latest state-of-the-art model. High accuracy (INT4).",
            RepoId = "onnxruntime/Gemma-3-ONNX",
            FileName = "gemma-3-4b-it/cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4",
            SizeDescription = "~2.5 GB",
            Status = "Checking..."
        });
    }

    private void RefreshLists()
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
                        FullPath = dir
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
                        FullPath = file
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

    private void RefreshDownloadedButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshLists();
    }

    private void DeleteModelButton_Click(object sender, RoutedEventArgs e)
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

                if (Directory.Exists(selected.FullPath))
                {
                    Directory.Delete(selected.FullPath, true);
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

            RefreshLists();
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

            var url = $"https://huggingface.co/{repoId}/resolve/main/{fileName}";
            var tempPath = Path.Combine(_whisperDir, fileName + ".tmp");
            var targetPath = Path.Combine(_whisperDir, fileName);

            ProgressStatusTextBlock.Text = "Connecting to Hugging Face...";
            DownloadProgressBar.Value = 0;
            ProgressPctTextBlock.Text = "0%";

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using var contentStream = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            var bytesRead = 0L;
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, _downloadCts.Token);
                if (read == 0) break;

                await fileStream.WriteAsync(buffer, 0, read, _downloadCts.Token);
                bytesRead += read;

                if (canReportProgress)
                {
                    var pct = (double)bytesRead / totalBytes * 100;
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    var speed = elapsed > 0 ? (bytesRead / 1024.0 / 1024.0) / elapsed : 0;

                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgressBar.Value = pct;
                        ProgressPctTextBlock.Text = $"{pct:F0}%";
                        ProgressStatusTextBlock.Text = $"Downloading {fileName} ({speed:F1} MB/s)";
                    });
                }
            }

            await fileStream.FlushAsync(_downloadCts.Token);
            fileStream.Close();

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            ProgressStatusTextBlock.Text = "Download complete ✓";
            DownloadProgressBar.Value = 100;
            ProgressPctTextBlock.Text = "100%";

            RefreshLists();
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
            SetUIEnabled(true);
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
            var jsonResponse = await _httpClient.GetStringAsync(apiUrl, _downloadCts.Token);
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

                var downloadUrl = $"https://huggingface.co/{repoId}/resolve/main/{sibling.rpath}";
                var tempFilePath = fileTargetPath + ".tmp";

                ProgressStatusTextBlock.Text = $"Downloading file {count} of {filesToDownload.Count}: {relativeFileName}...";
                DownloadProgressBar.Value = (double)(count - 1) / filesToDownload.Count * 100;
                ProgressPctTextBlock.Text = $"{DownloadProgressBar.Value:F0}%";

                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                using var contentStream = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
                using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                var bytesRead = 0L;

                while (true)
                {
                    var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, _downloadCts.Token);
                    if (read == 0) break;

                    await fileStream.WriteAsync(buffer, 0, read, _downloadCts.Token);
                    bytesRead += read;

                    if (canReportProgress)
                    {
                        var filePct = (double)bytesRead / totalBytes;
                        var overallPct = (((double)(count - 1) + filePct) / filesToDownload.Count) * 100;
                        Dispatcher.Invoke(() =>
                        {
                            DownloadProgressBar.Value = overallPct;
                            ProgressPctTextBlock.Text = $"{overallPct:F0}%";
                            ProgressStatusTextBlock.Text = $"Downloading file {count} of {filesToDownload.Count}: {relativeFileName} ({filePct * 100:F0}%)";
                        });
                    }
                }

                await fileStream.FlushAsync(_downloadCts.Token);
                fileStream.Close();

                if (File.Exists(fileTargetPath))
                {
                    File.Delete(fileTargetPath);
                }
                File.Move(tempFilePath, fileTargetPath);
            }

            // Write the inference_model.json template automatically based on name autodetection
            WriteInferenceModelJsonTemplate(targetDir, modelAlias);

            ProgressStatusTextBlock.Text = "All files downloaded successfully ✓";
            DownloadProgressBar.Value = 100;
            ProgressPctTextBlock.Text = "100%";

            RefreshLists();
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
            SetUIEnabled(true);
        }
    }

    private void WriteInferenceModelJsonTemplate(string folderPath, string modelAlias)
    {
        try
        {
            var jsonPath = Path.Combine(folderPath, "inference_model.json");
            if (File.Exists(jsonPath)) return; // Don't overwrite if model already has one

            var template = new
            {
                model_type = "chat",
                prompt_template = new Dictionary<string, string>()
            };

            var alias = modelAlias.ToLowerInvariant();
            if (alias.Contains("qwen"))
            {
                template.prompt_template["system"] = "<|im_start|>system\n{content}<|im_end|>\n";
                template.prompt_template["user"] = "<|im_start|>user\n{content}<|im_end|>\n";
                template.prompt_template["assistant"] = "<|im_start|>assistant\n{content}<|im_end|>\n";
            }
            else if (alias.Contains("phi"))
            {
                template.prompt_template["system"] = "<|system|>\n{content}<|end|>\n";
                template.prompt_template["user"] = "<|user|>\n{content}<|end|>\n";
                template.prompt_template["assistant"] = "<|assistant|>\n{content}<|end|>\n";
            }
            else if (alias.Contains("llama"))
            {
                template.prompt_template["system"] = "<|start_header_id|>system<|end_header_id|>\n\n{content}<|eot_id|>";
                template.prompt_template["user"] = "<|start_header_id|>user<|end_header_id|>\n\n{content}<|eot_id|>";
                template.prompt_template["assistant"] = "<|start_header_id|>assistant<|end_header_id|>\n\n{content}<|eot_id|>";
            }
            else if (alias.Contains("gemma"))
            {
                template.prompt_template["system"] = "<start_of_turn>user\nSystem instructions: {content}<end_of_turn>\n";
                template.prompt_template["user"] = "<start_of_turn>user\n{content}<end_of_turn>\n";
                template.prompt_template["assistant"] = "<start_of_turn>model\n{content}<end_of_turn>\n";
            }
            else
            {
                // Fallback to standard ChatML
                template.prompt_template["system"] = "<|im_start|>system\n{content}<|im_end|>\n";
                template.prompt_template["user"] = "<|im_start|>user\n{content}<|im_end|>\n";
                template.prompt_template["assistant"] = "<|im_start|>assistant\n{content}<|im_end|>\n";
            }

            var jsonText = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonPath, jsonText);
            _logger.Log($"Wrote auto-detected chat template to: {jsonPath}");
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Failed to write inference_model.json template");
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
        base.OnClosing(e);
    }
}

public class DownloadedModelInfo
{
    public required string FileName { get; set; }
    public required string RelativePath { get; set; }
    public required string SizeDescription { get; set; }
    public required string FullPath { get; set; }
}

public class RecommendedModelInfo
{
    public required string DisplayName { get; set; }
    public required string Description { get; set; }
    public required string RepoId { get; set; }
    public required string FileName { get; set; }
    public required string SizeDescription { get; set; }
    public required string Status { get; set; }
}

