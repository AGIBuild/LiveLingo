namespace LiveLingo.Core.Drafting;

internal sealed record DocumentPiece(string Text, string Lead, string Trail, bool Owned, bool Attach = false);

/// <summary>A detached input snapshot. Model calls never read a changing draft.</summary>
internal sealed record DocumentWork(
    DocumentTicket Ticket,
    string Source,
    IReadOnlyList<DocumentPiece> Pieces,
    bool AllNativePolished = false,
    string? NativePolishedText = null)
{
    internal static DocumentWork Capture(Draft draft, UnitKind kind)
    {
        var allNativePolished = kind == UnitKind.Clean
            && !draft.ForceCleanRepolish
            && draft.Units.Count > 0
            && draft.Units.All(u => u.Clean.IsOwned || (u.IsNativePolished && !string.IsNullOrWhiteSpace(u.Clean.Text)));
        var nativePolishedText = allNativePolished ? draft.PreviewText(UnitKind.Clean) : null;

        var pieces = new List<DocumentPiece>();
        var run = new List<(string Lead, string Body, string Trail)>();
        void Flush()
        {
            if (run.Count > 0)
                pieces.AddRange(Split(TextUnits.Join(run)));
            run.Clear();
        }

        var clean = kind == UnitKind.Translation ? draft.TranslationDocumentInput() : null;
        if (draft.Units.Any(u => u.Translation.IsOwned))
            clean = null; // Raw snapshot provides stable unit boundaries for protected translations.
        if (clean is not null)
            pieces.AddRange(Split(clean));
        else
        {
            foreach (var unit in draft.Units)
            {
                var result = unit.Result(kind);
                if (!result.IsOwned)
                {
                    run.Add((unit.Lead, unit.Raw, unit.Trail));
                    continue;
                }
                Flush();
                if (string.IsNullOrWhiteSpace(result.Text))
                    throw new InvalidDataException("A protected manual result is empty.");
                pieces.Add(new DocumentPiece(result.Text, unit.Lead, unit.Trail, true));
            }
            Flush();
        }
        return new DocumentWork(draft.DocumentTicketFor(kind), clean ?? draft.SourceText(), pieces, allNativePolished, nativePolishedText);
    }

    internal async Task<string> TransformAsync(
        Func<string, string, CancellationToken, Task<string>> transform, int contextChars, CancellationToken ct)
    {
        var output = new System.Text.StringBuilder();
        var previousBody = "";
        var previousTrail = "";
        foreach (var piece in Pieces)
        {
            ct.ThrowIfCancellationRequested();
            var preceding = output.ToString();
            if (preceding.Length > contextChars)
                preceding = preceding[^contextChars..];
            var text = piece.Owned ? piece.Text
                : (await transform(piece.Text, preceding, ct).ConfigureAwait(false)).Trim();
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text) && !(piece.Owned && piece.Text.Length == 0))
                throw new InvalidDataException("Empty final document response.");
            if (!piece.Attach && piece.Lead.Length == 0 && previousTrail.Length == 0 && TextUnits.NeedsSpace(previousBody, text))
                output.Append(' ');
            output.Append(piece.Lead).Append(text).Append(piece.Trail);
            previousBody = text;
            previousTrail = piece.Trail;
        }
        return output.ToString();
    }

    private const int Budget = 800;

    // Split oversized units too; never cut a literal or a UTF-16 surrogate pair. An oversized literal fails explicitly.
    private static IEnumerable<DocumentPiece> Split(string source)
    {
        var spans = TextUnits.LiteralSpans(source);
        if (spans.Any(span => span.End - span.Start > Budget))
            throw new InvalidDataException("A protected literal exceeds the 800 character document group budget.");
        var start = 0;
        var attach = false;
        while (start < source.Length)
        {
            var end = Math.Min(start + Budget, source.Length);
            // Explicit source line breaks/indentation are layout, unlike false sentence breaks inside ASR passages.
            for (var i = start; i < end; i++)
                if (source[i] == '\n' && !spans.Any(span => span.Start <= i && span.End > i))
                {
                    end = i + 1;
                    break;
                }
            if (end < source.Length)
            {
                foreach (var span in spans)
                    if (span.Start < end && span.End > end)
                        end = span.Start;
                // Prefer paragraph, sentence, or word boundaries, but don't leave half a literal behind.
                for (var i = end - 1; i > start + Budget / 2; i--)
                {
                    if ((char.IsWhiteSpace(source[i]) || "。！？.!?;；".Contains(source[i]))
                        && !spans.Any(span => span.Start <= i && span.End > i))
                    {
                        end = i + 1;
                        break;
                    }
                }
                if (end > start && char.IsHighSurrogate(source[end - 1]))
                    end--;
            }
            if (end <= start)
                throw new InvalidDataException("Cannot safely split the document within the character budget.");
            var chunk = source[start..end];
            var body = chunk.Trim();
            if (body.Length > 0)
            {
                var lead = chunk[..chunk.IndexOf(body, StringComparison.Ordinal)];
                yield return new DocumentPiece(body, lead, chunk[(lead.Length + body.Length)..], false, attach);
            }
            else
                yield return new DocumentPiece("", chunk, "", true, attach);
            start = end;
            attach = true;
        }
    }
}
