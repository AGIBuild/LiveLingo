using System;
using LLama;
using LLama.Common;
using LLama.Native;

NativeLogConfig.llama_log_set((level, msg) => {
    Console.Write(msg);
});

var modelPath = "/Users/Hongwei.Xi/Library/Application Support/LiveLingo/models/qwen35-9b/Qwen3.5-9B-abliterated-Q4_K_M.gguf";

try {
    var parameters = new ModelParams(modelPath)
    {
        ContextSize = 2048,
        GpuLayerCount = 0
    };
    using var weights = LLamaWeights.LoadFromFile(parameters);
    Console.WriteLine("Loaded successfully!");
} catch (Exception ex) {
    Console.WriteLine("Exception: " + ex.Message);
}
