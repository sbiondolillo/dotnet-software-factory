using System.Globalization;
using System.Text;

namespace Core;

/// <summary>
/// The job summary of a factory run: one table row for each phase that ran after the clone and one list item for each Redo text.
/// A Redo text is named after the row of the phase that produced it: <c>test N</c>, <c>check N</c> or <c>review N</c>;
/// the scope faults of a cleanup run (found after the lint gate, before the test gate) are named after <c>build N</c>,
/// and a lost test is named after <c>test N</c>, whose gate found it.
/// </summary>
public sealed class JobSummary
{
    private readonly List<string> _rows = [];
    private readonly List<string> _redos = [];
    private readonly List<SessionCost> _sessions = [];
    private readonly Dictionary<string, int> _counts = [];
    private TimeSpan _pendingTime = TimeSpan.Zero;
    private int _pendingTurns;
    private decimal _pendingCost;

    /// <summary>True when no phase has run yet.</summary>
    public bool IsEmpty => _rows.Count is 0;

    /// <summary>Notes one answer of <c>claude</c>; the next row takes the sum of the answers noted since the row before, rounded to four decimal places.</summary>
    public void Prompt(TimeSpan elapsed, int turns, decimal cost)
    {
        _pendingTime += elapsed;
        _pendingTurns += turns;
        _pendingCost += cost;
    }

    /// <summary>Adds the row of <paramref name="phase"/> and gives its name; <paramref name="elapsed"/> null takes the time of the noted answers.</summary>
    public string AddRow(string phase, TimeSpan? elapsed, string result)
    {
        int number = _counts.GetValueOrDefault(phase) + 1;
        _counts[phase] = number;
        string name = phase is "plan" ? phase : string.Create(CultureInfo.InvariantCulture, $"{phase} {number}");
        long seconds = (long)(elapsed ?? _pendingTime).TotalSeconds;
        _rows.Add(string.Create(CultureInfo.InvariantCulture, $"| {name} | {seconds} | {_pendingTurns} | ${Math.Round(_pendingCost, 4, MidpointRounding.AwayFromZero)} | {result} |"));
        _pendingTime = TimeSpan.Zero;
        _pendingTurns = 0;
        _pendingCost = 0m;
        return name;
    }

    /// <summary>Adds one item for each text of the Redo number <paramref name="redo"/>, which follows the row <paramref name="after"/>.</summary>
    public void AddRedos(int redo, string after, IReadOnlyList<string> texts)
    {
        foreach (string text in texts)
        {
            _redos.Add(string.Create(CultureInfo.InvariantCulture, $"- Redo {redo}, after {after}: {FirstLine(text)}"));
        }
    }

    /// <summary>Adds the Managed Agents sessions of the run, one line with each id, role and cost.</summary>
    public void AddSessions(IReadOnlyList<SessionCost> sessions) => _sessions.AddRange(sessions);

    /// <summary>The Markdown text, ending with a line feed.</summary>
    public string Render()
    {
        var text = new StringBuilder("## Factory run\n\n| phase | seconds | turns | spend | result |\n|---|---|---|---|---|\n");
        foreach (string row in _rows)
        {
            text.Append(row).Append('\n');
        }

        if (_redos.Count > 0)
        {
            text.Append("\n### Redos\n\n");
            foreach (string item in _redos)
            {
                text.Append(item).Append('\n');
            }
        }

        if (_sessions.Count > 0)
        {
            text.Append("\n### Sessions\n\n");
            foreach (SessionCost session in _sessions)
            {
                text.Append(CultureInfo.InvariantCulture, $"- {session.Id}, {session.Role}, {Deliver.Dollars(session.Cents)}\n");
            }
        }

        return text.ToString();
    }

    private static string FirstLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return "";
    }
}
