using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Speech.Cloud;

internal enum QwenServerEventKind
{
    Unknown,
    TaskStarted,
    ResultGenerated,
    TaskFinished,
    TaskFailed
}

internal sealed record QwenSentenceUpdate(
    int SentenceId,
    string Text,
    bool SentenceEnd,
    bool Heartbeat,
    int? BeginTimeMs,
    int? EndTimeMs,
    int? StashSentenceId = null,
    string? StashText = null);

internal sealed record QwenServerEvent(
    QwenServerEventKind Kind,
    string? TaskId,
    QwenSentenceUpdate? Sentence = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

internal sealed record QwenHttpSentence(int BeginTimeMs, int EndTimeMs, string Text);

internal static class QwenAudioProtocol
{
    public const int MaxContextTurns = 5;
    public const int MaxContextCharsPerTurn = 400;
    public const int DefaultMaxSentenceSilenceMs = 900;

    public static byte[] EncodePcm16Le(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 2];
        WritePcm16Le(samples, bytes);
        return bytes;
    }

    public static void WritePcm16Le(ReadOnlySpan<float> samples, Span<byte> destination)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var scaled = MathF.Round(Math.Clamp(samples[i], -1f, 1f) * 32767f);
            var pcm = (short)Math.Clamp((int)scaled, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(destination[(i * 2)..], pcm);
        }
    }

    public static byte[] EncodeWavPcm16Mono(ReadOnlySpan<float> samples, int sampleRate = AudioFormat.SampleRate)
    {
        var pcmBytes = samples.Length * 2;
        var wav = new byte[44 + pcmBytes];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span[..4]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..8], (uint)(36 + pcmBytes));
        "WAVE"u8.CopyTo(span[8..12]);
        "fmt "u8.CopyTo(span[12..16]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..20], 16u);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..22], 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..24], 1); // Mono
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..28], (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..32], (uint)(sampleRate * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..34], 2); // BlockAlign
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..36], 16); // BitsPerSample
        "data"u8.CopyTo(span[36..40]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..44], (uint)pcmBytes);

        WritePcm16Le(samples, span[44..]);
        return wav;
    }

    public static IReadOnlyDictionary<string, int> BuildVocabulary(SpeechSessionRequest request)
    {
        if (request.Glossary is { } glossary)
            return glossary.CloudSpeechVocabulary();
        return Glossary.BuildCloudVocabulary(request.Hotwords, []);
    }

    public static JsonArray BuildContextArray(IReadOnlyList<string> recentTurns)
    {
        var array = new JsonArray();
        var start = Math.Max(0, recentTurns.Count - MaxContextTurns);
        for (var i = start; i < recentTurns.Count; i++)
        {
            var text = recentTurns[i].Trim();
            if (text.Length == 0)
                continue;
            if (text.Length > MaxContextCharsPerTurn)
                text = text[..MaxContextCharsPerTurn];

            array.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "input_text",
                        ["text"] = text
                    }
                }
            });
        }
        return array;
    }

    public static string BuildRunTaskJson(
        string taskId,
        SpeechCloudRoute route,
        SpeechSessionRequest request,
        IReadOnlyList<string> contextTurns,
        bool intermediateResults)
    {
        var isQwenMessage = route.Model.Contains("message", StringComparison.OrdinalIgnoreCase);
        var isQwenAudio = route.Model.StartsWith("qwen-audio-", StringComparison.OrdinalIgnoreCase);

        var parameters = new JsonObject
        {
            ["format"] = "pcm",
            ["sample_rate"] = AudioFormat.SampleRate,
            ["heartbeat"] = true
        };

        if (isQwenAudio)
        {
            parameters["intermediate_result_enabled"] = intermediateResults;
            parameters["max_sentence_silence"] = DefaultMaxSentenceSilenceMs;
        }

        if (isQwenMessage)
        {
            parameters["vad_model"] = "near_meeting_16k";
            parameters["disfluency_removal_enabled"] = request.EnableNativePolish;
        }

        if (route.SupportsTieredHotwords)
        {
            var vocab = BuildVocabulary(request);
            if (vocab.Count > 0)
            {
                var vocabObj = new JsonObject();
                foreach (var (term, weight) in vocab)
                    vocabObj[term] = weight;
                parameters["vocabulary"] = vocabObj;
            }
        }

        var input = new JsonObject();
        if (isQwenAudio && contextTurns.Count > 0)
        {
            var context = BuildContextArray(contextTurns);
            if (context.Count > 0)
                input["context"] = context;
        }

        var root = new JsonObject
        {
            ["header"] = new JsonObject
            {
                ["action"] = "run-task",
                ["task_id"] = taskId,
                ["streaming"] = "duplex"
            },
            ["payload"] = new JsonObject
            {
                ["task_group"] = "audio",
                ["task"] = "asr",
                ["function"] = "recognition",
                ["model"] = route.Model,
                ["parameters"] = parameters,
                ["input"] = input
            }
        };

        return root.ToJsonString();
    }

    public static string BuildContinueTaskJson(string taskId, IReadOnlyList<string> contextTurns)
    {
        var input = new JsonObject();
        var context = BuildContextArray(contextTurns);
        if (context.Count > 0)
            input["context"] = context;

        var root = new JsonObject
        {
            ["header"] = new JsonObject
            {
                ["action"] = "continue-task",
                ["task_id"] = taskId,
                ["streaming"] = "duplex"
            },
            ["payload"] = new JsonObject
            {
                ["input"] = input
            }
        };

        return root.ToJsonString();
    }

    public static string BuildFinishTaskJson(string taskId)
    {
        var root = new JsonObject
        {
            ["header"] = new JsonObject
            {
                ["action"] = "finish-task",
                ["task_id"] = taskId,
                ["streaming"] = "duplex"
            },
            ["payload"] = new JsonObject
            {
                ["input"] = new JsonObject()
            }
        };

        return root.ToJsonString();
    }

    public static QwenServerEvent ParseServerEvent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("header", out var header) || header.ValueKind != JsonValueKind.Object)
            return new QwenServerEvent(QwenServerEventKind.Unknown, null);

        var eventName = header.TryGetProperty("event", out var ev) ? ev.GetString() : null;
        var taskId = header.TryGetProperty("task_id", out var tid) ? tid.GetString() : null;

        switch (eventName)
        {
            case "task-started":
                return new QwenServerEvent(QwenServerEventKind.TaskStarted, taskId);

            case "task-finished":
                return new QwenServerEvent(QwenServerEventKind.TaskFinished, taskId);

            case "task-failed":
            {
                var code = header.TryGetProperty("error_code", out var ec) ? ec.GetString() : null;
                var msg = header.TryGetProperty("error_message", out var em) ? em.GetString() : null;
                return new QwenServerEvent(QwenServerEventKind.TaskFailed, taskId, ErrorCode: code, ErrorMessage: msg);
            }

            case "result-generated":
            {
                if (!root.TryGetProperty("payload", out var payload)
                    || payload.ValueKind != JsonValueKind.Object
                    || !payload.TryGetProperty("output", out var output)
                    || output.ValueKind != JsonValueKind.Object
                    || !output.TryGetProperty("sentence", out var sentence)
                    || sentence.ValueKind != JsonValueKind.Object)
                {
                    return new QwenServerEvent(QwenServerEventKind.ResultGenerated, taskId);
                }

                var sentenceId = sentence.TryGetProperty("sentence_id", out var sid) && sid.ValueKind == JsonValueKind.Number
                    ? sid.GetInt32()
                    : 0;
                var heartbeat = sentenceId == 0
                    || (sentence.TryGetProperty("heartbeat", out var hb) && hb.ValueKind == JsonValueKind.True);
                var text = sentence.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String
                    ? txt.GetString() ?? ""
                    : "";
                var sentenceEnd = sentence.TryGetProperty("sentence_end", out var se) && se.ValueKind == JsonValueKind.True;
                int? beginTime = sentence.TryGetProperty("begin_time", out var bt) && bt.ValueKind == JsonValueKind.Number
                    ? bt.GetInt32()
                    : null;
                int? endTime = sentence.TryGetProperty("end_time", out var et) && et.ValueKind == JsonValueKind.Number
                    ? et.GetInt32()
                    : null;

                int? stashId = null;
                string? stashText = null;
                if (sentence.TryGetProperty("stash", out var stash) && stash.ValueKind == JsonValueKind.Object)
                {
                    if (stash.TryGetProperty("sentence_id", out var stId) && stId.ValueKind == JsonValueKind.Number)
                        stashId = stId.GetInt32();
                    if (stash.TryGetProperty("text", out var stTxt) && stTxt.ValueKind == JsonValueKind.String)
                        stashText = stTxt.GetString();
                }

                return new QwenServerEvent(
                    QwenServerEventKind.ResultGenerated,
                    taskId,
                    new QwenSentenceUpdate(sentenceId, text, sentenceEnd, heartbeat, beginTime, endTime, stashId, stashText));
            }

            default:
                return new QwenServerEvent(QwenServerEventKind.Unknown, taskId);
        }
    }

    public static async Task<IReadOnlyList<QwenHttpSentence>> TranscribeChunkHttpAsync(
        HttpClient http,
        SpeechCloudRoute route,
        ReadOnlyMemory<float> samples,
        SpeechSessionRequest request,
        IReadOnlyList<string> contextTurns,
        CancellationToken ct)
    {
        if (samples.Length == 0)
            return [];

        var wavBytes = EncodeWavPcm16Mono(samples.Span);
        var durationMs = (int)Math.Round(AudioFormat.SamplesToMs(samples.Length));

        return route.Protocol switch
        {
            SpeechCloudProtocol.QwenAudioHttp => await CallDashScopeMultimodalAsync(http, route, wavBytes, durationMs, request, contextTurns, ct).ConfigureAwait(false),
            SpeechCloudProtocol.OpenAiChatAudio => await CallOpenAiChatAudioAsync(http, route, wavBytes, durationMs, request, contextTurns, ct).ConfigureAwait(false),
            _ => await CallOpenAiTranscriptionsAsync(http, route, wavBytes, durationMs, request, ct).ConfigureAwait(false)
        };
    }

    private static async Task<IReadOnlyList<QwenHttpSentence>> CallDashScopeMultimodalAsync(
        HttpClient http,
        SpeechCloudRoute route,
        byte[] wavBytes,
        int fallbackDurationMs,
        SpeechSessionRequest request,
        IReadOnlyList<string> contextTurns,
        CancellationToken ct)
    {
        var messages = BuildContextArray(contextTurns);
        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "input_audio",
                    ["input_audio"] = new JsonObject
                    {
                        ["data"] = "data:audio/wav;base64," + Convert.ToBase64String(wavBytes)
                    }
                }
            }
        });

        var parameters = new JsonObject
        {
            ["format"] = "wav",
            ["sample_rate"] = AudioFormat.SampleRate
        };
        if (route.SupportsTieredHotwords)
        {
            var vocab = BuildVocabulary(request);
            if (vocab.Count > 0)
            {
                var vocabObj = new JsonObject();
                foreach (var (term, weight) in vocab)
                    vocabObj[term] = weight;
                parameters["vocabulary"] = vocabObj;
            }
        }

        var payload = new JsonObject
        {
            ["model"] = route.Model,
            ["input"] = new JsonObject { ["messages"] = messages },
            ["parameters"] = parameters
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, route.Endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", route.ApiKey);
        req.Headers.TryAddWithoutValidation("X-DashScope-SSE", "disable");

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new CloudSpeechException($"Cloud speech HTTP {(int)resp.StatusCode}: {ExtractError(body)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var errCode) && errCode.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(errCode.GetString()))
        {
            var errMsg = root.TryGetProperty("message", out var em) ? em.GetString() : errCode.GetString();
            throw new CloudSpeechException($"Cloud speech failed ({errCode.GetString()}): {errMsg}");
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Object)
            return [];

        if (output.TryGetProperty("sentences", out var sentences) && sentences.ValueKind == JsonValueKind.Array)
        {
            var list = new List<QwenHttpSentence>();
            foreach (var item in sentences.EnumerateArray())
            {
                var itemText = item.TryGetProperty("text", out var it) ? TextTidy.Apply(it.GetString() ?? "") : "";
                if (itemText.Length == 0)
                    continue;
                var bt = item.TryGetProperty("begin_time", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : 0;
                var et = item.TryGetProperty("end_time", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : fallbackDurationMs;
                list.Add(new QwenHttpSentence(bt, Math.Max(bt, et), itemText));
            }
            if (list.Count > 0)
                return list;
        }

        if (output.TryGetProperty("sentence", out var sentence) && sentence.ValueKind == JsonValueKind.Object)
        {
            var sentenceText = sentence.TryGetProperty("text", out var st) ? TextTidy.Apply(st.GetString() ?? "") : "";
            if (sentenceText.Length > 0)
            {
                var bt = sentence.TryGetProperty("begin_time", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : 0;
                var et = sentence.TryGetProperty("end_time", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : fallbackDurationMs;
                return [new QwenHttpSentence(bt, Math.Max(bt, et), sentenceText)];
            }
        }

        if (output.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
        {
            var plain = TextTidy.Apply(textProp.GetString() ?? "");
            if (plain.Length > 0)
                return [new QwenHttpSentence(0, fallbackDurationMs, plain)];
        }

        if (output.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var msg = choices[0].GetProperty("message");
            if (msg.TryGetProperty("content", out var content))
            {
                var text = content.ValueKind == JsonValueKind.String
                    ? content.GetString() ?? ""
                    : content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0 && content[0].TryGetProperty("text", out var t0)
                        ? t0.GetString() ?? ""
                        : "";
                var tidy = TextTidy.Apply(text);
                if (tidy.Length > 0)
                    return [new QwenHttpSentence(0, fallbackDurationMs, tidy)];
            }
        }

        return [];
    }

    private static async Task<IReadOnlyList<QwenHttpSentence>> CallOpenAiChatAudioAsync(
        HttpClient http,
        SpeechCloudRoute route,
        byte[] wavBytes,
        int fallbackDurationMs,
        SpeechSessionRequest request,
        IReadOnlyList<string> contextTurns,
        CancellationToken ct)
    {
        var messages = new JsonArray();
        var vocabTerms = request.Glossary?.Terms ?? request.Hotwords;
        if (vocabTerms.Count > 0 || contextTurns.Count > 0)
        {
            var sb = new StringBuilder();
            if (vocabTerms.Count > 0)
                sb.Append(string.Join(", ", vocabTerms.Take(80)));
            if (contextTurns.Count > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(string.Join(" ", contextTurns.TakeLast(3)));
            }
            var sysText = sb.ToString();
            if (sysText.Length > 600)
                sysText = sysText[..600];
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = sysText
            });
        }

        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "input_audio",
                    ["input_audio"] = new JsonObject
                    {
                        ["data"] = "data:audio/wav;base64," + Convert.ToBase64String(wavBytes),
                        ["format"] = "wav"
                    }
                }
            }
        });

        var payload = new JsonObject
        {
            ["model"] = route.Model,
            ["messages"] = messages,
            ["stream"] = false
        };
        if (route.Model.StartsWith("qwen", StringComparison.OrdinalIgnoreCase))
        {
            payload["asr_options"] = new JsonObject { ["enable_itn"] = true };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, route.Endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", route.ApiKey);

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new CloudSpeechException($"Cloud speech HTTP {(int)resp.StatusCode}: {ExtractError(body)}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var msg)
            && msg.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            var text = TextTidy.Apply(content.GetString() ?? "");
            if (text.Length > 0)
                return [new QwenHttpSentence(0, fallbackDurationMs, text)];
        }

        return [];
    }

    private static async Task<IReadOnlyList<QwenHttpSentence>> CallOpenAiTranscriptionsAsync(
        HttpClient http,
        SpeechCloudRoute route,
        byte[] wavBytes,
        int fallbackDurationMs,
        SpeechSessionRequest request,
        CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(wavBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", "speech.wav");
        form.Add(new StringContent(route.Model), "model");
        form.Add(new StringContent("json"), "response_format");

        var terms = request.Glossary?.Terms ?? request.Hotwords;
        if (terms.Count > 0)
        {
            var prompt = string.Join(", ", terms.Take(40));
            if (prompt.Length > 240)
                prompt = prompt[..240];
            form.Add(new StringContent(prompt), "prompt");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, route.Endpoint) { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", route.ApiKey);

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new CloudSpeechException($"Cloud speech HTTP {(int)resp.StatusCode}: {ExtractError(body)}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
        {
            var text = TextTidy.Apply(textProp.GetString() ?? "");
            if (text.Length > 0)
                return [new QwenHttpSentence(0, fallbackDurationMs, text)];
        }

        return [];
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "empty response";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return msg.GetString()!;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString()!;
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var em) && em.ValueKind == JsonValueKind.String)
                    return em.GetString()!;
            }
        }
        catch (JsonException)
        {
        }
        var trimmed = body.Trim();
        return trimmed.Length > 180 ? trimmed[..180] + "…" : trimmed;
    }
}
