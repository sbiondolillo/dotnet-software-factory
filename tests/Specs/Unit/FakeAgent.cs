using Core;

namespace Specs.Unit;

/// <summary>
/// The one fake <see cref="Agent"/> of the tests. It records each <see cref="AgentRequest"/>, runs the acts of the prompt on the
/// work tree, writes the files a test stages under <c>.factory/</c> of the work tree, and answers with the <see cref="AgentAnswer"/>
/// queued for the prompt, or else the last one queued for every prompt, or else <see cref="Done"/>. As an adapter does,
/// it deletes each output the request names before the prompt, and gives the text of each one on disk after it.
/// </summary>
/// <remarks>
/// A script names its prompt in one of two ways. A prompt number counts every request of the agent, from 1. A phase and a number
/// count the requests of that <see cref="Phase"/> alone, from 1, so the first build of a factory run is the build 1 after its plan.
/// The work tree is a function, so a test of the console program reads the clone that the program picks for each run.
/// </remarks>
public sealed class FakeAgent(Func<string> workTree) : Agent
{
    private readonly List<(When When, string Name, string? Text)> _staged = [];
    private readonly List<(When When, Action<string> Act)> _acts = [];
    private readonly List<(When When, Func<AgentAnswer> Answer)> _answers = [];
    private readonly List<When> _clears = [];
    private int _order;

    /// <summary>A fake over the one work tree <paramref name="workTree"/>.</summary>
    public FakeAgent(string workTree)
        : this(() => workTree)
    {
    }

    /// <summary>True, the default, gives the outputs that are on disk after the prompt, as an adapter does. False gives the
    /// outputs of the queued answer as they are, so a test can tell a phase that reads the answer from one that reads the disk.</summary>
    public bool ReadsDisk { get; set; } = true;

    /// <summary>Each request, in order.</summary>
    public List<AgentRequest> Requests { get; } = [];

    /// <summary>The prompt of each request, in order.</summary>
    public IReadOnlyList<string> Prompts => [.. Requests.Select(request => request.Prompt.Text)];

    /// <summary>The requests of <paramref name="phase"/>, in order.</summary>
    public IEnumerable<AgentRequest> Of(Phase phase) => Requests.Where(request => request.Phase == phase);

    /// <summary>Called with each request before its acts run, so a harness can note the state of the run at each prompt.</summary>
    public Action<AgentRequest>? OnRequest { get; set; }

    /// <summary>The request that a real adapter carries to its fake API now, which <see cref="Play"/> answers. Null between prompts.</summary>
    public AgentRequest? Pending { get; set; }

    /// <summary>A prompt of <paramref name="text"/> with no cuttable piece.</summary>
    public static AgentPrompt Whole(string text) => new([], _ => text);

    /// <summary>An answer with no error, no output and no fault.</summary>
    public static AgentAnswer Done(string result = "done") => new(result, 1, 0m, false, new Dictionary<string, string>(), []);

    /// <summary>An answer with the error flag and <paramref name="result"/>.</summary>
    public static AgentAnswer Error(string result) => new(result, 1, 0m, true, new Dictionary<string, string>(), []);

    /// <summary>Queues <paramref name="answer"/> for the prompt number <paramref name="prompt"/>, from 1; null queues it for every prompt.</summary>
    public void Answer(AgentAnswer answer, int? prompt = null) => _answers.Add((Next(null, prompt), () => answer));

    /// <summary>Queues <paramref name="answer"/> for the prompt <paramref name="number"/> of <paramref name="phase"/>, from 1; null
    /// queues it for every prompt of the phase.</summary>
    public void Answer(Phase phase, AgentAnswer answer, int? number = null) => _answers.Add((Next(phase, number), () => answer));

    /// <summary>Queues a throw of <paramref name="exception"/> for the prompt number <paramref name="prompt"/>, from 1; null queues it for every prompt.</summary>
    public void Throw(Exception exception, int? prompt = null) => _answers.Add((Next(null, prompt), () => throw exception));

    /// <summary>Queues a throw of <paramref name="exception"/> for the prompt <paramref name="number"/> of <paramref name="phase"/>.</summary>
    public void Throw(Phase phase, Exception exception, int? number = null) => _answers.Add((Next(phase, number), () => throw exception));

