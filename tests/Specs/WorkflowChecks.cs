using System.Text.RegularExpressions;
using Specs.Unit;

namespace Specs;

/// <summary>The checks the starter's workflow scenarios make on a copy. Each throws <see cref="InvalidOperationException"/> when it fails.</summary>
public static partial class WorkflowChecks
{
    private const string Checkout = "actions/checkout@";
    private const string Setup = "actions/setup-dotnet@";

    /// <summary>Fails when the directory lacks one of the files.</summary>
    public static void HoldsFiles(string directory, params string[] names)
    {
        foreach (string name in names.Where(name => !File.Exists(Path.Combine(directory, name))))
        {
            throw new InvalidOperationException($"{directory} lacks {name}");
        }
    }

    /// <summary>Fails when the directory holds a file other than the names.</summary>
    public static void HoldsNoOther(string directory, params string[] names)
    {
        string? other = StarterChecks.Files(directory).FirstOrDefault(file => !names.Contains(file));
        if (other is not null)
        {
            throw new InvalidOperationException($"{directory} holds the other file {other}");
        }
    }

    /// <summary>Fails when there is no request, or when a request does not carry the value in the Authorization header.</summary>
    public static void HoldsHeader(IEnumerable<TimedRequest> requests, string header, string value)
    {
        if (header is not "Authorization")
        {
            throw new InvalidOperationException($"the header {header} is not recorded");
        }

        List<TimedRequest> list = [.. requests];
        if (list.Count is 0)
        {
            throw new InvalidOperationException("no request was made");
        }

        TimedRequest? other = list.Find(r => r.Authorization != value);
        if (other is not null)
        {
            throw new InvalidOperationException($"the request {other.Path} carries {other.Authorization}, not {value}");
        }
    }

    /// <summary>Fails when the workflow has no trigger for the event, with the type when one is given.</summary>
    public static void Trigger(string file, string eventName, string? type = null)
    {
        Dictionary<string, object?> on = Map(Root(file), "on", file);
        bool ok = on.TryGetValue(eventName, out object? body)
            && (type is null || (body is Dictionary<string, object?> map && List(map.GetValueOrDefault("types")).SequenceEqual([type])));
        if (!ok)
        {
            throw new InvalidOperationException($"{file} has no trigger {eventName}" + (type is null ? "" : $" with the type {type}"));
        }
    }

    /// <summary>Fails when the workflow's triggers are not exactly issues and workflow_dispatch.</summary>
    public static void NoOtherTrigger(string file)
    {
        Dictionary<string, object?> on = Map(Root(file), "on", file);
        if (on.Count is not 2 || !on.ContainsKey("issues") || !on.ContainsKey("workflow_dispatch"))
        {
            throw new InvalidOperationException($"{file} does not have exactly the triggers issues and workflow_dispatch");
        }
    }

    /// <summary>Fails when the trigger has no input that is required with the type.</summary>
    public static void RequiredInput(string file, string eventName, string input, string type)
    {
        Dictionary<string, object?>? map = Input(file, eventName, input);
        if (map is null || map.GetValueOrDefault("required") is not "true" || (map.GetValueOrDefault("type") as string) != type)
        {
            throw new InvalidOperationException($"the trigger {eventName} of {file} has no required input {input} with the type {type}");
        }
    }

    /// <summary>Fails when the trigger has no input with the default.</summary>
    public static void InputDefault(string file, string eventName, string input, string value)
    {
        if ((Input(file, eventName, input)?.GetValueOrDefault("default") as string) != value)
        {
            throw new InvalidOperationException($"the trigger {eventName} of {file} has no input {input} with the default {value}");
        }
    }

    private static Dictionary<string, object?>? Input(string file, string eventName, string input)
        => (Map(Root(file), "on", file).GetValueOrDefault(eventName) as Dictionary<string, object?>)?.GetValueOrDefault("inputs") is Dictionary<string, object?> inputs
            ? inputs.GetValueOrDefault(input) as Dictionary<string, object?>
            : null;

