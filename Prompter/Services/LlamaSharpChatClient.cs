using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace Prompter.Services;

public class LlamaSharpChatClient : IChatClient
{
    private readonly string _modelPath;
    private readonly IFileLogger _fileLogger;
    private LLamaWeights? _weights;
    private ModelParams? _modelParams;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public LlamaSharpChatClient(string modelPath, IFileLogger fileLogger)
    {
        _modelPath = modelPath;
        _fileLogger = fileLogger;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LlamaSharpChatClient));
            if (_weights != null) return;

            _fileLogger.Log($"Loading LlamaSharp model from: {_modelPath}");

            var parameters = new ModelParams(_modelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = -1
            };

            try
            {
                _weights = await LLamaWeights.LoadFromFileAsync(parameters, ct);
                _modelParams = parameters;
                _fileLogger.Log("LlamaSharp model loaded with GPU offload.");
            }
            catch (Exception ex) when (parameters.GpuLayerCount != 0)
            {
                _fileLogger.LogException(ex, "LlamaSharp GPU init failed; falling back to CPU");
                parameters.GpuLayerCount = 0;
                _weights = await LLamaWeights.LoadFromFileAsync(parameters, ct);
                _modelParams = parameters;
                _fileLogger.Log("LlamaSharp model loaded on CPU.");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> CompleteAsync(List<ChatMessage> messages, float temperature, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LlamaSharpChatClient));
            if (_weights == null || _modelParams == null)
                throw new InvalidOperationException("Model not loaded. Call LoadAsync first.");

            var chatHistory = new ChatHistory();
            foreach (var msg in messages)
            {
                if (Enum.TryParse<AuthorRole>(msg.Role, ignoreCase: true, out var role))
                {
                    chatHistory.AddMessage(role, msg.Content);
                }
                else
                {
                    chatHistory.AddMessage(AuthorRole.User, msg.Content);
                }
            }

            string prompt;
            try
            {
                var template = new LLamaTemplate(_weights.NativeHandle);
                foreach (var msg in chatHistory.Messages)
                {
                    template.Add(msg.AuthorRole.ToString(), msg.Content);
                }
                prompt = Encoding.UTF8.GetString(template.Apply());
            }
            catch (Exception ex)
            {
                _fileLogger.LogException(ex, "LLamaTemplate.Apply failed; using ChatML fallback");
                var sb = new StringBuilder();
                foreach (var msg in messages)
                {
                    sb.AppendLine($"<|im_start|>{msg.Role}");
                    sb.AppendLine(msg.Content);
                    sb.AppendLine("<|im_end|>");
                }
                sb.AppendLine("<|im_start|>assistant");
                prompt = sb.ToString();
            }

            var executor = new StatelessExecutor(_weights, _modelParams!);
            var inferenceParams = new InferenceParams
            {
                MaxTokens = 2048,
                AntiPrompts = new List<string> { "<|im_end|>", "<|eot_id|>" },
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = temperature, Seed = 42 }
            };

            var sbResult = new StringBuilder();
            await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct))
            {
                sbResult.Append(token);
            }

            var result = sbResult.ToString().Trim();
            _fileLogger.Log($"LlamaSharp completion result length: {result.Length}");
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _disposed = true;
            _weights?.Dispose();
            _weights = null;
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}
