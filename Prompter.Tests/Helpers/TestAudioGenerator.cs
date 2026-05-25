using System.IO;
using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;

namespace Prompter.Tests.Helpers;

public static class TestAudioGenerator
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Prompter",
        "tests",
        "cache");

    private static readonly object _lock = new();

    /// <summary>
    /// Generates a 16 kHz mono WAV file containing spoken text via Windows TTS.
    /// The file is cached keyed by the phrase hash so subsequent calls are instant.
    /// </summary>
    public static string EnsureAudioFile(string phrase = "hello world, this is a test")
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
            // Release the file handle so downstream readers can open it immediately.
            synthesizer.SetOutputToDefaultAudioDevice();

            // Whisper.net requires 16 kHz mono. Resample the TTS output.
            using (var reader = new WaveFileReader(tempPath))
            {
                var targetFormat = new WaveFormat(16000, 16, 1);
                using var resampler = new MediaFoundationResampler(reader, targetFormat);
                WaveFileWriter.CreateWaveFile(path, resampler);
            }

            try { File.Delete(tempPath); } catch { }
        }

        return path;
    }
}