    /// <summary>Fails when the workflow's triggers are not the first event and the second event on the branch.</summary>
    public static void TwoTriggers(string file, string first, string second, string branch)
    {
        Dictionary<string, object?> on = Map(Root(file), "on", file);
        if (on.Count is not 2 || !on.ContainsKey(first) || !on.TryGetValue(second, out object? body)
            || body is not Dictionary<string, object?> map || !List(map.GetValueOrDefault("branches")).SequenceEqual([branch]) || map.Count is not 1)
        {
            throw new InvalidOperationException($"{file} does not have exactly the triggers {first} and {second} on {branch}");
        }
    }

    /// <summary>Fails when the workflow's permissions are not the text: <c>{}</c>, or one <c>key: value</c>.</summary>
    public static void Permissions(string file, string expected)
    {
        object? actual = Root(file).GetValueOrDefault("permissions");
        bool ok = expected.Contains(": ", StringComparison.Ordinal)
            ? actual is Dictionary<string, object?> { Count: 1 } map && map.TryGetValue(expected.Split(": ")[0], out object? value) && (value as string) == expected.Split(": ")[1]
            : (actual as string) == expected;
        if (!ok)
        {
            throw new InvalidOperationException($"{file} does not have the permissions {expected}");
        }
    }

    /// <summary>Fails when the workflow does not have exactly one job.</summary>
    public static void OneJob(string file)
    {
        int count = Jobs(file).Count;
        if (count is not 1)
        {
            throw new InvalidOperationException($"{file} has {count} jobs");
        }
    }

    /// <summary>Fails when the workflow's one job lacks the runner or the condition.</summary>
    public static void JobRuns(string file, string runner, string condition)
    {
        Dictionary<string, object?> job = Jobs(file).Values.First() as Dictionary<string, object?> ?? throw new InvalidOperationException($"{file} has no job");
        if ((job.GetValueOrDefault("runs-on") as string) != runner || (job.GetValueOrDefault("if") as string) != condition)
        {
            throw new InvalidOperationException($"the job of {file} does not run on {runner} with the condition {condition}");
        }
    }

    /// <summary>Fails when the first step of the one job is not the checkout of the repo at the ref with no `token:` key.</summary>
    public static void FirstStepChecksOut(string file, string repository, string reference)
    {
        Dictionary<string, object?> step = Steps(OnlyJob(file)).FirstOrDefault() ?? throw new InvalidOperationException($"{file} has no step");
        Dictionary<string, object?> with = With(step);
        if (!IsUses(step, Checkout) || (with.GetValueOrDefault("repository") as string) != repository
            || (with.GetValueOrDefault("ref") as string) != reference || with.ContainsKey("token"))
        {
            throw new InvalidOperationException($"the first step of {file} is not the checkout of {repository} at {reference} with no token");
        }
    }

    /// <summary>Fails when no setup step that reads global.json comes before the step with the run command.</summary>
    public static void SetupBeforeRun(string file, string run)
    {
        List<Dictionary<string, object?>> steps = Steps(OnlyJob(file));
        int runIndex = steps.FindIndex(step => (step.GetValueOrDefault("run") as string) == run);
        if (runIndex < 0 || !steps.Take(runIndex).Any(IsSetup))
        {
            throw new InvalidOperationException($"{file} has no SDK setup before the step that runs {run}");
        }
    }

    /// <summary>Fails when the last two steps of the one job are not a step that runs the command and then a step with the name and the condition.</summary>
    public static void LastTwoSteps(string file, string run, string name, string condition)
    {
        List<Dictionary<string, object?>> steps = Steps(OnlyJob(file));
        if (steps.Count < 2 || (steps[^2].GetValueOrDefault("run") as string) != run
            || (steps[^1].GetValueOrDefault("name") as string) != name || (steps[^1].GetValueOrDefault("if") as string) != condition)
        {
            throw new InvalidOperationException($"the last two steps of {file} are not a step that runs {run} and then the step {name} with the condition {condition}");
        }
    }

