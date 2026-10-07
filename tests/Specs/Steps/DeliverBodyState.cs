using Core;
using Specs.Unit;

namespace Specs.Steps;

/// <summary>The whole run that the scenarios of the pull request body drive, shared by the step classes that bind its steps.</summary>
public sealed class DeliverBodyState : IDisposable
{
    private JobSummaryHarness? _harness;

    /// <summary>The harness of the run; reading it starts the run's setup, so a scenario of the plain Deliver leaves it unread.</summary>
    public JobSummaryHarness Harness => _harness ??= new JobSummaryHarness();

    /// <summary>True when a step of the body feature set the run up.</summary>
    public bool InUse => _harness is not null;

    public int ExpectedRedos { get; set; }

    public FactoryRunResult? Result { get; set; }

    public string Body { get; set; } = "";

    public void Dispose() => _harness?.Dispose();
}
