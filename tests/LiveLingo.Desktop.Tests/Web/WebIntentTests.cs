using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class WebIntentTests
{
    [Theory]
    [InlineData("""{"v":1,"name":"ready"}""", typeof(ReadyIntent))]
    [InlineData("""{"v":1,"name":"size","width":656,"height":300}""", typeof(SizeIntent))]
    [InlineData("""{"v":1,"name":"drag"}""", typeof(DragIntent))]
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
    [InlineData("""{"v":1,"name":"saveKey","value":"sk-1"}""", typeof(SaveKeyIntent))]
    [InlineData("""{"v":1,"name":"forgetKey"}""", typeof(ForgetKeyIntent))]
    [InlineData("""{"v":1,"name":"removeLegacyModels"}""", typeof(RemoveLegacyModelsIntent))]
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
    [InlineData("""{"v":1,"name":"size","width":656,"height":99999}""", "invalid")]
    [InlineData("""{"v":1,"name":"drag","x":1}""", "JsonException")]
    [InlineData("""{"v":1,"name":"openSettings","section":"../etc"}""", "invalid")]
    [InlineData("""{"v":1,"name":"source","text":"x","rev":-1}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"formatDisk","value":"1"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"theme","value":"Neon"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"commitKey","value":"enter"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"reduceMotion","value":"yes"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"cleanup","value":"yes"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"tier","value":"Cloud"}""", "invalid")]
    [InlineData("""{"v":1,"name":"setting","key":"theme"}""", "JsonException")]
    [InlineData("""{"v":1,"name":"removeTerm","index":-1}""", "invalid")]
    [InlineData("""{"v":1,"name":"cloudToggle","step":"everything","on":true}""", "JsonException")]
    [InlineData("""{"v":1,"name":"saveKey","value":""}""", "invalid")]
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
    [InlineData("cloudBaseUrl", 2_049)]
    [InlineData("cloudModel", 257)]
    public void Parse_BoundsTheFreeTextOfASetting(string key, int length)
    {
        var json = $$"""{"v":1,"name":"setting","key":"{{key}}","value":"{{new string('a', length)}}"}""";

        Assert.Null(WebIntent.Parse(json, out var reason));
        Assert.Equal("invalid", reason);
    }

    [Fact]
    public void Parse_BoundsTheGlossaryTermsAndTheKey()
    {
        var long200 = new string('a', 201);

        Assert.Null(WebIntent.Parse($$"""{"v":1,"name":"addTerm","source":"{{long200}}","target":""}""", out _));
        Assert.Null(WebIntent.Parse($$"""{"v":1,"name":"saveKey","value":"{{new string('k', 4_097)}}"}""", out _));
    }

    [Fact]
    public void ASavedKey_NeverPrintsItself()
    {
        var intent = WebIntent.Parse("""{"v":1,"name":"saveKey","value":"sk-very-secret"}""", out _);

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
