using System.Text.RegularExpressions;

namespace Core;

/// <summary>What the scenarios of an issue did to a clone: the feature files they wrote and removed, or each fault that stopped them.</summary>
public sealed record IssueScenariosResult(IReadOnlyList<string> Faults, IReadOnlyList<string> Written, IReadOnlyList<string> Removed);

/// <summary>Reads <c>## Scenarios</c> and <c>## Removed scenarios</c> from the body of an issue, and applies them to the
/// feature files of a clone. A block that holds a <c>Feature:</c> line is a whole file. Any other block upserts its
/// scenarios by name. The clone changes only when the body and every resulting file are sound.</summary>
public static partial class IssueScenarios
{
    private const string ScenariosHeading = "## Scenarios";
    private const string RemovedHeading = "## Removed scenarios";
    private const string CriteriaHeading = "## Acceptance criteria";
    private const string FeatureDirectory = "specs/features/";
    private const string PathLine = "# " + FeatureDirectory;

    /// <summary>Tells whether the body holds the heading <c>## Scenarios</c> or <c>## Removed scenarios</c>.</summary>
    public static bool Holds(string body)
    {
        IReadOnlyList<string> headings = Read(body).Headings;
        return headings.Contains(ScenariosHeading) || headings.Contains(RemovedHeading);
    }

    /// <summary>Applies the scenarios of <paramref name="body"/> to the feature files under <paramref name="workTree"/>.
    /// Writes and removes nothing when any fault is found, and gives back every fault.</summary>
    public static IssueScenariosResult Apply(string body, string workTree)
    {
        Body read = Read(body);
        var faults = new List<string>();
        var blocks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var twice = new HashSet<string>(StringComparer.Ordinal);
        bool hasScenarios = read.Headings.Contains(ScenariosHeading);
        if (hasScenarios && read.Headings.Contains(CriteriaHeading))
        {
            faults.Add($"The body holds both {ScenariosHeading} and {CriteriaHeading}. Keep one of the two.");
        }

        List<Block> scenarioBlocks = [.. read.Blocks.Where(block => block.Heading is ScenariosHeading)];
        if (hasScenarios && scenarioBlocks.Count is 0)
        {
            faults.Add($"The heading {ScenariosHeading} holds no fenced block. Put the scenarios of each feature file in a fenced block under it.");
        }

        foreach (Block block in scenarioBlocks)
        {
            if (!block.Closed)
            {
                faults.Add($"A fenced block under the heading {ScenariosHeading} has no closing fence.");
                continue;
            }

            string first = block.Lines.Count is 0 ? "" : block.Lines[0].TrimEnd();
            if (!first.StartsWith("# ", StringComparison.Ordinal) || !FeaturePath().IsMatch(first[2..]))
            {
                faults.Add($"Each fenced block under the heading {ScenariosHeading} starts with the line {PathLine}<name>.feature. A block starts with '{first}'.");
                continue;
            }

            string path = first[2..];
            if (blocks.ContainsKey(path))
            {
                if (twice.Add(path))
                {
                    faults.Add($"Two blocks under the heading {ScenariosHeading} name {path}.");
                }

                continue;
            }

            blocks[path] = [.. block.Lines.Skip(1)];
        }

        foreach (Block block in read.Blocks.Where(block => block.Heading is not ScenariosHeading))
        {
            if (block.Lines.Count > 0 && block.Lines[0].StartsWith(PathLine, StringComparison.Ordinal))
            {
                faults.Add($"A fenced block that starts with {PathLine} sits outside the heading {ScenariosHeading}.");
            }
        }

        var removals = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string line in read.RemovedLines.Select(line => line.Trim()).Where(line => line.Length > 0))
        {
            Match item = RemovedItem().Match(line);
            if (!item.Success || !FeaturePath().IsMatch(item.Groups["path"].Value))
            {
                faults.Add($"Each line under the heading {RemovedHeading} is a list item: a path in backticks, a colon, and a scenario name. A line reads '{line}'.");
                continue;
            }

            string path = item.Groups["path"].Value;
            string name = item.Groups["name"].Value.Trim();
            if (!removals.TryGetValue(path, out List<string>? names))
            {
                removals[path] = names = [];
            }

            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        var results = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in blocks.Keys.Union(removals.Keys).Except(twice).Order(StringComparer.Ordinal))
        {
            string full = Path.Combine(workTree, path);
            string? main = File.Exists(full) ? File.ReadAllText(full) : null;
            List<string> removed = removals.GetValueOrDefault(path) ?? [];
            string? result = blocks.TryGetValue(path, out List<string>? lines)
                ? ApplyBlock(path, lines, main, removed, faults)
                : ApplyRemovals(path, main, removed, faults);
            if (result is not null)
            {
                results[path] = result;
            }
        }

