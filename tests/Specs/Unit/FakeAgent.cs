using Core;

namespace Specs.Unit;

/// <summary>
/// The one fake <see cref="Agent"/> of the tests. It records each <see cref="AgentRequest"/>, runs the acts of the prompt on the
/// work tree, writes the files a test stages under <c>.factory/</c> of the work tree, and answers with the <see cref="AgentAnswer"/>
/// queued for the prompt, or else the last one queued for every prompt, or else <see cref="Done"/>. As an adapter does,
/// it deletes each output the request names before the prompt, and gives the text of each one on disk after it.
/// </summary>
public sealed class FakeAgent(string workTree) : Agent
{
    private readonly List<(int? Prompt, string Name, string Text)> _staged = [];
    private readonly List<(int? Prompt, Action<string> Act)> _acts = [];
    private readonly List<(int? Prompt, Func<AgentAnswer> Answer)> _answers = [];

    /// <summary>True, the default, gives the outputs that are on disk after the prompt, as an adapter does. False gives the
    /// outputs of the queued answer as they are, so a test can tell a phase that reads the answer from one that reads the disk.</summary>
    public bool ReadsDisk { get; set; } = true;

    /// <summary>Each request, in order.</summary>
    public List<AgentRequest> Requests { get; } = [];

    /// <summary>The prompt of each request, in order.</summary>
    public IReadOnlyList<string> Prompts => [.. Requests.Select(request => request.Prompt.Text)];

    /// <summary>A prompt of <paramref name="text"/> with no cuttable piece.</summary>
    public static AgentPrompt Whole(string text) => new([], _ => text);

    /// <summary>An answer with no error, no output and no fault.</summary>
    public static AgentAnswer Done(string result = "done") => new(result, 1, 0m, false, new Dictionary<string, string>(), []);

    /// <summary>An answer with the error flag and <paramref name="result"/>.</summary>
    public static AgentAnswer Error(string result) => new(result, 1, 0m, true, new Dictionary<string, string>(), []);

    /// <summary>Queues <paramref name="answer"/> for the prompt number <paramref name="prompt"/>, from 1; null queues it for every prompt.</summary>
    public void Answer(AgentAnswer answer, int? prompt = null) => _answers.Add((prompt, () => answer));

    /// <summary>Queues a throw of <paramref name="exception"/> for the prompt number <paramref name="prompt"/>, from 1; null queues it for every prompt.</summary>
    public void Throw(Exception exception, int? prompt = null) => _answers.Add((prompt, () => throw exception));

    /// <summary>Stages <paramref name="text"/> as the file <c>.factory/&lt;name&gt;</c> that the prompt number <paramref name="prompt"/>,
    /// from 1, writes; null writes it on every prompt.</summary>
    public void Stage(string name, string text, int? prompt = null) => _staged.Add((prompt, name, text));

    /// <summary>Runs <paramref name="act"/> on the work tree during the prompt number <paramref name="prompt"/>, from 1; null runs it on every prompt.</summary>
    public void Act(Action<string> act, int? prompt = null) => _acts.Add((prompt, act));

    /// <inheritdoc />
    public override Task<AgentAnswer> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        int number = Requests.Count;
        string factory = Path.Combine(workTree, ".factory");
        foreach (string name in request.Outputs)
        {
            if (File.Exists(Path.Combine(factory, name)))
            {
                File.Delete(Path.Combine(factory, name));
            }
        }

        foreach ((int? prompt, Action<string> act) in _acts)
        {
            if (prompt is null || prompt == number)
            {
                act(workTree);
            }
        }

        foreach ((int? prompt, string name, string text) in _staged)
        {
            if (prompt is null || prompt == number)
            {
                Directory.CreateDirectory(factory);
                File.WriteAllText(Path.Combine(factory, name), text);
            }
        }

        Func<AgentAnswer> next = _answers.LastOrDefault(a => a.Prompt == number).Answer
            ?? _answers.LastOrDefault(a => a.Prompt is null).Answer
            ?? (() => Done());
        AgentAnswer answer = next();
        if (!ReadsDisk)
        {
            return Task.FromResult(answer);
        }

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in request.Outputs)
        {
            string path = Path.Combine(factory, name);
            if (File.Exists(path))
            {
                outputs[name] = File.ReadAllText(path);
            }
        }

        return Task.FromResult(answer with { Outputs = outputs });
    }
}
