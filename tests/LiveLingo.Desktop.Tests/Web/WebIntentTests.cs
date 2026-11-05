using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class WebIntentTests
{
    [Theory]
    [InlineData("""{"v":1,"name":"ready"}""", typeof(ReadyIntent))]
    [InlineData("""{"v":1,"name":"size","width":656,"height":300}""", typeof(SizeIntent))]
    [InlineData("""{"v":1,"name":"drag"}""", typeof(DragIntent))]
    [InlineData("""{"v":1,"name":"resize","width":900,"height":null}""", typeof(ResizeIntent))]
    [InlineData("""{"v":1,"name":"resize","width":null,"height":700}""", typeof(ResizeIntent))]
    [InlineData("""{"v":1,"name":"resize","width":900,"height":700}""", typeof(ResizeIntent))]
    [InlineData("""{"v":1,"name":"zoom"}""", typeof(ZoomIntent))]
    [InlineData("""{"v":1,"name":"source","text":"hi","rev":3}""", typeof(SourceIntent))]
    [InlineData("""{"v":1,"name":"micToggle"}""", typeof(MicToggleIntent))]
    [InlineData("""{"v":1,"name":"output","value":"translate"}""", typeof(OutputIntent))]
    [InlineData("""{"v":1,"name":"swapLanguage"}""", typeof(SwapLanguageIntent))]
    [InlineData("""{"v":1,"name":"commit","fromKey":true}""", typeof(CommitIntent))]
    [InlineData("""{"v":1,"name":"copy"}""", typeof(CopyIntent))]
    [InlineData("""{"v":1,"name":"cancel"}""", typeof(CancelIntent))]
    [InlineData("""{"v":1,"name":"hide"}""", typeof(HideIntent))]
    [InlineData("""{"v":1,"name":"retry","i":2}""", typeof(RetryIntent))]
    [InlineData("""{"v":1,"name":"edit","i":0}""", typeof(EditIntent))]
    [InlineData("""{"v":1,"name":"editText","text":"x"}""", typeof(EditTextIntent))]
    [InlineData("""{"v":1,"name":"saveEdit"}""", typeof(SaveEditIntent))]
    [InlineData("""{"v":1,"name":"cancelEdit"}""", typeof(CancelEditIntent))]
    [InlineData("""{"v":1,"name":"useOriginal","i":1}""", typeof(UseOriginalIntent))]
    [InlineData("""{"v":1,"name":"newDraft"}""", typeof(NewDraftIntent))]
    [InlineData("""{"v":1,"name":"restoreRecent","index":4}""", typeof(RestoreRecentIntent))]
    [InlineData("""{"v":1,"name":"clearRecent"}""", typeof(ClearRecentIntent))]
    [InlineData("""{"v":1,"name":"import"}""", typeof(ImportIntent))]
    [InlineData("""{"v":1,"name":"export","format":"srt"}""", typeof(ExportIntent))]
    [InlineData("""{"v":1,"name":"wide","on":true}""", typeof(WideIntent))]
    [InlineData("""{"v":1,"name":"whole","on":false}""", typeof(WholeIntent))]
    [InlineData("""{"v":1,"name":"copyOnly","on":true}""", typeof(CopyOnlyIntent))]
    [InlineData("""{"v":1,"name":"banner","action":"openMicrophoneSettings"}""", typeof(BannerIntent))]
    [InlineData("""{"v":1,"name":"openSettings","section":"voice"}""", typeof(OpenSettingsIntent))]
    [InlineData("""{"v":1,"name":"setting","key":"theme","value":"Dark"}""", typeof(SettingIntent))]
    [InlineData("""{"v":1,"name":"addTerm","source":"Redis","target":""}""", typeof(AddTermIntent))]
    [InlineData("""{"v":1,"name":"removeTerm","index":1}""", typeof(RemoveTermIntent))]
    [InlineData("""{"v":1,"name":"cloudToggle","step":"clean","on":true}""", typeof(CloudToggleIntent))]
    [InlineData("""{"v":1,"name":"consent","allow":false}""", typeof(ConsentIntent))]
    [InlineData("""{"v":1,"name":"openEndpointForm","id":null}""", typeof(OpenEndpointFormIntent))]
    [InlineData("""{"v":1,"name":"openEndpointForm","id":"abc"}""", typeof(OpenEndpointFormIntent))]
    [InlineData("""{"v":1,"name":"closeEndpointForm"}""", typeof(CloseEndpointFormIntent))]
    [InlineData("""{"v":1,"name":"saveEndpoint","label":"Main","baseUrl":"https://x.example/v1","model":"m","apiKey":"sk-1"}""", typeof(SaveEndpointIntent))]
    [InlineData("""{"v":1,"name":"enableEndpoint","id":"abc","on":true}""", typeof(EnableEndpointIntent))]
    [InlineData("""{"v":1,"name":"deleteEndpoint","id":"abc"}""", typeof(DeleteEndpointIntent))]
    [InlineData("""{"v":1,"name":"endpointAnswer","allow":false}""", typeof(EndpointAnswerIntent))]
    [InlineData("""{"v":1,"name":"removeLegacyModels"}""", typeof(RemoveLegacyModelsIntent))]
    [InlineData("""{"v":1,"name":"checkUpdates"}""", typeof(CheckUpdatesIntent))]
    [InlineData("""{"v":1,"name":"clipped","viewportWidth":656,"viewportHeight":300,"contentWidth":656,"contentHeight":560}""", typeof(ClippedIntent))]
    [InlineData("""{"v":1,"name":"restartToUpdate"}""", typeof(RestartToUpdateIntent))]
    [InlineData("""{"v":1,"name":"openDownloads"}""", typeof(OpenDownloadsIntent))]
    [InlineData("""{"v":1,"name":"restoreDefaults"}""", typeof(RestoreDefaultsIntent))]
    [InlineData("""{"v":1,"name":"forgetApp","appId":"com.tinyspeck.slackmacgap"}""", typeof(ForgetAppIntent))]
    [InlineData("""{"v":1,"name":"clearApps"}""", typeof(ClearAppsIntent))]
    [InlineData("""{"v":1,"name":"setting","key":"defaultOutput","value":"Translate"}""", typeof(SettingIntent))]
    [InlineData("""{"v":1,"name":"setting","key":"autoDictate","value":"true"}""", typeof(SettingIntent))]
    [InlineData("""{"v":1,"name":"setting","key":"preloadSpeech","value":"false"}""", typeof(SettingIntent))]
    [InlineData("""{"v":1,"name":"setting","key":"rememberChoices","value":"false"}""", typeof(SettingIntent))]
    public void Parse_AcceptsEveryIntentOfTheClosedSet(string json, Type expected)
    {
        var intent = WebIntent.Parse(json, out var reason);

        Assert.NotNull(intent);
        Assert.Equal("", reason);
        Assert.IsType(expected, intent);
    }

    [Fact]
    public void Parse_ReadsTheFieldsOfAnIntent()
    {
        var source = Assert.IsType<SourceIntent>(WebIntent.Parse("""{"v":1,"name":"source","text":"你好","rev":9}""", out _));
        var output = Assert.IsType<OutputIntent>(WebIntent.Parse("""{"v":1,"name":"output","value":"original"}""", out _));
        var banner = Assert.IsType<BannerIntent>(WebIntent.Parse("""{"v":1,"name":"banner","action":"keepWaiting"}""", out _));
        var export = Assert.IsType<ExportIntent>(WebIntent.Parse("""{"v":1,"name":"export","format":"markdown"}""", out _));

        Assert.Equal(("你好", 9), (source.Text, source.Rev));
        Assert.Equal(ComposerOutput.Original, output.Value);
        Assert.Equal(BannerAction.KeepWaiting, banner.Action);
        Assert.Equal(ExportFormat.Markdown, export.Format);
    }

    [Theory]
    [InlineData("not json", "JsonException")]
    [InlineData("""{"v":1,"name":"formatDisk"}""", "JsonException")]
    [InlineData("""{"v":1}""", "JsonException")]
    [InlineData("""{"v":2,"name":"ready"}""", "version")]
    [InlineData("""{"name":"ready"}""", "JsonException")]
    [InlineData("""{"v":1,"name":"source","rev":1}""", "JsonException")]
    [InlineData("""{"v":1,"name":"source","text":5,"rev":1}""", "JsonException")]
    [InlineData("""{"v":1,"name":"retry","i":"zero"}""", "JsonException")]
    [InlineData("""{"v":1,"name":"ready","extra":1}""", "JsonException")]
    [InlineData("""{"v":1,"name":"output","value":"shout"}""", "JsonException")]
    [InlineData("""{"v":1,"name":"output","value":1}""", "JsonException")]
    [InlineData("""{"v":1,"name":"banner","action":"deleteEverything"}""", "JsonException")]
    [InlineData("""{"v":1,"name":"retry","i":-1}""", "invalid")]
    [InlineData("""{"v":1,"name":"size","width":0,"height":300}""", "invalid")]
    [InlineData("""{"v":1,"name":"clipped","viewportWidth":-1,"viewportHeight":300,"contentWidth":656,"contentHeight":560}""", "invalid")]
    [InlineData("""{"v":1,"name":"size","width":656,"height":99999}""", "invalid")]
    [InlineData("""{"v":1,"name":"drag","x":1}""", "JsonException")]
    [InlineData("""{"v":1,"name":"resize"}""", "JsonException")]
    [InlineData("""{"v":1,"name":"resize","width":900}""", "JsonException")]
    [InlineData("""{"v":1,"name":"resize","width":null,"height":null}""", "invalid")]
    [InlineData("""{"v":1,"name":"resize","width":0,"height":null}""", "invalid")]
    [InlineData("""{"v":1,"name":"resize","width":900,"height":99999}""", "invalid")]
    [InlineData("""{"v":1,"name":"resize","width":"wide","height":null}""", "JsonException")]
    [InlineData("""{"v":1,"name":"zoom","on":true}""", "JsonException")]
    [InlineData("""{"v":1,"name":"openSettings","section":"../etc"}""", "invalid")]
    [InlineData("""{"v":1,"name":"source","text":"x","rev":-1}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"formatDisk","value":"1"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"theme","value":"Neon"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"commitKey","value":"enter"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"reduceMotion","value":"yes"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"cleanup","value":"yes"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"tier","value":"Cloud"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"defaultOutput","value":"Cloud"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"autoDictate","value":"1"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"preloadSpeech","value":"yes"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"rememberChoices","value":"maybe"}""", "invalid")]
    [InlineData("""{"v":1,"name":"forgetApp","appId":""}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"theme"}""", "JsonException")]
    [InlineData("""{"v":1,"name":"removeTerm","index":-1}""", "invalid")]
    [InlineData("""{"v":1,"name":"cloudToggle","step":"everything","on":true}""", "JsonException")]
    [InlineData("""{"v":1,"name":"modelInfo"}""", "JsonException")]
    [InlineData("""{"v":1,"name":"openEndpointForm","id":""}""", "invalid")]
    [InlineData("""{"v":1,"name":"enableEndpoint","id":"","on":true}""", "invalid")]
    [InlineData("""{"v":1,"name":"deleteEndpoint","id":""}""", "invalid")]
    [InlineData("""{"v":1,"name":"saveEndpoint","label":"x","baseUrl":"u","model":"m"}""", "JsonException")]
    [InlineData("""{"v":1,"name":"setting","key":"cloudBaseUrl","value":"https://x.example"}""", "invalid")]
    public void Parse_RejectsAnythingOutsideTheClosedSet(string json, string expectedReason)
    {
        var intent = WebIntent.Parse(json, out var reason);

        Assert.Null(intent);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void Parse_RejectsAMessageOverTheSizeLimitWithoutParsingIt()
    {
        var json = $$"""{"v":1,"name":"source","text":"{{new string('a', WebIntent.MaxBytes)}}","rev":1}""";

        Assert.Null(WebIntent.Parse(json, out var reason));
        Assert.Equal("too large", reason);
    }

    [Theory]
    [InlineData("hotkey", 65)]
    [InlineData("microphone", 257)]
    public void Parse_BoundsTheFreeTextOfASetting(string key, int length)
    {
        var json = $$"""{"v":1,"name":"setting","key":"{{key}}","value":"{{new string('a', length)}}"}""";

        Assert.Null(WebIntent.Parse(json, out var reason));
        Assert.Equal("invalid", reason);
    }

    [Theory]
    [InlineData(41, 10, 10, 10)]
    [InlineData(10, 2_049, 10, 10)]
    [InlineData(10, 10, 257, 10)]
    [InlineData(10, 10, 10, 4_097)]
    public void Parse_BoundsTheFieldsOfAnEndpoint(int label, int url, int model, int key)
    {
        var json = $$"""{"v":1,"name":"saveEndpoint","label":"{{new string('a', label)}}","baseUrl":"{{new string('a', url)}}","model":"{{new string('a', model)}}","apiKey":"{{new string('a', key)}}"}""";

        Assert.Null(WebIntent.Parse(json, out var reason));
        Assert.Equal("invalid", reason);
    }

    [Fact]
    public void Parse_BoundsTheGlossaryTerms()
    {
        var long200 = new string('a', 201);

        Assert.Null(WebIntent.Parse($$"""{"v":1,"name":"addTerm","source":"{{long200}}","target":""}""", out _));
    }

    [Fact]
    public void AnEndpointWithItsKey_NeverPrintsItself()
    {
        var intent = WebIntent.Parse("""{"v":1,"name":"saveEndpoint","label":"Main","baseUrl":"https://x.example/v1","model":"m","apiKey":"sk-very-secret"}""", out _);

        Assert.DoesNotContain("sk-very-secret", intent!.ToString());
    }

    [Fact]
    public void Parse_RejectionReasonNeverEchoesTheMessage()
    {
        // The discriminator value is attacker- or user-controlled text; it must not reach a log line through the reason.
        WebIntent.Parse("""{"v":1,"name":"secret-token-123"}""", out var reason);

        Assert.DoesNotContain("secret-token-123", reason);
    }
}
