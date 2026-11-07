using System.Globalization;

namespace Core;

/// <summary>What <see cref="TestGate"/> found.</summary>
/// <param name="Passed">True when every check held.</param>
/// <param name="Evidence">One text for each fact that failed the gate; empty when it passed.</param>
/// <param name="Tests">The tests of the TRX file, or null when the gate stopped before it read one.</param>
public sealed record TestGateResult(bool Passed, IReadOnlyList<string> Evidence, IReadOnlyList<ReportedTest>? Tests = null);

/// <summary>The test gate: it judges a work tree the way CI does, by the pin, the four build commands, the TRX file and the
/// integration suite, which <paramref name="skipIntegration"/> true leaves out. The integration command alone gets
/// <paramref name="secretNames"/>. A changed pinned path for which <paramref name="pinExempt"/> is true is no pin fault.
/// <paramref name="basePassed"/>, the tests that passed on the base commit as <c>class.method</c>, is the baseline of the
/// lost-test check, or null for a run with no such check.</summary>
public sealed class TestGate(
    Sandbox sandbox,
    string workTree,
    Pin pin,
    IReadOnlyList<string> secretNames,
    Func<string, bool>? pinExempt = null,
    IReadOnlySet<string>? basePassed = null,
    TextWriter? log = null,
    bool skipIntegration = false) : IGate
{
    /// <inheritdoc/>
    public GateName Name => GateName.Test;

    /// <summary>The tests of the TRX file of the last <see cref="RunAsync"/>, or null when it stopped before it read one.</summary>
    public IReadOnlyList<ReportedTest>? Tests { get; private set; }

    /// <summary>Gives the gate of a run. With a <paramref name="scope"/>, for a cleanup or a reframe, a pinned path in the scope
    /// that is not a feature file is no pin fault, and the gate first runs the tests of <paramref name="workTree"/>, which holds
    /// the base commit, for the baseline of the lost-test check.</summary>
    public static async Task<TestGate> CreateAsync(
        Sandbox sandbox,
        string workTree,
        Pin pin,
        IReadOnlyList<string> secretNames,
        IReadOnlyList<string>? scope,
        TextWriter? log = null,
        bool skipIntegration = false,
        CancellationToken cancellationToken = default)
    {
        if (scope is null)
        {
            return new TestGate(sandbox, workTree, pin, secretNames, log: log, skipIntegration: skipIntegration);
        }

        IReadOnlySet<string> passed = await ReadPassedTestsAsync(sandbox, workTree, log, cancellationToken).ConfigureAwait(false);
        return new TestGate(sandbox, workTree, pin, secretNames, path => PinExempt(scope, path), passed, log, skipIntegration);
    }

    /// <summary>Gives <c>failed</c> with one Redo text for each fact that failed the gate, or <c>passed</c>, with one Redo text
    /// for each test of the baseline that no longer passes.</summary>
    public async Task<GateOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        TestGateResult result = await JudgeAsync(cancellationToken).ConfigureAwait(false);
        Tests = result.Tests;
        if (!result.Passed)
        {
            return new GateOutcome("failed", result.Evidence);
        }

        return new GateOutcome("passed", basePassed is null ? [] : LostTests(basePassed, result));
    }

    private async Task<TestGateResult> JudgeAsync(CancellationToken cancellationToken)
    {
        PinComparison comparison = pin.Compare(workTree);
        List<string> pinEvidence = [.. comparison.Changed, .. comparison.Added, .. comparison.Removed];
        if (pinExempt is not null)
        {
            pinEvidence.RemoveAll(path => pinExempt(path));
        }

        if (pinEvidence.Count > 0)
        {
            return new TestGateResult(false, pinEvidence.ConvertAll(path => $"pin: {path}"));
        }

        foreach (string[] arguments in new string[][]
        {
            ["restore", "--locked-mode"],
            ["build", "--no-restore"],
            ["format", "--verify-no-changes", "--no-restore"],
        })
        {
            CommandResult result = await RunDotnetAsync(arguments, [], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode is not 0)
            {
                return new TestGateResult(false, [Describe(arguments[0], result)]);
            }
        }

        (CommandResult test, IReadOnlyList<ReportedTest> tests) = await RunTestsAsync(sandbox, workTree, ".test-gate-", log, cancellationToken).ConfigureAwait(false);
        List<string> evidence = [];
        if (test.ExitCode is not 0)
        {
            evidence.Add(Describe("test", test));
            if (tests.Count is 0)
            {
                return new TestGateResult(false, evidence, tests);
            }
        }

        foreach (ReportedTest reported in tests)
        {
            if (reported.Outcome is TestOutcome.Failed)
            {
                evidence.Add($"failed test: {reported.ClassName}.{reported.Method}: {reported.Message}");
            }
            else if (reported.Outcome is TestOutcome.Skipped)
            {
                evidence.Add($"skipped test: {reported.ClassName}.{reported.Method}");
            }
        }

        foreach ((string path, IReadOnlyList<string> titles) in FeatureFiles.ReadScenarioTitlesFromDirectory(Path.Combine(workTree, "specs", "features")))
        {
            foreach (string title in titles)
            {
                string name = FeatureFiles.ComputeTestName(title);
                if (!tests.Any(reported => reported.Method == name))
                {
                    evidence.Add($"no test: specs/features/{path}, scenario \"{title}\", test {name}");
                }
            }
        }

        if (evidence.Count > 0)
        {
            return new TestGateResult(false, evidence, tests);
        }

        if (skipIntegration)
        {
            return new TestGateResult(true, [], tests);
        }

        CommandResult integration = await RunDotnetAsync(
            ["run", "--no-restore", "--project", "tests/Integration"],
            secretNames,
            cancellationToken).ConfigureAwait(false);
        return integration.ExitCode is 0
            ? new TestGateResult(true, [], tests)
            : new TestGateResult(false, [Describe("run", integration)], tests);
    }

    private static async Task<IReadOnlySet<string>> ReadPassedTestsAsync(Sandbox sandbox, string workTree, TextWriter? log, CancellationToken cancellationToken)
    {
        await RunDotnetAsync(sandbox, log, ["restore", "--locked-mode"], [], cancellationToken).ConfigureAwait(false);
        (_, IReadOnlyList<ReportedTest> tests) = await RunTestsAsync(sandbox, workTree, ".test-base-", log, cancellationToken).ConfigureAwait(false);
        return tests.Where(test => test.Outcome is TestOutcome.Passed).Select(test => $"{test.ClassName}.{test.Method}").ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Runs the tests with a TRX logger into a results directory of its own under the work tree, named from
    /// <paramref name="prefix"/>, reads each TRX file in it in the order of the names, and removes the directory, also after a
    /// throw. A run that writes no TRX file gives an empty list.</summary>
    private static async Task<(CommandResult Run, IReadOnlyList<ReportedTest> Tests)> RunTestsAsync(
        Sandbox sandbox,
        string workTree,
        string prefix,
        TextWriter? log,
        CancellationToken cancellationToken)
    {
        string resultsName = prefix + Guid.NewGuid().ToString("N");
        string hostResults = Path.Combine(workTree, resultsName);
        try
        {
            CommandResult run = await RunDotnetAsync(
                sandbox,
                log,
                ["test", "--no-restore", "--logger", "trx", "--results-directory", $"{Sandbox.WorkDirectory}/{resultsName}"],
                [],
                cancellationToken).ConfigureAwait(false);
            List<ReportedTest> tests = [];
            if (Directory.Exists(hostResults))
            {
                foreach (string file in Directory.EnumerateFiles(hostResults, "*.trx").Order(StringComparer.Ordinal))
                {
                    tests.AddRange(TestReport.Read(file));
                }
            }

            return (run, tests);
        }
        finally
        {
            if (Directory.Exists(hostResults))
            {
                Directory.Delete(hostResults, recursive: true);
            }
        }
    }

    private static bool PinExempt(IReadOnlyList<string> scope, string path) =>
        CleanupBody.InScope(scope, path) && !path.StartsWith("specs/features/", StringComparison.Ordinal);

    private static IReadOnlyList<string> LostTests(IReadOnlySet<string> basePassed, TestGateResult gate)
    {
        HashSet<string> nowPassed = [.. (gate.Tests ?? []).Where(test => test.Outcome is TestOutcome.Passed).Select(test => $"{test.ClassName}.{test.Method}")];
        return [.. basePassed.Where(name => !nowPassed.Contains(name)).Order(StringComparer.Ordinal).Select(name => $"lost test: {name}")];
    }

    private Task<CommandResult> RunDotnetAsync(string[] arguments, IReadOnlyList<string> names, CancellationToken cancellationToken) =>
        RunDotnetAsync(sandbox, log, arguments, names, cancellationToken);

    private static async Task<CommandResult> RunDotnetAsync(Sandbox sandbox, TextWriter? log, string[] arguments, IReadOnlyList<string> secretNames, CancellationToken cancellationToken)
    {
        CommandResult result = await sandbox.RunAsync("dotnet", arguments, secretNames, cancellationToken).ConfigureAwait(false);
        await ProgressLine.WriteAsync(
            log,
            string.Create(CultureInfo.InvariantCulture, $"command: dotnet {string.Join(' ', arguments)} exited {result.ExitCode}")).ConfigureAwait(false);
        return result;
    }

    private static string Describe(string subcommand, CommandResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"dotnet {subcommand} exited with the code {result.ExitCode}\nstandard output: {result.StandardOutput}\nstandard error: {result.StandardError}");
}
