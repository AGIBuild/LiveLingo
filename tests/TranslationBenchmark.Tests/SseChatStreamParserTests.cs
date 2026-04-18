using System.Text;
using TranslationBenchmark;

namespace TranslationBenchmark.Tests;

public class SseChatStreamParserTests
{
    [Fact]
    public async Task EnumerateDeltasAsync_ParsesStandardChunks()
    {
        var body = string.Join("\n\n",
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}",
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}",
            "data: [DONE]",
            "");

        var deltas = await CollectAsync(body);
        Assert.Equal(["Hello", " world"], deltas);
    }

    [Fact]
    public async Task EnumerateDeltasAsync_SkipsKeepalivesAndEmptyDeltas()
    {
        var body = string.Join("\n",
            ":ka",
            "",
            "data: {\"choices\":[{\"delta\":{}}]}",
            "",
            "data: {\"choices\":[{\"delta\":{\"content\":\"\"}}]}",
            "",
            "data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}]}",
            "",
            "data: [DONE]",
            "");

        var deltas = await CollectAsync(body);
        Assert.Equal(["x"], deltas);
    }

    [Fact]
    public async Task EnumerateDeltasAsync_MalformedPayload_Throws()
    {
        var body = "data: {not-json}\n\n";
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await CollectAsync(body));
    }

    [Fact]
    public async Task EnumerateDeltasAsync_PreservesOrderForLongStream()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 50; i++)
            sb.Append($"data: {{\"choices\":[{{\"delta\":{{\"content\":\"{i} \"}}}}]}}\n\n");
        sb.Append("data: [DONE]\n\n");

        var deltas = await CollectAsync(sb.ToString());
        Assert.Equal(50, deltas.Count);
        Assert.Equal("0 ", deltas[0]);
        Assert.Equal("49 ", deltas[^1]);
    }

    private static async Task<List<string>> CollectAsync(string body)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var list = new List<string>();
        await foreach (var delta in SseChatStreamParser.EnumerateDeltasAsync(stream))
            list.Add(delta);
        return list;
    }
}
