using System.Text;

namespace Core;

/// <summary>The prompt of a request: Compose joins the pieces into the text. An adapter with a limit on the text cuts each piece
/// to the same number of bytes, the largest that fits, and ends a cut piece with "[cut]". With no cuttable piece, the text is Compose([]).</summary>
public sealed record AgentPrompt(IReadOnlyList<string> Pieces, Func<IReadOnlyList<string>, string> Compose)
{
    private const string CutMarker = "[cut]";

    /// <summary>The text of the prompt, with each piece whole.</summary>
    public string Text => Compose(Pieces);

    /// <summary>The text within <paramref name="limit"/> bytes of UTF-8, or null when the fixed part alone is over the limit.</summary>
    public string? Fit(int limit)
    {
        string whole = Text;
        if (Bytes(whole) <= limit)
        {
            return whole;
        }

        int low = 0;
        int high = Pieces.Count is 0 ? 0 : Pieces.Max(Bytes);
        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);
            if (Bytes(CutTo(middle)) <= limit)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        string result = CutTo(low);
        return Bytes(result) <= limit ? result : null;
    }

    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private string CutTo(int size) => Compose([.. Pieces.Select(piece => Cut(piece, size))]);

    private static string Cut(string text, int size)
    {
        if (Bytes(text) <= size)
        {
            return text;
        }

        int budget = size - Bytes(CutMarker);
        var builder = new StringBuilder();
        int used = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            used += rune.Utf8SequenceLength;
            if (used > budget)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.Append(CutMarker).ToString();
    }
}
