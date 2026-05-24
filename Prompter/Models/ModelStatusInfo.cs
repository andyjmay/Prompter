namespace Prompter.Models;

public record ModelStatusInfo
{
    public string Alias { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsCached { get; init; }
    public bool IsLoaded { get; init; }
    public string SizeDescription { get; init; } = "Unknown";
    public float? SizeInMegabytes { get; init; }
    public string TaskType { get; init; } = "Unknown";
}
