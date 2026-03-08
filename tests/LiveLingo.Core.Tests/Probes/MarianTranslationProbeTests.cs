using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests.Probes;

public sealed class MarianTranslationProbeTests
{
    [Fact]
    [Trait("Category", "TranslationProbe")]
    public async Task Translate_ZhToEn_KnownPhrase_ProducesExpectedText()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_TRANSLATION_PROBE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var sourceText = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_SOURCE_TEXT") ?? "你好";
        var expectedContains = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_EXPECTED_CONTAINS") ?? "hello";
        var modelPath = ResolveModelPath();

        var options = Options.Create(new CoreOptions
        {
            ModelStoragePath = modelPath
        });

        using var http = new HttpClient();
        var modelManager = new ModelManager(options, http, NullLogger<ModelManager>.Instance);
        using var engine = new MarianOnnxEngine(modelManager, NullLogger<MarianOnnxEngine>.Instance);

        var translated = await engine.TranslateAsync(sourceText, "zh", "en", CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(translated));
        Assert.DoesNotContain("Bor Bor", translated, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedContains, translated, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "TranslationProbe")]
    public async Task Translate_ZhToEn_BatchCases_ProducesExpectedText()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_TRANSLATION_PROBE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var cases = ParseProbeCases(
            Environment.GetEnvironmentVariable("LIVELINGO_PROBE_CASES") ??
            "你好=>hello;谢谢=>thank;早上好=>morning");
        Assert.NotEmpty(cases);

        var modelPath = ResolveModelPath();
        var options = Options.Create(new CoreOptions
        {
            ModelStoragePath = modelPath
        });

        using var http = new HttpClient();
        var modelManager = new ModelManager(options, http, NullLogger<ModelManager>.Instance);
        using var engine = new MarianOnnxEngine(modelManager, NullLogger<MarianOnnxEngine>.Instance);

        foreach (var (sourceText, expectedContains) in cases)
        {
            var translated = await engine.TranslateAsync(sourceText, "zh", "en", CancellationToken.None);
            Assert.False(string.IsNullOrWhiteSpace(translated));
            Assert.DoesNotContain("Bor Bor", translated, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedContains, translated, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<(string SourceText, string ExpectedContains)> ParseProbeCases(string raw)
    {
        var parsed = new List<(string SourceText, string ExpectedContains)>();
        var segments = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var pair = segment.Split("=>", 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]) || string.IsNullOrWhiteSpace(pair[1]))
                continue;
            parsed.Add((pair[0], pair[1]));
        }

        return parsed;
    }

    private static string ResolveModelPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveLingo",
            "models");
    }
}
