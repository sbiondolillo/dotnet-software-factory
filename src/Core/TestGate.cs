using System.Globalization;

namespace Core;

/// <summary>What <see cref="TestGate"/> found.</summary>
/// <param name="Passed">True when every check held.</param>
/// <param name="Evidence">One text for each fact that failed the gate; empty when it passed.</param>
/// <param name="Tests">The tests of the TRX file, or null when the gate stopped before it read one.</param>
public sealed record TestGateResult(bool Passed, IReadOnlyList<string> Evidence, IReadOnlyList<ReportedTest>? Tests = null);

/// <summary>Judges a work tree the way CI does: the pin, the four build commands, the TRX file and the integration suite.</summary>
public sealed class TestGate(Sandbox sandbox, TextWriter? log = null)
{
    /// <summary>Runs the gate over <paramref name="workTree"/>; the integration command alone gets <paramref name="secretNames"/>.</summary>
    public Task<TestGateResult> RunAsync(
        string workTree,
        Pin pin,
        IReadOnlyList<string> secretNames,
        CancellationToken cancellationToken = default) =>
        RunAsync(workTree, pin, secretNames, null, cancellationToken);

    /// <summary>Runs the gate; a changed pinned path for which <paramref name="pinExempt"/> is true is no pin evidence.</summary>
    public async Task<TestGateResult> RunAsync(
        string workTree,
        Pin pin,
        IReadOnlyList<string> secretNames,
        Func<string, bool>? pinExempt,
        CancellationToken cancellationToken = default)
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

        string resultsName = ".test-gate-" + Guid.NewGuid().ToString("N");
        string hostResults = Path.Combine(workTree, resultsName);
        List<string> evidence = [];
        List<ReportedTest> tests = [];
        try
        {
            CommandResult test = await RunDotnetAsync(
                ["test", "--no-restore", "--logger", "trx", "--results-directory", $"/work/{resultsName}"],
                [],
                cancellationToken).ConfigureAwait(false);
            if (test.ExitCode is not 0)
            {
                evidence.Add(Describe("test", test));
            }

            if (Directory.Exists(hostResults))
            {
                foreach (string file in Directory.EnumerateFiles(hostResults, "*.trx").Order(StringComparer.Ordinal))
                {
                    tests.AddRange(TestReport.Read(file));
                }
            }

            if (test.ExitCode is not 0 && tests.Count is 0)
            {
                return new TestGateResult(false, evidence, tests);
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
        }
        finally
        {
            if (Directory.Exists(hostResults))
            {
                Directory.Delete(hostResults, recursive: true);
            }
        }

        if (evidence.Count > 0)
        {
            return new TestGateResult(false, evidence, tests);
        }

        CommandResult integration = await RunDotnetAsync(
            ["run", "--no-restore", "--project", "tests/Integration"],
            secretNames,
            cancellationToken).ConfigureAwait(false);
        return integration.ExitCode is 0
            ? new TestGateResult(true, [], tests)
            : new TestGateResult(false, [Describe("run", integration)], tests);
    }

    /// <summary>Runs the tests of <paramref name="workTree"/> and gives the tests that passed, as <c>class.method</c>.</summary>
    public async Task<IReadOnlySet<string>> ReadPassedTestsAsync(string workTree, CancellationToken cancellationToken = default)
    {
        await RunDotnetAsync(["restore", "--locked-mode"], [], cancellationToken).ConfigureAwait(false);
        string resultsName = ".test-base-" + Guid.NewGuid().ToString("N");
        string hostResults = Path.Combine(workTree, resultsName);
        HashSet<string> passed = new(StringComparer.Ordinal);
        try
        {
            await RunDotnetAsync(
                ["test", "--no-restore", "--logger", "trx", "--results-directory", $"/work/{resultsName}"],
                [],
                cancellationToken).ConfigureAwait(false);
            if (Directory.Exists(hostResults))
            {
                foreach (string file in Directory.EnumerateFiles(hostResults, "*.trx").Order(StringComparer.Ordinal))
                {
                    foreach (ReportedTest test in TestReport.Read(file).Where(test => test.Outcome is TestOutcome.Passed))
                    {
                        passed.Add($"{test.ClassName}.{test.Method}");
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(hostResults))
            {
                Directory.Delete(hostResults, recursive: true);
            }
        }

        return passed;
    }

    private async Task<CommandResult> RunDotnetAsync(string[] arguments, IReadOnlyList<string> secretNames, CancellationToken cancellationToken)
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
