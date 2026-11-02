using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Inference;

public class ServiceFailureTests
{
    [Fact]
    public void ServiceLevelFailures_AreRecognized()
    {
        Assert.True(ServiceFailure.IsUnavailable(new HttpRequestException("refused")));
        Assert.True(ServiceFailure.IsUnavailable(new LlamaServerStartException("exit 3")));
        Assert.True(ServiceFailure.IsUnavailable(new ModelNotReadyException(ModelType.Translation, "m", "missing", "download")));
        Assert.True(ServiceFailure.IsUnavailable(new CloudNotConfiguredException("no key")));
    }

    [Fact]
    public void ContentSpecificFailures_AreNot()
    {
        Assert.False(ServiceFailure.IsUnavailable(new InvalidDataException("literal lost")));
        Assert.False(ServiceFailure.IsUnavailable(new InvalidOperationException("other")));
    }
}
