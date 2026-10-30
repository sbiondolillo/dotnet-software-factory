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
    int ReviewBudgetCents = 300);
