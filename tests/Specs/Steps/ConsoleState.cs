namespace Specs.Steps;

/// <summary>What the console program's scenarios share with the step classes that bind the same step text.</summary>
public sealed class ConsoleState
{
    public int? ExitCode { get; set; }

    public bool NoReviews { get; set; }

    public bool NoCheckRuns { get; set; }

    public (string Name, string Status)? FirstCheckRun { get; set; }
}
