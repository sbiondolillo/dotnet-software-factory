using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the <c>claude</c> answers <c>tests/Specs</c> canned for <see cref="ClaudeCodeAgent"/> still hold against the real <c>claude</c>.</summary>
public sealed class ClaudeCodeAgentIntegrationTests : IDisposable
{
    private const string Tag = "factory-sandbox:integration";
    private const string Model = "claude-haiku-4-5-20251001";
    private static readonly CommandRunner Runner = new();
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-claude-tree-");

    public void Dispose() => _tree.Delete(recursive: true);

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Solution.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution.slnx not found");
    }

    private Task<Sandbox> StartAsync() =>
        Sandbox.StartAsync(Runner, Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);

    private static Task<CommandResult> ClaudeAsync(Sandbox sandbox, params string[] arguments) =>
        sandbox.RunAsync("claude", arguments, [], TestContext.Current.CancellationToken);

    private static string[] Args(string prompt, string model, string turns) =>
        ["-p", prompt, "--model", model, "--max-turns", turns, "--output-format", "json", "--dangerously-skip-permissions"];

    [Fact]
    public async Task AnOkAnswerHoldsItsFourValues()
    {
        await using Sandbox sandbox = await StartAsync();

        CommandResult result = await ClaudeAsync(sandbox, Args("Reply with the single word ok and nothing else.", Model, "1"));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("is_error").GetBoolean());
        Assert.Equal(1, root.GetProperty("num_turns").GetInt32());
        Assert.Equal("ok", root.GetProperty("result").GetString());
        Assert.True(root.GetProperty("total_cost_usd").GetDecimal() > 0);
    }

    [Fact]
    public async Task TheAgentRunsARealPrompt()
    {
        await using Sandbox sandbox = await StartAsync();

        ClaudeCodeResult result = await new ClaudeCodeAgent(sandbox).RunAsync(
            "Reply with the single word ok and nothing else.", Model, 1, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("ok", result.Result);
    }

    private const string WritePrompt =
        "Write the text {\"verdict\":\"approved\"} to the file .factory/answer.json, creating the directory if needed, and then reply done.";

    private string AnswerPath => Path.Combine(_tree.FullName, ".factory", "answer.json");

    private static JsonDocument Parse(CommandResult result)
    {
        Assert.NotEmpty(result.StandardOutput);
        return JsonDocument.Parse(result.StandardOutput);
    }

    private void AssertCallerOwnsAnswer()
    {
        Assert.True(File.Exists(AnswerPath));
        Assert.Contains("approved", File.ReadAllText(AnswerPath), StringComparison.Ordinal);
        File.AppendAllText(AnswerPath, " ");
        using JsonDocument document = ClaudeCodeAgent.ReadJsonFile(AnswerPath);
        Assert.Equal("approved", document.RootElement.GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task AFileWritingPromptWithSixTurnsWritesTheFileTheCallerOwns()
    {
        await using Sandbox sandbox = await StartAsync();

        CommandResult result = await ClaudeAsync(sandbox, Args(WritePrompt, Model, "6"));

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = Parse(result);
        Assert.False(document.RootElement.GetProperty("is_error").GetBoolean());
        AssertCallerOwnsAnswer();
    }

    [Fact]
    public async Task AFileWritingPromptWithOneTurnEndsInErrorMaxTurns()
    {
        await using Sandbox sandbox = await StartAsync();

        CommandResult result = await ClaudeAsync(sandbox, Args(WritePrompt, Model, "1"));

        Assert.Equal(1, result.ExitCode);
        using JsonDocument document = Parse(result);
        Assert.True(document.RootElement.GetProperty("is_error").GetBoolean());
        Assert.Equal("error_max_turns", document.RootElement.GetProperty("subtype").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("num_turns").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task AnUnknownModelAnswersWith404()
    {
        await using Sandbox sandbox = await StartAsync();

        CommandResult result = await ClaudeAsync(sandbox, Args("Say hi.", "no-such-model", "1"));

        Assert.Equal(1, result.ExitCode);
        using JsonDocument document = Parse(result);
        Assert.True(document.RootElement.GetProperty("is_error").GetBoolean());
        Assert.Equal(404, document.RootElement.GetProperty("api_error_status").GetInt32());
        Assert.Contains("no-such-model", document.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public async Task AnInvalidKeyAnswersWith401AndCostZero()
    {
        await using Sandbox sandbox = await Sandbox.StartAsync(
            new KeyRunner("sk-ant-invalid-key"), Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);

        CommandResult result = await ClaudeAsync(sandbox, Args("Say hi.", Model, "1"));

        Assert.Equal(1, result.ExitCode);
        using JsonDocument document = Parse(result);
        Assert.True(document.RootElement.GetProperty("is_error").GetBoolean());
        Assert.Equal(401, document.RootElement.GetProperty("api_error_status").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("total_cost_usd").GetDecimal());
        Assert.Contains("invalid", document.RootElement.GetProperty("result").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoKeyAnswersNotLoggedInAndCostZero()
    {
        await using Sandbox sandbox = await Sandbox.StartAsync(
            new KeyRunner(""), Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);

        CommandResult result = await ClaudeAsync(sandbox, Args("Say hi.", Model, "1"));

        Assert.Equal(1, result.ExitCode);
        using JsonDocument document = Parse(result);
        Assert.True(document.RootElement.GetProperty("is_error").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("total_cost_usd").GetDecimal());
        Assert.Contains("logged in", document.RootElement.GetProperty("result").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoPromptExitsWithOneAndNoOutput()
    {
        await using Sandbox sandbox = await StartAsync();

        CommandResult result = await ClaudeAsync(sandbox, "-p");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.True(
            result.StandardError.Contains("stdin", StringComparison.OrdinalIgnoreCase)
            || result.StandardError.Contains("prompt", StringComparison.OrdinalIgnoreCase),
            result.StandardError);
    }

    [Fact]
    public async Task APromptOf131071BytesAnswers()
    {
        await using Sandbox sandbox = await StartAsync();

        CommandResult result = await ClaudeAsync(sandbox, Args(new string('a', 131071), Model, "1"));

        using JsonDocument document = Parse(result);
        Assert.True(document.RootElement.TryGetProperty("is_error", out _));
    }

    [Fact]
    public async Task AnOversizedPromptFailsTheDockerStart()
    {
        await using Sandbox sandbox = await StartAsync();

        CommandStartException exception = await Assert.ThrowsAsync<CommandStartException>(
            () => ClaudeAsync(sandbox, Args(new string('a', 131072), Model, "1")));

        Assert.Contains("Argument list too long", exception.Message);
    }

    [Fact]
    public async Task NoOutputFormatPrintsPlainText()
    {
        await using Sandbox sandbox = await StartAsync();

        CommandResult result = await ClaudeAsync(
            sandbox, "-p", "Reply with the single word ok and nothing else.", "--model", Model, "--dangerously-skip-permissions");

        Assert.Equal("ok\n", result.StandardOutput);
    }

    private sealed class KeyRunner(string key) : CommandRunner
    {
        public override Task<CommandResult> RunAsync(CommandSpec spec, CancellationToken cancellationToken = default) =>
            base.RunAsync(
                new CommandSpec
                {
                    Command = spec.Command,
                    Arguments = spec.Arguments,
                    WorkingDirectory = spec.WorkingDirectory,
                    EnvironmentVariables = new Dictionary<string, string>(spec.EnvironmentVariables) { ["ANTHROPIC_API_KEY"] = key },
                    Timeout = spec.Timeout,
                },
                cancellationToken);
    }
}
