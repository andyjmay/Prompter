namespace Prompter.Models;

public class ModelStatusInfo
{
    public string Alias { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsCached { get; set; }
    public bool IsLoaded { get; set; }
    public string SizeDescription { get; set; } = "Unknown";
    public string TaskType { get; set; } = "Unknown";
}