    /// <summary>Fails when the step with the run command has an environment other than the names, each from the secret of its name.</summary>
    public static void RunStepEnvironment(string file, string run, params string[] names)
    {
        Dictionary<string, object?>? step = Jobs(file).Values.SelectMany(job => Steps(job as Dictionary<string, object?> ?? [])).FirstOrDefault(s => (s.GetValueOrDefault("run") as string) == run)
            ?? throw new InvalidOperationException($"{file} has no step that runs {run}");
        Dictionary<string, object?> env = step.GetValueOrDefault("env") as Dictionary<string, object?> ?? [];
        if (env.Count != names.Length || names.Any(name => (env.GetValueOrDefault(name) as string) != "${{ secrets." + name + " }}"))
        {
            throw new InvalidOperationException($"the step that runs {run} in {file} does not have exactly {string.Join(", ", names)} from their secrets");
        }
    }

    /// <summary>Fails when a step other than the one with the run command has an environment, or the workflow or a job has one.</summary>
    public static void NoOtherStepEnvironment(string file, string run)
    {
        Dictionary<string, object?> root = Root(file);
        bool other = root.ContainsKey("env") || Jobs(file).Values.Any(job => job is Dictionary<string, object?> map
            && (map.ContainsKey("env") || Steps(map).Any(step => step.ContainsKey("env") && (step.GetValueOrDefault("run") as string) != run)));
        if (other)
        {
            throw new InvalidOperationException($"{file} has an environment beside the one of the step that runs {run}");
        }
    }

    /// <summary>Fails when the jobs are not exactly the names, each on the runner.</summary>
    public static void JobsOn(string file, string runner, params string[] names)
    {
        Dictionary<string, object?> jobs = Jobs(file);
        if (jobs.Count != names.Length || names.Any(name => jobs.GetValueOrDefault(name) is not Dictionary<string, object?> job || (job.GetValueOrDefault("runs-on") as string) != runner))
        {
            throw new InvalidOperationException($"{file} does not have exactly the jobs {string.Join(", ", names)} on {runner}");
        }
    }

    /// <summary>Fails when the job does not need the other job.</summary>
    public static void Needs(string file, string job, string needed)
    {
        if ((Job(file, job).GetValueOrDefault("needs") as string) != needed)
        {
            throw new InvalidOperationException($"the job {job} of {file} does not need {needed}");
        }
    }

    /// <summary>Fails when a job does not check out first and set up the SDK before its first dotnet command.</summary>
    public static void EachJobChecksOutAndSetsUp(string file)
    {
        foreach ((string name, object? value) in Jobs(file))
        {
            List<Dictionary<string, object?>> steps = Steps(value as Dictionary<string, object?> ?? []);
            int firstDotnet = steps.FindIndex(step => step.GetValueOrDefault("run") is string run && run.StartsWith("dotnet ", StringComparison.Ordinal));
            int setup = steps.FindIndex(IsSetup);
            if (steps.Count is 0 || !IsUses(steps[0], Checkout) || With(steps[0]).ContainsKey("repository")
                || setup < 1 || (firstDotnet >= 0 && setup > firstDotnet))
            {
                throw new InvalidOperationException($"the job {name} of {file} does not check out and set up the SDK before its first dotnet command");
            }
        }
    }

    /// <summary>Fails when the run commands of the job are not the commands, in order.</summary>
    public static void JobRunsInOrder(string file, string job, IEnumerable<string> commands)
    {
        if (!Runs(file, job).SequenceEqual(commands))
        {
            throw new InvalidOperationException($"the job {job} of {file} does not run exactly the expected commands in order");
        }
    }

    /// <summary>Fails when the job does not run the first command and then the second.</summary>
    public static void JobRunsThen(string file, string job, string first, string second)
    {
        List<string> runs = Runs(file, job);
        int one = runs.IndexOf(first);
        if (one < 0 || runs.IndexOf(second) <= one)
        {
            throw new InvalidOperationException($"the job {job} of {file} does not run {first} and then {second}");
        }
    }

