using Core;
using Xunit;

namespace Specs.Unit;

public sealed class PinTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pintests-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void SourceFileIsNotPinned()
    {
        Write("global.json", "{}");
        Write("src/Core/Greeter.cs", "a");
        var pin = Pin.Take(_root);
        Assert.Equal(["global.json"], pin.Hashes.Keys);
        Write("src/Core/Greeter.cs", "b");
        var diff = pin.Compare(_root);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void ChangedAndRemovedAreReported()
    {
        Write("global.json", "{}");
        Write("nuget.config", "x");
        var pin = Pin.Take(_root);
        Write("global.json", "[]");
        File.Delete(Path.Combine(_root, "nuget.config"));
        var diff = pin.Compare(_root);
        Assert.Equal(["global.json"], diff.Changed);
        Assert.Equal(["nuget.config"], diff.Removed);
        Assert.Empty(diff.Added);
    }

    [Fact]
    public void UnchangedDirectoryHasNoDifferences()
    {
        Write("specs/features/a.feature", "x");
        var diff = Pin.Take(_root).Compare(_root);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Theory]
    [InlineData(".editorconfig")]
    [InlineData(".globalconfig")]
    [InlineData("global.json")]
    [InlineData("nuget.config")]
    [InlineData("NuGet.config")]
    [InlineData("NuGet.Config")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Build.rsp")]
    [InlineData("reqnroll.json")]
    public void EveryNameIsRecognisedAtDepth(string name)
    {
        var pin = Pin.Take(_root);
        Write(name, "x");
        Write("src/Core/" + name, "x");
        Assert.Equal([name, "src/Core/" + name], pin.Compare(_root).Added);
    }

    [Theory]
    [InlineData("tests/bin/reqnroll.json")]
    [InlineData("obj/global.json")]
    [InlineData("specs/features/bin/a.feature")]
    [InlineData("specs/features/x/obj/a.md")]
    [InlineData("Global.json")]
    [InlineData("global.json.bak")]
    public void SkippedAndNearMissFilesAreNotPinned(string path)
    {
        var pin = Pin.Take(_root);
        Write(path, "x");
        Assert.Empty(pin.Compare(_root).Added);
        Assert.Empty(Pin.Take(_root).Hashes);
    }

    [Fact]
    public void AnyFileUnderSpecsFeaturesIsAdded()
    {
        var pin = Pin.Take(_root);
        Write("specs/features/b/notes", "n");
        Assert.Equal(["specs/features/b/notes"], pin.Compare(_root).Added);
    }

    [Fact]
    public void ARefactorPinReportsEachChangeUnderTheThreeDirectoriesAndIgnoresBinAndObj()
    {
        string[] roots = ["specs/features", "tests/Specs", "tests/Integration"];
        foreach (string root in roots)
        {
            Write($"{root}/kept.txt", "a");
            Write($"{root}/changed.cs", "a");
            Write($"{root}/removed.cs", "a");
            Write($"{root}/bin/ignored.dll", "a");
            Write($"{root}/obj/ignored.dll", "a");
        }

        Write("src/Core/Greeter.cs", "a");
        var pin = Pin.TakeRefactor(_root);
        foreach (string root in roots)
        {
            Write($"{root}/changed.cs", "b");
            File.Delete(Path.Combine(_root, $"{root}/removed.cs"));
            Write($"{root}/added.cs", "b");
            Write($"{root}/bin/ignored.dll", "b");
            Write($"{root}/obj/new.dll", "b");
        }

        Write("src/Core/Greeter.cs", "b");
        var diff = pin.Compare(_root);
        Assert.Equal([.. roots.Order(StringComparer.Ordinal).Select(r => $"{r}/changed.cs")], diff.Changed);
        Assert.Equal([.. roots.Order(StringComparer.Ordinal).Select(r => $"{r}/added.cs")], diff.Added);
        Assert.Equal([.. roots.Order(StringComparer.Ordinal).Select(r => $"{r}/removed.cs")], diff.Removed);
    }
}
