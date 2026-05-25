using System.IO;

namespace Prompter.Services;

public class GgufModelStore : IGgufModelStore
{
    public string BaseDirectory { get; }

    public GgufModelStore()
    {
        BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prompter", "models", "gguf-chat");
    }

    public void EnsureDirectoryExists()
    {
        if (!Directory.Exists(BaseDirectory))
            Directory.CreateDirectory(BaseDirectory);
    }

    public string GetDownloadPath(string repoId, string fileName)
    {
        var author = repoId.Split('/').FirstOrDefault() ?? "unknown";
        var dir = Path.Combine(BaseDirectory, author);
        return Path.Combine(dir, fileName);
    }

    public Task<List<InstalledGgufInfo>> GetInstalledModelsAsync(CancellationToken ct)
    {
        var results = new List<InstalledGgufInfo>();
        if (!Directory.Exists(BaseDirectory))
            return Task.FromResult(results);

        foreach (var file in Directory.GetFiles(BaseDirectory, "*.gguf", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            results.Add(new InstalledGgufInfo(info.Name, file, null, info.Length));
        }

        return Task.FromResult(results);
    }
}
