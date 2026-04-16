using LiveLingo.Core.Models;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace LiveLingo.Core.Tests.Models;

public sealed class TranslationChatClientTests
{
    private static readonly ModelProfile LocalProfile =
        new StaticModelCatalog().FindById(ModelRegistry.Gemma4_12B.Id)!;

    [Fact]
    public async Task GetResponseAsync_selects_translation_profile_and_invokes()
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();

        ModelInvocationRequest? capturedRequest = null;
        selector.SelectTranslationProfile("zh", "en", Arg.Any<TranslationRoutingContext?>()).Returns(LocalProfile);
        invocationService
            .InvokeAsync(Arg.Do<ModelInvocationRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("Hello world"));

        var client = new TranslationChatClient(selector, invocationService);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Translate zh to en."),
            new(ChatRole.User, "你好")
        };
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["sourceLang"] = "zh",
                ["targetLang"] = "en"
            }
        };

        var response = await client.GetResponseAsync(messages, options, CancellationToken.None);

        Assert.Equal("Hello world", response.Text);
        Assert.Equal(LocalProfile.Id, response.ModelId);
        Assert.NotNull(capturedRequest);
        Assert.Same(LocalProfile, capturedRequest!.Profile);
        Assert.Equal(ModelTaskType.Translation, capturedRequest.TaskType);
        Assert.Equal(2, capturedRequest.Messages.Count);
    }

    [Fact]
    public async Task GetResponseAsync_rebuilds_template_messages_when_sourceText_provided()
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();

        ModelInvocationRequest? capturedRequest = null;
        selector.SelectTranslationProfile("zh", "en", Arg.Any<TranslationRoutingContext?>()).Returns(LocalProfile);
        invocationService
            .InvokeAsync(Arg.Do<ModelInvocationRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("result"));

        var client = new TranslationChatClient(selector, invocationService);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["sourceLang"] = "zh",
                ["targetLang"] = "en",
                ["sourceText"] = "你好",
                ["sourceLangName"] = "Chinese",
                ["targetLangName"] = "English"
            }
        };

        // Incoming messages are the canonical cache-key messages (Default template)
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "cache-key-system"), new ChatMessage(ChatRole.User, "cache-key-user")],
            options, CancellationToken.None);

        // TranslationChatClient should have rebuilt using Gemma template (LocalProfile uses Gemma4_12B)
        Assert.NotNull(capturedRequest);
        Assert.Contains("Begin your response immediately", capturedRequest!.Messages[0].Content,
            StringComparison.Ordinal); // Gemma template
        Assert.Contains("你好", capturedRequest.Messages[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetResponseAsync_selects_post_processing_profile_for_taskType()
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();

        selector.SelectPostProcessingProfile().Returns(LocalProfile);
        invocationService
            .InvokeAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("optimized text"));

        var client = new TranslationChatClient(selector, invocationService);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["sourceLang"] = "zh",
                ["targetLang"] = "en",
                ["taskType"] = "PostProcessing"
            }
        };

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "text")], options, CancellationToken.None);

        Assert.Equal("optimized text", response.Text);
        selector.Received(1).SelectPostProcessingProfile();
        selector.DidNotReceive().SelectTranslationProfile(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task GetResponseAsync_defaults_src_en_when_no_props()
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();

        selector.SelectTranslationProfile("zh", "en").Returns(LocalProfile);
        invocationService.InvokeAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("result"));

        var client = new TranslationChatClient(selector, invocationService);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], null, CancellationToken.None);

        selector.Received(1).SelectTranslationProfile("zh", "en");
    }
}