    /// <summary>Stages <paramref name="text"/> as the file <c>.factory/&lt;name&gt;</c> that the prompt number <paramref name="prompt"/>,
    /// from 1, writes; null writes it on every prompt. For each name, the most specific stage wins, as for an answer, and a
    /// <paramref name="text"/> of null writes no file.</summary>
    public void Stage(string name, string? text, int? prompt = null) => _staged.Add((Next(null, prompt), name, text));

    /// <summary>Stages <paramref name="text"/> as the file <c>.factory/&lt;name&gt;</c> that the prompt <paramref name="number"/> of
    /// <paramref name="phase"/> writes; null writes it on every prompt of the phase. A <paramref name="text"/> of null writes no file.</summary>
    public void Stage(Phase phase, string name, string? text, int? number = null) => _staged.Add((Next(phase, number), name, text));

    /// <summary>Runs <paramref name="act"/> on the work tree during the prompt number <paramref name="prompt"/>, from 1; null runs it on every prompt.</summary>
    public void Act(Action<string> act, int? prompt = null) => _acts.Add((Next(null, prompt), act));

    /// <summary>Runs <paramref name="act"/> on the work tree during the prompt <paramref name="number"/> of <paramref name="phase"/>;
    /// null runs it on every prompt of the phase.</summary>
    public void Act(Phase phase, Action<string> act, int? number = null) => _acts.Add((Next(phase, number), act));

    /// <summary>Drops each answer, act and staged file queued so far for the prompt <paramref name="number"/> of
    /// <paramref name="phase"/>, or for every prompt of the phase. A script queued after the call still runs.</summary>
    public void Clear(Phase phase, int? number = null) => _clears.Add(Next(phase, number));

    /// <summary>Answers <see cref="Pending"/>, as the agent behind a real adapter does.</summary>
    public AgentAnswer Play() =>
        RunAsync(Pending ?? throw new InvalidOperationException("No request is pending.")).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override Task<AgentAnswer> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        int number = Requests.Count;
        int inPhase = Requests.Count(r => r.Phase == request.Phase);
        OnRequest?.Invoke(request);
        string tree = workTree();
        string factory = Path.Combine(tree, ".factory");
        foreach (string name in request.Outputs)
        {
            if (File.Exists(Path.Combine(factory, name)))
            {
                File.Delete(Path.Combine(factory, name));
            }
        }

        foreach ((When when, Action<string> act) in _acts)
        {
            if (Applies(when, request.Phase, number, inPhase))
            {
                act(tree);
            }
        }

        foreach (IGrouping<string, (When When, string Name, string? Text)> staged in _staged
            .Where(s => Applies(s.When, request.Phase, number, inPhase))
            .GroupBy(s => s.Name, StringComparer.Ordinal))
        {
            if (Winner(staged, s => s.When).Text is string text)
            {
                Directory.CreateDirectory(factory);
                File.WriteAllText(Path.Combine(factory, staged.Key), text);
            }
        }

        Func<AgentAnswer> next = _answers.Any(a => Applies(a.When, request.Phase, number, inPhase))
            ? Winner(_answers.Where(a => Applies(a.When, request.Phase, number, inPhase)), a => a.When).Answer
            : () => Done();
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

    private When Next(Phase? phase, int? number) => new(phase, number, ++_order);

    /// <summary>The most specific of <paramref name="scripts"/>, and of those the last queued.</summary>
    private static T Winner<T>(IEnumerable<T> scripts, Func<T, When> when) =>
        scripts.OrderBy(s => when(s).Weight).ThenBy(s => when(s).Order).Last();

    private bool Applies(When when, Phase phase, int number, int inPhase) =>
        when.Matches(phase, number, inPhase) && !_clears.Any(clear => clear.Order > when.Order && clear.Matches(phase, number, inPhase));

    /// <summary>The prompts a script serves, and its place in the order of the queue.</summary>
    private readonly record struct When(Phase? Phase, int? Number, int Order)
    {
        /// <summary>A script for one prompt beats one for every prompt, and a script of a phase beats one of every phase.</summary>
        public int Weight => (Number is null ? 0 : 2) + (Phase is null ? 0 : 1);

        public bool Matches(Core.Phase phase, int number, int inPhase) => Phase is null
            ? Number is null || Number == number
            : Phase == phase && (Number is null || Number == inPhase);
    }
}
