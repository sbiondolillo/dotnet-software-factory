using System.Diagnostics;
using Xunit;

namespace Integration;

/// <summary>Proves the starter template's answers with the real <c>dotnet</c>: install, create, and every command on a copy.</summary>
public sealed class StarterTemplateIntegrationTests
{
    private const string ShortName = "factory-starter";

    private static readonly string[] LockFiles =
    [
        "src/App/packages.lock.json",
        "src/Core/packages.lock.json",
        "tests/Specs/packages.lock.json",
        "tests/Integration/packages.lock.json",
    ];

    private static string Starter()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "templates", "starter");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("templates/starter was not found above " + AppContext.BaseDirectory);
    }

    private static (int Code, string Out, string Err) Run(string directory, string home, IDictionary<string, string>? extra, params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["DOTNET_CLI_HOME"] = home;
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach ((string key, string value) in extra ?? new Dictionary<string, string>())
        {
            info.Environment[key] = value;
        }

        using Process process = Process.Start(info)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.GetAwaiter().GetResult());
    }

    private static string Normalised(string text) => string.Join('\n', text.Split('\n').Select(line => line.TrimEnd('\r', ' ')));

    /// <summary>Runs <paramref name="test"/> with a temp root holding a home and a copy directory, and removes the root.</summary>
    private static void WithRoot(Action<string, string, string> test)
    {
        string root = Directory.CreateTempSubdirectory("starter-it-").FullName;
        try
        {
            string home = Path.Combine(root, "home");
            string copy = Path.Combine(root, "copy");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(copy);
            test(root, home, copy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void InstallAndCreate(string root, string home, string copy)
    {
        Assert.Equal(0, Run(root, home, null, "new", "install", Starter()).Code);
        (int code, string output, string error) = Run(root, home, null, "new", ShortName, "-o", copy);
        Assert.True(code is 0, output + error);
    }

    private static void AssertSucceeds(string copy, string home, string? offlineMarker, params string[] arguments)
    {
        (int code, string output, string error) = Run(copy, home, null, arguments);
        Assert.True(code is 0, $"{string.Join(' ', arguments)} {offlineMarker}\n{output}\n{error}");
    }

    private static void AssertLockFilesEqualTheStarters(string copy)
    {
        foreach (string file in LockFiles)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(Starter(), file)), File.ReadAllBytes(Path.Combine(copy, file)));
        }
    }

    [Fact]
    public void InstallingTheFolderExitsWithTheCode0AndListsTheTemplate() => WithRoot((root, home, _) =>
    {
        (int code, string output, string error) = Run(root, home, null, "new", "install", Starter());
        Assert.True(code is 0, output + error);
        (int listCode, string list, _) = Run(root, home, null, "new", "list");
        Assert.Equal(0, listCode);
        Assert.Contains(list.Split('\n'), line => line.Contains("dotnet-software-factory starter", StringComparison.Ordinal) && line.Contains(ShortName, StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(home, ".templateengine")));
    });

    [Fact]
    public void InstallingTheSameFolderTwiceExitsWithTheCode106() => WithRoot((root, home, _) =>
    {
        Assert.Equal(0, Run(root, home, null, "new", "install", Starter()).Code);
        (int code, string output, string error) = Run(root, home, null, "new", "install", Starter());
        Assert.Equal(106, code);
        Assert.Contains($"{Starter()} is already installed.", Normalised(output + error), StringComparison.Ordinal);
    });

    [Fact]
    public void UninstallingTheFolderExitsWithTheCode0AndTheListOmitsTheTemplate() => WithRoot((root, home, _) =>
    {
        Assert.Equal(0, Run(root, home, null, "new", "install", Starter()).Code);
        (int code, string output, string error) = Run(root, home, null, "new", "uninstall", Starter());
        Assert.True(code is 0, output + error);
        string list = Run(root, home, null, "new", "list").Out;
        Assert.DoesNotContain(ShortName, list, StringComparison.Ordinal);
    });

    [Fact]
    public void CreatingACopyWritesEveryStarterFileByteEqual() => WithRoot((root, home, copy) =>
    {
        Assert.Equal(0, Run(root, home, null, "new", "install", Starter()).Code);
        (int code, string output, string error) = Run(root, home, null, "new", ShortName, "-o", copy);
        Assert.Equal(0, code);
        Assert.Contains("The template \"dotnet-software-factory starter\" was created successfully.", output + error, StringComparison.Ordinal);
        string[] expected = [.. Directory.EnumerateFiles(Starter(), "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(Starter(), file).Replace('\\', '/'))
            .Where(path => !path.StartsWith(".template.config/", StringComparison.Ordinal) && !path.Contains("bin/", StringComparison.Ordinal) && !path.Contains("obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
        string[] actual = [.. Directory.EnumerateFiles(copy, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(copy, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
        Assert.Equal(expected, actual);
        foreach (string path in expected)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(Starter(), path)), File.ReadAllBytes(Path.Combine(copy, path)));
        }
    });

    [Fact]
    public void CreatingACopyWithNoTemplateInstalledExitsWithTheCode103() => WithRoot((root, home, copy) =>
    {
        (int code, string output, string error) = Run(root, home, null, "new", ShortName, "-o", copy);
        Assert.Equal(103, code);
        Assert.Contains("No templates or subcommands found matching: 'factory-starter'.", output + error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(copy));
    });

    [Fact]
    public void ATemplateWithNoSourcesListDropsTheLockFiles() => WithRoot((root, home, copy) =>
    {
        string template = Path.Combine(root, "template");
        Directory.CreateDirectory(Path.Combine(template, ".template.config"));
        File.WriteAllText(
            Path.Combine(template, ".template.config", "template.json"),
            "{ \"identity\": \"Test.NoSources\", \"name\": \"no sources\", \"shortName\": \"no-sources\", \"tags\": { \"language\": \"C#\", \"type\": \"project\" } }");
        File.WriteAllText(Path.Combine(template, "packages.lock.json"), "{}");
        File.WriteAllText(Path.Combine(template, "kept.txt"), "kept");
        Assert.Equal(0, Run(root, home, null, "new", "install", template).Code);
        Assert.Equal(0, Run(root, home, null, "new", "no-sources", "-o", copy).Code);
        Assert.True(File.Exists(Path.Combine(copy, "kept.txt")));
        Assert.False(File.Exists(Path.Combine(copy, "packages.lock.json")));
    });

    [Fact]
    public void TheFourCommandsExitWithTheCode0AndLeaveTheLockFilesAsTheyWere() => WithRoot((root, home, copy) =>
    {
        InstallAndCreate(root, home, copy);
        AssertSucceeds(copy, home, null, "restore", "--locked-mode");
        AssertSucceeds(copy, home, null, "build", "--no-restore");
        AssertSucceeds(copy, home, null, "format", "--verify-no-changes", "--no-restore");
        (int code, string output, string error) = Run(copy, home, null, "test", "--no-restore");
        Assert.True(code is 0, output + error);
        Assert.Matches(@"Passed:\s+1\b", output);
        Assert.Matches(@"Failed:\s+0\b", output);
        Assert.Matches(@"Skipped:\s+0\b", output);
        AssertLockFilesEqualTheStarters(copy);
    });

    [Theory]
    [InlineData("Ada", "Hello, Ada!")]
    [InlineData("", "Hello, world!")]
    public void TheProgramPrintsTheGreeting(string argument, string expected) => WithRoot((root, home, copy) =>
    {
        InstallAndCreate(root, home, copy);
        AssertSucceeds(copy, home, null, "restore", "--locked-mode");
        string[] arguments = argument.Length is 0
            ? ["run", "--project", "src/App"]
            : ["run", "--project", "src/App", "--", argument];
        (int code, string output, string error) = Run(copy, home, null, arguments);
        Assert.True(code is 0, output + error);
        Assert.Equal(expected, output.Trim());
    });

    [Fact]
    public void TheProgramWithHelpExitsWithTheCode0AndWritesUsage() => WithRoot((root, home, copy) =>
    {
        InstallAndCreate(root, home, copy);
        AssertSucceeds(copy, home, null, "restore", "--locked-mode");
        (int code, string output, string error) = Run(copy, home, null, "run", "--project", "src/App", "--", "--help");
        Assert.True(code is 0, output + error);
        Assert.Contains("<name>", output, StringComparison.Ordinal);
        Assert.Contains("[default: world]", output, StringComparison.Ordinal);
    });

    [Fact]
    public void TheProgramWithTwoArgumentsExitsWithTheCode1() => WithRoot((root, home, copy) =>
    {
        InstallAndCreate(root, home, copy);
        AssertSucceeds(copy, home, null, "restore", "--locked-mode");
        (int code, _, string error) = Run(copy, home, null, "run", "--project", "src/App", "--", "Ada", "Lovelace");
        Assert.Equal(1, code);
        Assert.Contains("Unrecognized command or argument 'Lovelace'.", error, StringComparison.Ordinal);
    });

    [Fact]
    public void TheCopysIntegrationSuiteRunsWithNoTest() => WithRoot((root, home, copy) =>
    {
        InstallAndCreate(root, home, copy);
        AssertSucceeds(copy, home, null, "restore", "--locked-mode");
        (int code, string output, string error) = Run(copy, home, null, "run", "--no-restore", "--project", "tests/Integration");
        Assert.True(code is 0, output + error);
        Assert.Contains("Integration  Total: 0", output, StringComparison.Ordinal);
    });

    [Fact]
    public void RestoreWithTheLockFilesRemovedAndAWarmCacheWorksOfflineAndWritesTheSameFiles() => WithRoot((root, home, copy) =>
    {
        InstallAndCreate(root, home, copy);
        AssertSucceeds(copy, home, null, "restore", "--locked-mode");
        foreach (string file in LockFiles)
        {
            File.Delete(Path.Combine(copy, file));
        }

        var offline = new Dictionary<string, string>
        {
            ["HTTP_PROXY"] = "http://127.0.0.1:9",
            ["HTTPS_PROXY"] = "http://127.0.0.1:9",
            ["http_proxy"] = "http://127.0.0.1:9",
            ["https_proxy"] = "http://127.0.0.1:9",
        };
        (int code, string output, string error) = Run(copy, home, offline, "restore", "--source", Path.Combine(home, ".nuget", "packages"));
        Assert.True(code is 0, output + error);
        AssertLockFilesEqualTheStarters(copy);
    });
}
