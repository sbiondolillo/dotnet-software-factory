namespace Core;

/// <summary>The four values of the JSON object <c>claude</c> writes.</summary>
public sealed record ClaudeCodeResult(string Result, int NumTurns, decimal CostUsd, bool IsError);
