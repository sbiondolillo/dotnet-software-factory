using System.Text;

namespace Core;

/// <summary>Composes the body of a comment from a lead and texts, and cuts the texts to GitHub's limit.</summary>
internal static class CommentText
{
    private const int CommentLimit = 262144;
    private const string CutMarker = "[cut]";

    /// <summary>Gives the lead and every text, each text cut at its end when the whole is over the limit in UTF-8 bytes.</summary>
    public static string Fit(string lead, IReadOnlyList<string> texts)
    {
        string body = Compose(lead, texts);
        if (Bytes(body) <= CommentLimit)
        {
            return body;
        }

        int low = Bytes(CutMarker);
        int high = texts.Count is 0 ? 0 : texts.Max(Bytes);
        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);
            if (Bytes(Compose(lead, texts.Select(text => Cut(text, middle)))) <= CommentLimit)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Compose(lead, texts.Select(text => Cut(text, low)));
    }

    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private static string Compose(string lead, IEnumerable<string> texts)
    {
        var builder = new StringBuilder(lead);
        foreach (string text in texts)
        {
            builder.Append("\n\n").Append(text);
        }

        return builder.ToString();
    }

    private static string Cut(string text, int limit)
    {
        if (Bytes(text) <= limit)
        {
            return text;
        }

        int budget = limit - Bytes(CutMarker);
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
