using LiveLingo.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Silero VAD v5 ONNX inference wrapper.
/// Processes 512-sample frames (32ms at 16kHz) and returns speech probability.
/// </summary>
public sealed class SileroVadDetector : IVoiceActivityDetector
{
    public const int SampleRate = 16000;
    public const int WindowSize = 512;
    private const int ContextSize = 64;
    private const int StateSize = 128;

    private readonly IModelManager _modelManager;
    private InferenceSession? _session;
    private float[] _state;
    private float[] _context;

    public SileroVadDetector(IModelManager modelManager)
    {
        _modelManager = modelManager;
        _state = new float[2 * 1 * StateSize];
        _context = new float[ContextSize];
    }

    public float ProcessFrame(float[] samples)
    {
        if (samples.Length != WindowSize)
            throw new ArgumentException($"Expected {WindowSize} samples, got {samples.Length}.");

        EnsureSession();

        var input = new float[ContextSize + WindowSize];
        Array.Copy(_context, 0, input, 0, ContextSize);
        Array.Copy(samples, 0, input, ContextSize, WindowSize);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input",
                new DenseTensor<float>(input, [1, input.Length])),
            NamedOnnxValue.CreateFromTensor("sr",
                new DenseTensor<long>(new long[] { SampleRate }, [1])),
            NamedOnnxValue.CreateFromTensor("state",
                new DenseTensor<float>((float[])_state.Clone(), [2, 1, StateSize]))
        };

        using var outputs = _session!.Run(inputs);

        var output = outputs.First(o => o.Name == "output").AsTensor<float>();
        var newState = outputs.First(o => o.Name == "stateN").AsTensor<float>();

        Buffer.BlockCopy(newState.ToArray(), 0, _state, 0, _state.Length * sizeof(float));
        Array.Copy(input, input.Length - ContextSize, _context, 0, ContextSize);

        return output[0, 0];
    }

    public void Reset()
    {
        Array.Clear(_state);
        Array.Clear(_context);
    }

    private void EnsureSession()
    {
        if (_session is not null) return;

        var vadModel = ModelRegistry.AllModels
            .FirstOrDefault(m => m.Type == ModelType.VoiceActivityDetection)
            ?? throw new InvalidOperationException("No VAD model defined in registry.");

        var modelDir = _modelManager.GetModelDirectory(vadModel.Id);
        var modelPath = Path.Combine(modelDir, Path.GetFileName(vadModel.DownloadUrl));

        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"VAD model file not found at {modelPath}. Download it first.", modelPath);

        var opts = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1,
            EnableCpuMemArena = true
        };
        _session = new InferenceSession(modelPath, opts);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
