using System.Globalization;

namespace Core;

/// <summary>The outcome of a <see cref="TestGate"/> run.</summary>
/// <param name="Passed">True when every check passed.</param>
/// <param name="Evidence">The texts that say why the gate failed; empty when it passed.</param>
public sealed record TestGateResult(bool Passed, IReadOnlyList<string> Evidence);

/// <summary>Runs the CI commands of the build job in a <see cref="Sandbox"/> and checks that every pinned scenario passed.</summary>
public sealed class TestGate(Sandbox sandbox)
{
    private const string ContainerWorkTree = "/work";

    /// <summary>Runs the gate on <paramref name="workTree"/>; the integration command alone holds <paramref name="secretNames"/>.</summary>
    public async Task<TestGateResult> RunAsync(
        string workTree,
        Pin pin,
        IReadOnlyList<string> secretNames,
        CancellationToken cancellationToken = default)
    {
        PinComparison comparison = pin.Compare(workTree);
        List<string> paths = [.. comparison.Changed, .. comparison.Added, .. comparison.Removed];
        if (paths.Count > 0)
        {
            return new TestGateResult(false, paths);
        }

        (string Program, string[] Arguments)[] first =
        [
            ("dotnet", ["restore", "--locked-mode"]),
            ("dotnet", ["build", "--no-restore"]),
            ("dotnet", ["format", "--verify-no-changes", "--no-restore"]),
        ];
        foreach ((string program, string[] arguments) in first)
        {
            CommandResult result = await sandbox.RunAsync(program, arguments, [], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode is not 0)
            {
                return new TestGateResult(false, [Describe(program, arguments[0], result)]);
            }
        }

        string runName = "gate-" + Guid.NewGuid().ToString("N");
        string hostDirectory = Path.Combine(workTree, "TestResults", runName);
        try
        {
            CommandResult test = await sandbox.RunAsync(
                "dotnet",
                [
                    "test",
                    "--no-restore",
                    "--logger", "trx;LogFileName=" + runName + ".trx",
                    "--results-directory", $"{ContainerWorkTree}/TestResults/{runName}",
                ],
                [],
                cancellationToken).ConfigureAwait(false);
            List<string> evidence = CheckTests(workTree, hostDirectory, test);
            if (evidence.Count > 0)
            {
                return new TestGateResult(false, evidence);
            }
        }
        finally
        {
            if (Directory.Exists(hostDirectory))
            {
                Directory.Delete(hostDirectory, recursive: true);
            }
        }

        CommandResult integration = await sandbox.RunAsync(
            "dotnet",
            ["run", "--no-restore", "--project", "tests/Integration"],
            secretNames,
            cancellationToken).ConfigureAwait(false);
        return integration.ExitCode is 0
            ? new TestGateResult(true, [])
            : new TestGateResult(false, [Describe("dotnet", "run", integration)]);
    }

    private static List<string> CheckTests(string workTree, string hostDirectory, CommandResult test)
    {
        string? trx = Directory.Exists(hostDirectory)
            ? Directory.EnumerateFiles(hostDirectory, "*.trx").FirstOrDefault()
            : null;
        if (trx is null)
        {
            return test.ExitCode is 0 ? MissingScenarios(workTree, []) : [Describe("dotnet", "test", test)];
        }

        IReadOnlyList<ReportedTest> tests = TestReport.Read(trx);
        List<string> evidence = [];
        foreach (ReportedTest failed in tests.Where(t => t.Outcome is TestOutcome.Failed))
        {
            evidence.Add($"failed test: {failed.ClassName}\n{failed.Method}\n{failed.Message}");
        }

        foreach (ReportedTest skipped in tests.Where(t => t.Outcome is TestOutcome.Skipped))
        {
            evidence.Add($"skipped test: {skipped.ClassName}\n{skipped.Method}");
        }

        if (test.ExitCode is not 0 && evidence.Count is 0)
        {
            evidence.Add(Describe("dotnet", "test", test));
        }

        evidence.AddRange(MissingScenarios(workTree, tests));
        return evidence;
    }

    private static List<string> MissingScenarios(string workTree, IReadOnlyList<ReportedTest> tests)
    {
        List<string> evidence = [];
        string features = Path.Combine(workTree, "specs", "features");
        if (!Directory.Exists(features))
        {
            return evidence;
        }

        foreach ((string relativePath, IReadOnlyList<string> titles) in FeatureFiles.ReadScenarioTitlesFromDirectory(features))
        {
            foreach (string title in titles)
            {
                string name = FeatureFiles.ComputeTestName(title);
                if (!tests.Any(t => t.Method == name))
                {
                    evidence.Add($"scenario without a test: specs/features/{relativePath}\n{title}\n{name}");
                }
            }
        }

        return evidence;
    }

    private static string Describe(string program, string subcommand, CommandResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{program} {subcommand} exited with the code {result.ExitCode}\nstandard output:\n{result.StandardOutput}\nstandard error:\n{result.StandardError}");
}
