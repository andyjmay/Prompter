namespace Prompter.Services;

public record InstalledGgufInfo(string FileName, string FullPath, string? RepoId, long FileSizeBytes);

public interface IGgufModelStore
{
    string BaseDirectory { get; }
    void EnsureDirectoryExists();
    string GetDownloadPath(string repoId, string fileName);
    Task<List<InstalledGgufInfo>> GetInstalledModelsAsync(CancellationToken ct);
}
