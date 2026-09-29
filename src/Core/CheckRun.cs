namespace Core;

/// <summary>A check run as GitHub writes it; a null conclusion reads as the empty string.</summary>
public sealed record CheckRun(string Name, string Status, string Conclusion);
