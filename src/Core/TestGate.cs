using System.Globalization;

namespace Core;

/// <summary>What <see cref="TestGate"/> found.</summary>
/// <param name="Passed">True when every check held.</param>
/// <param name="Evidence">One text for each fact that failed the gate; empty when it passed.</param>
public sealed record TestGateResult(bool Passed, IReadOnlyList<string> Evidence);

/// <summary>Judges a work tree the way CI does: the pin, the four build commands, the TRX file and the integration suite.</summary>
public sealed class TestGate(Sandbox sandbox)
{
    /// <summary>Runs the gate over <paramref name="workTree"/>; the integration command alone gets <paramref name="secretNames"/>.</summary>
    public async Task<TestGateResult> RunAsync(
        string workTree,
        Pin pin,
        IReadOnlyList<string> secretNames,
        CancellationToken cancellationToken = default)
    {
        PinComparison comparison = pin.Compare(workTree);
        List<string> pinEvidence = [.. comparison.Changed, .. comparison.Added, .. comparison.Removed];
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
            CommandResult result = await sandbox.RunAsync("dotnet", arguments, [], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode is not 0)
            {
                return new TestGateResult(false, [Describe(arguments[0], result)]);
            }
        }

        string resultsName = ".test-gate-" + Guid.NewGuid().ToString("N");
        string hostResults = Path.Combine(workTree, resultsName);
        List<string> evidence = [];
        try
        {
            CommandResult test = await sandbox.RunAsync(
                "dotnet",
                ["test", "--no-restore", "--logger", "trx", "--results-directory", $"/work/{resultsName}"],
                [],
                cancellationToken).ConfigureAwait(false);
            if (test.ExitCode is not 0)
            {
                evidence.Add(Describe("test", test));
            }

            List<ReportedTest> tests = [];
            if (Directory.Exists(hostResults))
            {
                foreach (string file in Directory.EnumerateFiles(hostResults, "*.trx").Order(StringComparer.Ordinal))
                {
                    tests.AddRange(TestReport.Read(file));
                }
            }

            if (test.ExitCode is not 0 && tests.Count is 0)
            {
                return new TestGateResult(false, evidence);
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
            return new TestGateResult(false, evidence);
        }

        CommandResult integration = await sandbox.RunAsync(
            "dotnet",
            ["run", "--no-restore", "--project", "tests/Integration"],
            secretNames,
            cancellationToken).ConfigureAwait(false);
        return integration.ExitCode is 0
            ? new TestGateResult(true, [])
            : new TestGateResult(false, [Describe("run", integration)]);
    }

    private static string Describe(string subcommand, CommandResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"dotnet {subcommand} exited with the code {result.ExitCode}\nstandard output: {result.StandardOutput}\nstandard error: {result.StandardError}");
}
