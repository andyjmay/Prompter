namespace Prompter.Services;

public record HfRepoInfo(string RepoId, string DisplayName, long Downloads, IReadOnlyList<string> Tags);
public record HfFileInfo(string FilePath, long? SizeBytes);

public interface IHuggingFaceService
{
    Task<List<HfRepoInfo>> SearchAsync(string query, int limit, CancellationToken ct);
    Task<List<HfRepoInfo>> SearchRepositoriesAsync(string query, int limit, CancellationToken ct);
    Task<List<HfFileInfo>> ListGgufFilesAsync(string repoId, CancellationToken ct);
    Task<List<HfFileInfo>> ListRepoFilesAsync(string repoId, CancellationToken ct);
    Task<long?> GetFileSizeAsync(string repoId, string filePath, CancellationToken ct);
    Task DownloadAsync(string repoId, string filePath, string destination, IProgress<(long Received, long Total)>? progress, CancellationToken ct);
}
