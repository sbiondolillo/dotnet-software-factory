using System.Globalization;

namespace Core;

/// <summary>The verdict of a <see cref="TestGate"/> run.</summary>
/// <param name="Passed">True when every check held.</param>
/// <param name="Evidence">One text for each failed check; empty when the gate passed.</param>
public sealed record TestGateResult(bool Passed, IReadOnlyList<string> Evidence);

/// <summary>Runs the restore, build, format check, test and integration commands of CI in a <see cref="Sandbox"/> and judges the result.</summary>
public sealed class TestGate(Sandbox sandbox)
{
    private const string FeaturesDirectory = "specs/features";

    /// <summary>Runs the gate over <paramref name="workTree"/>, the directory that <c>Sandbox</c> holds.
    /// A pin change runs no command; a failed command or failed TRX check ends the gate.</summary>
    public async Task<TestGateResult> RunAsync(
        string workTree,
        Pin pin,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
    {
        PinComparison comparison = pin.Compare(workTree);
        List<string> pinEvidence = [.. comparison.Changed, .. comparison.Added, .. comparison.Removed];
        if (pinEvidence.Count > 0)
        {
            return new TestGateResult(false, pinEvidence);
        }

        string[][] commands =
        [
            ["restore", "--locked-mode"],
            ["build", "--no-restore"],
            ["format", "--verify-no-changes", "--no-restore"],
        ];
        foreach (string[] arguments in commands)
        {
            CommandResult result = await sandbox.RunAsync("dotnet", arguments, [], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode is not 0)
            {
                return new TestGateResult(false, [Describe(arguments[0], result)]);
            }
        }

        List<string> evidence = await RunTestsAsync(workTree, cancellationToken).ConfigureAwait(false);
        if (evidence.Count > 0)
        {
            return new TestGateResult(false, evidence);
        }

        CommandResult integration = await sandbox.RunAsync(
            "dotnet",
            ["run", "--no-restore", "--project", "tests/Integration"],
            names,
            cancellationToken).ConfigureAwait(false);
        return integration.ExitCode is 0
            ? new TestGateResult(true, [])
            : new TestGateResult(false, [Describe("run", integration)]);
    }

    private static string Describe(string subcommand, CommandResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"dotnet {subcommand} exited with the code {result.ExitCode}\nstandard output: {result.StandardOutput}\nstandard error: {result.StandardError}");

    private async Task<List<string>> RunTestsAsync(string workTree, CancellationToken cancellationToken)
    {
        string name = "test-gate-" + Guid.NewGuid().ToString("N");
        string hostDirectory = Path.Combine(workTree, "TestResults", name);
        Directory.CreateDirectory(hostDirectory);
        try
        {
            CommandResult test = await sandbox.RunAsync(
                "dotnet",
                ["test", "--no-restore", "--logger", "trx;LogFileName=gate.trx", "--results-directory", $"/work/TestResults/{name}"],
                [],
                cancellationToken).ConfigureAwait(false);
            List<string> evidence = [];
            if (test.ExitCode is not 0)
            {
                evidence.Add(Describe("test", test));
            }

            string trx = Path.Combine(hostDirectory, "gate.trx");
            if (!File.Exists(trx))
            {
                if (test.ExitCode is 0)
                {
                    evidence.Add("dotnet test exited with the code 0 and wrote no TRX file");
                }

                return evidence;
            }

            IReadOnlyList<ReportedTest> tests;
            try
            {
                tests = TestReport.Read(trx);
            }
            catch (TestReportException exception)
            {
                evidence.Add($"dotnet test wrote a TRX file that cannot be read: {exception.Message}");
                return evidence;
            }

            AddTestEvidence(workTree, tests, evidence);
            return evidence;
        }
        finally
        {
            Directory.Delete(hostDirectory, recursive: true);
        }
    }

    private static void AddTestEvidence(string workTree, IReadOnlyList<ReportedTest> tests, List<string> evidence)
    {
        foreach (ReportedTest test in tests)
        {
            if (test.Outcome is TestOutcome.Failed)
            {
                evidence.Add($"failed test: class {test.ClassName}, method {test.Method}" + (test.Message is null ? "" : $", message {test.Message}"));
            }
            else if (test.Outcome is TestOutcome.Skipped)
            {
                evidence.Add($"skipped test: class {test.ClassName}, method {test.Method}");
            }
        }

        string features = Path.Combine(workTree, "specs", "features");
        if (!Directory.Exists(features))
        {
            return;
        }

        HashSet<string> methods = tests.Select(test => test.Method).ToHashSet(StringComparer.Ordinal);
        foreach ((string relativePath, IReadOnlyList<string> titles) in FeatureFiles.ReadScenarioTitlesFromDirectory(features))
        {
            foreach (string title in titles)
            {
                string testName = FeatureFiles.ComputeTestName(title);
                if (!methods.Contains(testName))
                {
                    evidence.Add($"missing scenario: file {FeaturesDirectory}/{relativePath}, title {title}, test name {testName}");
                }
            }
        }
    }
}
