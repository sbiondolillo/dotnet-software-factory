namespace Specs.Steps;

/// <summary>The review prompt of a scenario, shared by the step classes that bind the step about it.</summary>
public sealed class ReviewPromptState
{
    public string? Text { get; set; }

    /// <summary>Reads the review prompt of a feedback run, once the run has happened.</summary>
    public Func<string?>? Lookup { get; set; }
}
