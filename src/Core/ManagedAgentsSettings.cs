using System.Globalization;

namespace Core;

/// <summary>What the <c>--agent managed</c> path reads from the environment, and the budget of each session in cents.</summary>
public sealed record ManagedAgentsSettings(
    string ApiKey,
    string AgentId,
    string EnvironmentId,
    string AppId,
    string InstallationId,
    string PrivateKeyPem,
    int BuilderBudgetCents = 1500,
    int ReviewBudgetCents = 300)
{
    /// <summary>The ids and the budgets alone, so no text of this record holds the key or the private key.</summary>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"ManagedAgentsSettings {{ AgentId = {AgentId}, EnvironmentId = {EnvironmentId}, AppId = {AppId}, InstallationId = {InstallationId}, BuilderBudgetCents = {BuilderBudgetCents}, ReviewBudgetCents = {ReviewBudgetCents} }}");
}