        var written = new List<string>();
        var gone = new List<string>();
        foreach ((string path, string text) in results)
        {
            if (!Sound(path, text, faults, out int scenarios))
            {
                continue;
            }

            if (scenarios is 0 && !IsWholeFile(blocks.GetValueOrDefault(path)))
            {
                gone.Add(path);
            }
            else
            {
                written.Add(path);
            }
        }

        if (faults.Count is 0)
        {
            string features = Path.Combine(workTree, FeatureDirectory);
            int left = (Directory.Exists(features) ? Directory.EnumerateFiles(features, "*.feature", SearchOption.AllDirectories) : [])
                .Select(file => Path.GetRelativePath(workTree, file).Replace('\\', '/'))
                .Union(written)
                .Except(gone)
                .Count();
            if (left is 0)
            {
                faults.Add($"The issue leaves no feature file under {FeatureDirectory}.");
            }
            else if (gone.Count is 0 && written.All(path => File.Exists(Path.Combine(workTree, path)) && File.ReadAllText(Path.Combine(workTree, path)) == results[path]))
            {
                faults.Add("The blocks and the removed scenarios leave every feature file as it is on main.");
            }
        }

        if (faults.Count > 0)
        {
            return new IssueScenariosResult(faults, [], []);
        }

        foreach (string path in written)
        {
            string full = Path.Combine(workTree, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, results[path]);
        }

        foreach (string path in gone)
        {
            File.Delete(Path.Combine(workTree, path));
        }

