using System.Timers;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Prompter.Services;

public class AudioFeedbackService : IAudioFeedbackService
{
    private readonly IConfigService _configService;

    public AudioFeedbackService(IConfigService configService)
    {
        _configService = configService;
    }

    public void PlayStart()
    {
        if (_configService.Load().AudioFeedbackEnabled)
        {
            PlayChime(659.25, 987.77);
        }
    }

    public void PlayStop()
    {
        if (_configService.Load().AudioFeedbackEnabled)
        {
            PlayChime(987.77, 659.25);
        }
    }

    private static void PlayChime(double freq1, double freq2)
    {
        WaveOutEvent? waveOut = null;
        System.Timers.Timer? fallbackTimer = null;
        try
        {
            var firstTone = new SignalGenerator(44100, 1)
            {
                Type = SignalGeneratorType.Sin,
                Frequency = freq1,
                Gain = 0.15
            }.Take(TimeSpan.FromMilliseconds(70));

            var secondTone = new SignalGenerator(44100, 1)
            {
                Type = SignalGeneratorType.Sin,
                Frequency = freq2,
                Gain = 0.15
            }.Take(TimeSpan.FromMilliseconds(160));

            var chime = new ConcatenatingSampleProvider(new[] { firstTone, secondTone });

            waveOut = new WaveOutEvent();
            waveOut.Init(chime);

            bool disposed = false;
            fallbackTimer = new System.Timers.Timer(2000);
            fallbackTimer.Elapsed += (_, _) =>
            {
                if (!disposed)
                {
                    disposed = true;
                    waveOut?.Stop();
                    waveOut?.Dispose();
                    fallbackTimer?.Dispose();
                }
            };
            fallbackTimer.AutoReset = false;
            fallbackTimer.Start();

            waveOut.PlaybackStopped += (_, _) =>
            {
                if (!disposed)
                {
                    disposed = true;
                    fallbackTimer?.Stop();
                    fallbackTimer?.Dispose();
                    waveOut?.Dispose();
                }
            };

            waveOut.Play();
        }
        catch
        {
            waveOut?.Dispose();
            fallbackTimer?.Dispose();
        }
    }
}
