using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Prompter.Services;

public class HuggingFaceService : IHuggingFaceService
{
    private readonly IConfigService _configService;
    private readonly IFileLogger _logger;
    private readonly HttpClient _httpClient;

    public HuggingFaceService(IConfigService configService, IFileLogger logger)
    {
        _configService = configService;
        _logger = logger;
        _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Prompter/1.0");
    }

    private void ApplyToken(HttpRequestMessage request)
    {
        var token = _configService.Load().HuggingFaceToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<List<HfRepoInfo>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var url = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(query)}&limit={limit}&sort=downloads&direction=-1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyToken(request);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var items = JsonSerializer.Deserialize<List<HfApiModelItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var results = new List<HfRepoInfo>();
        if (items == null) return results;

        foreach (var item in items)
        {
            var tags = item.tags ?? new List<string>();
            if (!tags.Any(t => t.Equals("gguf", StringComparison.OrdinalIgnoreCase)))
                continue;

            results.Add(new HfRepoInfo(
                item.id ?? "",
                item.id?.Split('/').LastOrDefault() ?? item.id ?? "",
                item.downloads,
                tags));
        }

        return results;
    }

    public async Task<List<HfRepoInfo>> SearchRepositoriesAsync(string query, int limit, CancellationToken ct)
    {
        var url = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(query)}&limit={limit}&sort=downloads&direction=-1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyToken(request);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var items = JsonSerializer.Deserialize<List<HfApiModelItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var results = new List<HfRepoInfo>();
        if (items == null) return results;

        foreach (var item in items)
        {
            var tags = item.tags ?? new List<string>();
            results.Add(new HfRepoInfo(
                item.id ?? "",
                item.id?.Split('/').LastOrDefault() ?? item.id ?? "",
                item.downloads,
                tags));
        }

        return results;
    }

    public async Task<List<HfFileInfo>> ListGgufFilesAsync(string repoId, CancellationToken ct)
    {
        var url = $"https://huggingface.co/api/models/{repoId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyToken(request);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new List<HfFileInfo>();

        var json = await response.Content.ReadAsStringAsync(ct);
        var repoInfo = JsonSerializer.Deserialize<HfApiRepoInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var results = new List<HfFileInfo>();
        if (repoInfo?.siblings == null) return results;

        foreach (var sibling in repoInfo.siblings)
        {
            var path = sibling.rfilename ?? "";
            if (path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new HfFileInfo(path, null));
            }
        }

        return results;
    }

    public async Task<List<HfFileInfo>> ListRepoFilesAsync(string repoId, CancellationToken ct)
    {
        var url = $"https://huggingface.co/api/models/{repoId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyToken(request);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new List<HfFileInfo>();

        var json = await response.Content.ReadAsStringAsync(ct);
        var repoInfo = JsonSerializer.Deserialize<HfApiRepoInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var results = new List<HfFileInfo>();
        if (repoInfo?.siblings == null) return results;

        foreach (var sibling in repoInfo.siblings)
        {
            var path = sibling.rfilename ?? "";
            if (!string.IsNullOrEmpty(path))
            {
                results.Add(new HfFileInfo(path, null));
            }
        }

        return results;
    }

    public async Task<long?> GetFileSizeAsync(string repoId, string filePath, CancellationToken ct)
    {
        var url = $"https://huggingface.co/{repoId}/resolve/main/{filePath}";
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        ApplyToken(request);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return response.Content.Headers.ContentLength;
        return null;
    }

    public async Task DownloadAsync(string repoId, string filePath, string destination, IProgress<(long Received, long Total)>? progress, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var url = $"https://huggingface.co/{repoId}/resolve/main/{filePath}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyToken(request);

        long existing = 0;
        var tempPath = destination + ".tmp";
        if (File.Exists(tempPath))
        {
            existing = new FileInfo(tempPath).Length;
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        if (response.StatusCode == HttpStatusCode.PartialContent && existing > 0 && total > 0)
        {
            total += existing;
        }
        else if (total < 0 && existing > 0)
        {
            File.Delete(tempPath);
            existing = 0;
        }
        else if (existing > 0)
        {
            File.Delete(tempPath);
            existing = 0;
        }

        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        var fileMode = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent ? FileMode.Append : FileMode.Create;
        using var fileStream = new FileStream(tempPath, fileMode, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long received = existing;
        while (true)
        {
            var read = await contentStream.ReadAsync(buffer, ct);
            if (read == 0) break;
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;

            if (total > 0)
            {
                progress?.Report((received, total));
            }
        }

        await fileStream.FlushAsync(ct);
        fileStream.Close();

        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(tempPath, destination);
    }

    private class HfApiModelItem
    {
        public string id { get; set; } = "";
        public List<string>? tags { get; set; }
        public long downloads { get; set; }
    }

    private class HfApiRepoInfo
    {
        public List<HfApiSibling>? siblings { get; set; }
    }

    private class HfApiSibling
    {
        public string rfilename { get; set; } = "";
    }
}
