namespace Specs.Steps;

/// <summary>The exception the last action step raised, shared across step classes so one "the exception message
/// contains" step serves every feature that raises an exception.</summary>
public sealed class ExceptionState
{
    public Exception? Exception { get; set; }
}
