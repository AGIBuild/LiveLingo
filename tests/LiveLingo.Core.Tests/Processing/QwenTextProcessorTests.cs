using LLama.Common;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Processing;

public class QwenTextProcessorTests
{
    [Fact]
    public void InferenceParams_DoesNotUseBlankLineAsStopToken()
    {
        using var host = CreateHost();
        var processor = new TestProcessor(host);

        var inference = processor.GetInferenceParams();

        Assert.DoesNotContain("\n\n", inference.AntiPrompts);
        Assert.Contains("</s>", inference.AntiPrompts);
        Assert.Contains("<|im_end|>", inference.AntiPrompts);
    }

    private static QwenModelHost CreateHost()
    {
        var opts = Options.Create(new CoreOptions { ModelStoragePath = "/fake" });
        var logger = Substitute.For<ILogger<QwenModelHost>>();
        return new QwenModelHost(opts, logger);
    }

    private sealed class TestProcessor : QwenTextProcessor
    {
        public override string Name => "test";
        protected override string SystemPrompt => "test";

        public TestProcessor(QwenModelHost host)
            : base(host, NullLogger<TestProcessor>.Instance)
        {
        }

        public InferenceParams GetInferenceParams() => CreateInferenceParams();
    }
}
