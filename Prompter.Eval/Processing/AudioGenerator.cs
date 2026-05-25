using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;

namespace Prompter.Eval.Processing;

public static class AudioGenerator
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Prompter",
        "eval",
        "cache");

    private static readonly object _lock = new();

    public static string EnsureAudioFile(string phrase)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(phrase)))[..16];
        var path = Path.Combine(CacheDir, $"tts-{hash}-16k.wav");

        lock (_lock)
        {
            if (File.Exists(path))
                return path;

            Directory.CreateDirectory(CacheDir);
            var tempPath = Path.Combine(CacheDir, $"tts-{hash}-temp-{Guid.NewGuid()}.wav");

            using var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer();
            synthesizer.SetOutputToWaveFile(tempPath);
            synthesizer.Speak(phrase);
            synthesizer.SetOutputToDefaultAudioDevice();

            try
            {
                using (var reader = new WaveFileReader(tempPath))
                {
                    var targetFormat = new WaveFormat(16000, 16, 1);
                    using var resampler = new MediaFoundationResampler(reader, targetFormat);
                    WaveFileWriter.CreateWaveFile(path, resampler);
                }
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        return path;
    }
}
