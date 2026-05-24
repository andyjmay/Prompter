using System.Diagnostics;
using Microsoft.Win32;

namespace Prompter.Services;

public class StartupService : IStartupService
{
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Prompter";

    private readonly string _executablePath;
    private readonly IFileLogger _logger;

    public StartupService(IFileLogger logger)
    {
        _logger = logger;
        _executablePath = Process.GetCurrentProcess().MainModule?.FileName
                          ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (_executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            _executablePath = _executablePath[..^4] + ".exe";
        }
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
            var value = key?.GetValue(AppName) as string;
            if (string.IsNullOrEmpty(value)) return false;

            var storedPath = value.Trim('"');
            return storedPath.Equals(_executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "StartupService.IsEnabled");
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true)
                ?? Registry.CurrentUser.CreateSubKey(RegistryKey);

            if (enabled)
            {
                key.SetValue(AppName, $"\"{_executablePath}\"");
            }
            else
            {
                if (key.GetValue(AppName) != null)
                    key.DeleteValue(AppName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "StartupService.SetEnabled");
            throw new InvalidOperationException("Failed to update Windows startup registry. You may need to run Prompter as administrator.", ex);
        }
    }
}