    private static List<string> Runs(string file, string job)
        => [.. Steps(Job(file, job)).Select(step => step.GetValueOrDefault("run")).OfType<string>()];

    private static Dictionary<string, object?> Root(string file) => Parse(File.ReadAllText(file));

    private static Dictionary<string, object?> Jobs(string file) => Map(Root(file), "jobs", file);

    private static Dictionary<string, object?> Job(string file, string name)
        => Jobs(file).GetValueOrDefault(name) as Dictionary<string, object?> ?? throw new InvalidOperationException($"{file} has no job {name}");

    private static Dictionary<string, object?> OnlyJob(string file)
        => Jobs(file).Values.FirstOrDefault() as Dictionary<string, object?> ?? throw new InvalidOperationException($"{file} has no job");

    private static Dictionary<string, object?> Map(Dictionary<string, object?> map, string key, string file)
        => map.GetValueOrDefault(key) as Dictionary<string, object?> ?? throw new InvalidOperationException($"{file} has no map {key}");

    private static List<Dictionary<string, object?>> Steps(Dictionary<string, object?> job)
        => job.GetValueOrDefault("steps") is List<object?> list ? [.. list.OfType<Dictionary<string, object?>>()] : [];

    private static Dictionary<string, object?> With(Dictionary<string, object?> step)
        => step.GetValueOrDefault("with") as Dictionary<string, object?> ?? [];

    private static bool IsUses(Dictionary<string, object?> step, string action)
        => step.GetValueOrDefault("uses") is string uses && uses.StartsWith(action, StringComparison.Ordinal);

    private static bool IsSetup(Dictionary<string, object?> step)
        => IsUses(step, Setup) && With(step).GetValueOrDefault("global-json-file") is "global.json";

    private static List<string> List(object? value)
        => value is string text && text.StartsWith('[') && text.EndsWith(']')
            ? [.. text[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];

    /// <summary>Reads the block YAML of a workflow: maps, lists and plain scalars. A flow value such as <c>[main]</c> or <c>{}</c> stays text.</summary>
    internal static Dictionary<string, object?> Parse(string yaml)
    {
        List<(int Indent, string Text)> lines = [.. yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#'))
            .Select(line => (line.Length - line.TrimStart().Length, line.Trim()))];
        int pos = 0;
        return Block(lines, ref pos, lines.Count > 0 ? lines[0].Indent : 0) as Dictionary<string, object?> ?? [];
    }

    private static object? Block(List<(int Indent, string Text)> lines, ref int pos, int indent)
    {
        if (lines[pos].Text.StartsWith("- ", StringComparison.Ordinal))
        {
            List<object?> list = [];
            while (pos < lines.Count && lines[pos].Indent == indent && lines[pos].Text.StartsWith("- ", StringComparison.Ordinal))
            {
                string rest = lines[pos].Text[2..].TrimStart();
                int inner = indent + (lines[pos].Text.Length - rest.Length);
                lines[pos] = (inner, rest);
                list.Add(KeyOf(rest) is not null ? Block(lines, ref pos, inner) : lines[pos++].Text);
            }

            return list;
        }

        Dictionary<string, object?> map = [];
        while (pos < lines.Count && lines[pos].Indent == indent && !lines[pos].Text.StartsWith("- ", StringComparison.Ordinal))
        {
            (string key, string value) = KeyOf(lines[pos].Text) ?? throw new InvalidOperationException($"not a key: {lines[pos].Text}");
            pos++;
            map[key] = value.Length > 0 ? value
                : pos < lines.Count && lines[pos].Indent > indent ? Block(lines, ref pos, lines[pos].Indent) : null;
        }

        return map;
    }

    private static (string Key, string Value)? KeyOf(string text)
    {
        Match match = KeyLine().Match(text);
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value.Trim()) : null;
    }

    [GeneratedRegex(@"^([A-Za-z_][\w.-]*):(?:\s+(.*))?$")]
    private static partial Regex KeyLine();
}
