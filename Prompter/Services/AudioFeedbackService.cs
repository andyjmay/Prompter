using System.Runtime.InteropServices;

namespace Prompter.Services;

public class AudioFeedbackService
{
    private readonly ConfigService _configService;

    public AudioFeedbackService(ConfigService configService)
    {
        _configService = configService;
    }

    public void PlayStart()
    {
        if (_configService.Load().AudioFeedbackEnabled)
            MessageBeep(0x00000040); // MB_ICONINFORMATION
    }

    public void PlayStop()
    {
        if (_configService.Load().AudioFeedbackEnabled)
            MessageBeep(0x00000000); // MB_OK
    }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);
}