        return new IssueScenariosResult([], written, gone);
    }

    private static bool IsWholeFile(List<string>? lines) =>
        lines is not null && Significant(lines) is string first && first.StartsWith("Feature:", StringComparison.Ordinal);

    private static string? Significant(List<string> lines) =>
        lines.Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && line[0] is not ('#' or '@'));

    private static string? ApplyBlock(string path, List<string> lines, string? main, List<string> removed, List<string> faults)
    {
        if (IsWholeFile(lines))
        {
            if (removed.Count > 0)
            {
                faults.Add($"The file {path} has a whole-file block, so {RemovedHeading} names none of its scenarios.");
                return null;
            }

            string text = Text(lines);
            if (Outline(path, text, faults) is not FeatureOutline whole)
            {
                return null;
            }

            return Duplicates(path, whole, faults) ? null : text;
        }

        if (main is null)
        {
            faults.Add($"The file {path} is not on main, so its block holds a Feature: line and the whole file.");
            return null;
        }

        string? opening = Significant(lines);
        if (opening is not null && (opening.StartsWith("Background:", StringComparison.Ordinal) || opening.StartsWith("Rule:", StringComparison.Ordinal)))
        {
            faults.Add($"The block for {path} holds a Background: or a Rule:. Change them in a block that holds the whole file.");
            return null;
        }

        if (opening is not null && !ScenarioKeyword().IsMatch(opening))
        {
            faults.Add($"The block for {path} holds text before its first scenario. A block of scenarios starts with a comment, a tag or a scenario.");
            return null;
        }

        // The line "Feature: _" stands in for the path line, so a line number of the block's outline is its line in the block.
        List<string> blockLines = ["Feature: _", .. lines];
        if (Outline(path, Text(blockLines), faults) is not FeatureOutline block)
        {
            return null;
        }

        if (block.HasRule || block.HasBackground)
        {
            faults.Add($"The block for {path} holds a Background: or a Rule:. Change them in a block that holds the whole file.");
            return null;
        }

        if (block.HasDescription)
        {
            faults.Add($"The block for {path} holds text before its first scenario. A block of scenarios starts with a comment, a tag or a scenario.");
            return null;
        }

        if (Duplicates(path, block, faults))
        {
            return null;
        }

        return Splice(path, main, block, blockLines, removed, faults);
    }

    private static string? ApplyRemovals(string path, string? main, List<string> removed, List<string> faults)
    {
        if (main is null)
        {
            faults.Add($"The file {path}, under {RemovedHeading}, is not on main.");
            return null;
        }

        return Splice(path, main, null, [], removed, faults);
    }

    private static string? Splice(string path, string main, FeatureOutline? block, List<string> blockLines, List<string> removed, List<string> faults)
    {
        string text = main.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Outline(path, text, faults) is not FeatureOutline file)
        {
            return null;
        }

        if (file.HasRule)
        {
            faults.Add($"The file {path} holds a Rule:, so change its scenarios in a block that holds the whole file.");
            return null;
        }

        IReadOnlyList<ScenarioSpan> upserts = block?.Scenarios ?? [];
        int count = faults.Count;
        foreach (string name in upserts.Select(span => span.Name).Concat(removed).Distinct(StringComparer.Ordinal))
        {
            if (file.Scenarios.Count(span => span.Name == name) > 1)
            {
                faults.Add($"The feature file {path} holds two scenarios named '{name}'.");
            }
        }

        foreach (string name in removed)
        {
            if (file.Scenarios.All(span => span.Name != name))
            {
                faults.Add($"The feature file {path} holds no scenario named '{name}'.");
            }
            else if (upserts.Any(span => span.Name == name))
            {
                faults.Add($"The scenario '{name}' of {path} is under {RemovedHeading} and in a block under {ScenariosHeading}. Keep one of the two.");
            }
        }

        if (faults.Count > count)
        {
            return null;
        }

        List<string> lines = [.. text.Split('\n')];
        if (text.EndsWith('\n'))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var output = new List<string>();
        ScenarioSpan? lastSpan = file.Scenarios.Count is 0 ? null : file.Scenarios.MaxBy(span => span.FirstLine);
        int at = 1;
        while (at <= lines.Count)
        {
            ScenarioSpan? span = file.Scenarios.FirstOrDefault(span => span.FirstLine == at);
            if (span is null)
            {
                output.Add(lines[at - 1]);
                at++;
                continue;
            }

            at = span.LastLine + 1;
            if (upserts.FirstOrDefault(upsert => upsert.Name == span.Name) is ScenarioSpan replacement)
            {
                output.AddRange(SpanLines(blockLines, replacement));
            }
            else if (removed.Contains(span.Name) && span == lastSpan)
            {
                TrimBlankEnd(output);
            }
            else if (removed.Contains(span.Name))
            {
                while (at <= lines.Count && lines[at - 1].Trim().Length is 0)
                {
                    at++;
                }
            }
            else
            {
                output.AddRange(lines[(span.FirstLine - 1)..span.LastLine]);
            }
        }

        foreach (ScenarioSpan upsert in upserts.Where(upsert => file.Scenarios.All(span => span.Name != upsert.Name)))
        {
            TrimBlankEnd(output);
            output.Add("");
            output.AddRange(SpanLines(blockLines, upsert));
        }

        return Text(output);
    }

    private static IEnumerable<string> SpanLines(List<string> lines, ScenarioSpan span) => lines[(span.FirstLine - 1)..span.LastLine];

    private static void TrimBlankEnd(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Trim().Length is 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private static string Text(IEnumerable<string> lines) => string.Concat(lines.Select(line => line + "\n"));

    private static FeatureOutline? Outline(string path, string text, List<string> faults)
    {
        try
        {
            return FeatureFiles.ReadOutline(path, text);
        }
        catch (FeatureFileException exception)
        {
            faults.Add($"The feature file {path} does not parse: {exception.Message}");
            return null;
        }
    }

    private static bool Duplicates(string path, FeatureOutline outline, List<string> faults)
    {
        string[] twice = [.. outline.Scenarios.GroupBy(span => span.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key)];
        foreach (string name in twice)
        {
            faults.Add($"The block for {path} holds two scenarios named '{name}'.");
        }

        return twice.Length > 0;
    }

    private static bool Sound(string path, string text, List<string> faults, out int scenarios)
    {
        scenarios = 0;
        if (Outline(path, text, faults) is not FeatureOutline outline)
        {
            return false;
        }

        int count = faults.Count;
        foreach (int line in FeatureFiles.ReadOverIndentedDocStringLines(path, text))
        {
            faults.Add($"The feature file {path}: the doc string at line {line} has text indented past its opening delimiter; start the text at the column of its opening delimiter.");
        }

        string[] names = [.. outline.Scenarios.Select(span => span.Name).Distinct(StringComparer.Ordinal)];
        foreach (IGrouping<string, string> group in names.GroupBy(FeatureFiles.ComputeTestName, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            string[] pair = [.. group];
            faults.Add($"The feature file {path} holds the scenarios '{pair[0]}' and '{pair[1]}', which give one test name.");
        }

        scenarios = outline.Scenarios.Count;
        return faults.Count == count;
    }

    private sealed record Block(string? Heading, List<string> Lines, bool Closed);

    private sealed record Body(IReadOnlyList<string> Headings, IReadOnlyList<Block> Blocks, IReadOnlyList<string> RemovedLines);

    /// <summary>Reads the headings, the fenced blocks and the lines under <c>## Removed scenarios</c>. A fenced block
    /// closes as GitHub closes it: at a fence line of its own mark, at least as long, indented 3 spaces or fewer.</summary>
    private static Body Read(string body)
    {
        var headings = new List<string>();
        var blocks = new List<Block>();
        var removed = new List<string>();
        string? heading = null;
        (char Mark, int Length, int Indent)? fence = null;
        List<string> content = [];
        foreach (string line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (fence is (char mark, int length, int indent))
            {
                if (Closes(line, mark, length))
                {
                    blocks.Add(new Block(heading, content, true));
                    fence = null;
                }
                else
                {
                    content.Add(StripIndent(line, indent));
                }

                continue;
            }

            if (Heading().IsMatch(line))
            {
                heading = line.Trim();
                headings.Add(heading);
                continue;
            }

            fence = Opens(line);
            if (fence is not null)
            {
                content = [];
            }

            if (heading is RemovedHeading)
            {
                removed.Add(line);
            }
        }

        if (fence is not null)
        {
            blocks.Add(new Block(heading, content, false));
        }

        return new Body(headings, blocks, removed);
    }

    private static (char Mark, int Length, int Indent)? Opens(string line)
    {
        int indent = line.TakeWhile(c => c is ' ').Count();
        if (indent > 3 || indent == line.Length || line[indent] is not ('`' or '~'))
        {
            return null;
        }

        char mark = line[indent];
        int length = line[indent..].TakeWhile(c => c == mark).Count();
        if (length < 3 || (mark is '`' && line[(indent + length)..].Contains('`', StringComparison.Ordinal)))
        {
            return null;
        }

        return (mark, length, indent);
    }

    private static bool Closes(string line, char mark, int length)
    {
        int indent = line.TakeWhile(c => c is ' ').Count();
        if (indent > 3)
        {
            return false;
        }

        int run = line[indent..].TakeWhile(c => c == mark).Count();
        return run >= length && line[(indent + run)..].Trim().Length is 0;
    }

    private static string StripIndent(string line, int indent)
    {
        int spaces = line.TakeWhile(c => c is ' ').Count();
        return line[Math.Min(spaces, indent)..];
    }

    [GeneratedRegex(@"^ {0,3}#{1,6}(?:[ \t]|$)")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^specs/features/[A-Za-z0-9][A-Za-z0-9._-]*\.feature$")]
    private static partial Regex FeaturePath();

    [GeneratedRegex(@"^[-*+][ \t]+`(?<path>[^`]+)`[ \t]*:[ \t]*(?<name>\S.*)$")]
    private static partial Regex RemovedItem();

    [GeneratedRegex(@"^(?:Scenario|Example|Scenario Outline|Scenario Template):")]
    private static partial Regex ScenarioKeyword();
}
