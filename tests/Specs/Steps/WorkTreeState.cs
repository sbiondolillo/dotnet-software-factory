namespace Specs.Steps;

/// <summary>The work tree of a scenario, a new directory shared by the step classes that run Git or a Sandbox over it.</summary>
public sealed class WorkTreeState : IDisposable
{
    private DirectoryInfo? _directory;

    /// <summary>The full path of the work tree; reading it creates the directory.</summary>
    public string Path => (_directory ??= Directory.CreateTempSubdirectory("factory-work-tree-")).FullName;

    /// <summary>True when a step of the scenario created the work tree.</summary>
    public bool InUse => _directory is not null;

    public void Dispose() => _directory?.Delete(recursive: true);
}
