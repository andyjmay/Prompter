using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
        {
            // Play an elegant rising double-tone chime (E5 -> B5)
            PlayChime(659.25, 987.77);
        }
    }

    public void PlayStop()
    {
        if (_configService.Load().AudioFeedbackEnabled)
        {
            // Play a gentle falling double-tone chime (B5 -> E5)
            PlayChime(987.77, 659.25);
        }
    }

    private void PlayChime(double freq1, double freq2)
    {
        try
        {
            // Synthesize the two notes using NAudio SignalGenerator
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

            // Sequence them together seamlessly
            var chime = new ConcatenatingSampleProvider(new[] { firstTone, secondTone });

            var waveOut = new WaveOutEvent();
            waveOut.Init(chime);
            waveOut.Play();

            // Auto-dispose when finished playing
            waveOut.PlaybackStopped += (s, e) =>
            {
                waveOut.Dispose();
            };
        }
        catch
        {
            // Audio feedback is best-effort; suppress errors
        }
    }
}
