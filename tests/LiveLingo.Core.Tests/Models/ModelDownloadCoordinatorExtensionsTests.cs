using LiveLingo.Core.Models;
using NSubstitute;

namespace LiveLingo.Core.Tests.Models;

public sealed class ModelDownloadCoordinatorExtensionsTests
{
    private static readonly ModelDescriptor[] Models = [ModelCatalog.Cleaner, ModelCatalog.TranslatorFast, ModelCatalog.SpeechVad];

    private static IModelDownloadCoordinator Coordinator(Func<string, ModelDownloadStatus> statusAfter, List<string> started)
    {
        var coordinator = Substitute.For<IModelDownloadCoordinator>();
        coordinator.StartAsync(Arg.Any<ModelDescriptor>()).Returns(call =>
        {
            started.Add(call.Arg<ModelDescriptor>().Id);
            return Task.CompletedTask;
        });
        coordinator.GetState(Arg.Any<string>()).Returns(call =>
            new ModelDownloadState(call.Arg<string>(), statusAfter(call.Arg<string>()), 100, null));
        return coordinator;
    }

    [Fact]
    public async Task TheModels_AreDownloadedOneAfterTheOther_InOrder()
    {
        var started = new List<string>();
        var coordinator = Coordinator(_ => ModelDownloadStatus.Installed, started);

        await coordinator.StartAllAsync(Models);

        Assert.Equal(Models.Select(model => model.Id), started);
    }

    [Theory]
    [InlineData(ModelDownloadStatus.Failed)]
    [InlineData(ModelDownloadStatus.Cancelled)]
    public async Task AModelThatFailedOrWasCancelled_StopsTheRest(ModelDownloadStatus status)
    {
        var started = new List<string>();
        var coordinator = Coordinator(id => id == Models[1].Id ? status : ModelDownloadStatus.Installed, started);

        await coordinator.StartAllAsync(Models);

        Assert.Equal([Models[0].Id, Models[1].Id], started);
    }

    [Fact]
    public async Task NothingToDownload_DoesNothing()
    {
        var started = new List<string>();

        await Coordinator(_ => ModelDownloadStatus.Installed, started).StartAllAsync([]);

        Assert.Empty(started);
    }
}
