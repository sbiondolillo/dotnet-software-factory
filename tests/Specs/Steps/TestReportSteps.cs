using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class TestReportSteps
{
    private const string Header = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">\n";

    private readonly List<(string Id, string Cls, string Method, string Name, string Outcome, string? Message)> _tests = [];
    private string? _raw;
    private IReadOnlyList<ReportedTest> _results = [];
    private TestReportException? _exception;
    private string _path = "";

    [Given("a TRX file holding the passed test {string} of the class {string}")]
    public void GivenPassed(string method, string cls) => _tests.Add((Guid.NewGuid().ToString(), cls, method, method, "Passed", null));

    [Given("a TRX file holding a failed test with the failure message lines {string}, {string} and {string} and a stack trace")]
    public void GivenFailed(string a, string b, string c) =>
        _tests.Add((Guid.NewGuid().ToString(), "Specs.Unit.GreeterTests", "Fails", "Fails", "Failed", $"{a}\n{b}\n{c}"));

    [Given("a TRX file holding the test {string} of the class {string} with the outcome {string}")]
    public void GivenOutcome(string method, string cls, string outcome) =>
        _tests.Add((Guid.NewGuid().ToString(), cls, method, method, outcome, outcome == "Passed" ? null : "held back"));

    [Given("a TRX file holding a test with the outcome {string}")]
    public void GivenAnOutcome(string outcome) =>
        _tests.Add((Guid.NewGuid().ToString(), "Specs.Unit.GreeterTests", "Some", "Some", outcome, "took too long"));

    [Given("a TRX file holding two results of the method {string} of the class {string}, one passed and one failed, named with their arguments")]
    public void GivenTwo(string method, string cls)
    {
        _tests.Add((Guid.NewGuid().ToString(), cls, method, $"{method}(who: \"Ada\", __pickleIndex: \"0\", exampleTags: [])", "Passed", null));
        _tests.Add((Guid.NewGuid().ToString(), cls, method, $"{method}(who: \"Bob\", __pickleIndex: \"1\", exampleTags: [])", "Failed", "wrong"));
    }

    [Given("a TRX file holding a test run with no results")]
    public void GivenNone() => _tests.Clear();

    [Given("a TRX file holding the one line {string}")]
    public void GivenLine(string line) => _raw = line;

    [When("TestReport reads the file")]
    public void WhenReads()
    {
        _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".trx");
        File.WriteAllText(_path, _raw ?? Build());
        try
        {
            _results = TestReport.Read(_path);
        }
        catch (TestReportException e)
        {
            _exception = e;
        }
        finally
        {
            File.Delete(_path);
        }
    }

    [Then("TestReport returns one test")]
    public void ThenOne() => Assert.Single(_results);

    [Then("TestReport returns two tests")]
    public void ThenTwo() => Assert.Equal(2, _results.Count);

    [Then("TestReport returns an empty list")]
    public void ThenEmpty() => Assert.Empty(_results);

    [Then("the test has the class {string} and the method {string}")]
    public void ThenClassMethod(string cls, string method)
    {
        Assert.Equal(cls, _results[0].ClassName);
        Assert.Equal(method, _results[0].Method);
    }

    [Then("the test has the outcome {word}")]
    public void ThenOutcome(string outcome) => Assert.Equal(Enum.Parse<TestOutcome>(outcome, true), _results[0].Outcome);

    [Then("the test has an empty message")]
    public void ThenEmptyMessage() => Assert.Equal("", _results[0].Message);

    [Then("the test message is the three lines {string}, {string} and {string}")]
    public void ThenMessage(string a, string b, string c) => Assert.Equal($"{a}\n{b}\n{c}", _results[0].Message);

    [Then("the methods of the tests are {string} and {string}")]
    public void ThenMethods(string a, string b) => Assert.Equal([a, b], _results.Select(r => r.Method));

    [Then("one test has the outcome passed and one test has the outcome failed")]
    public void ThenPassedFailed()
    {
        Assert.Single(_results, r => r.Outcome is TestOutcome.Passed);
        Assert.Single(_results, r => r.Outcome is TestOutcome.Failed);
    }

    [Then("TestReport throws a TestReportException")]
    public void ThenThrows() => Assert.NotNull(_exception);

    [Then("the exception message names the path of the file")]
    public void ThenPath()
    {
        Assert.NotNull(_exception);
        Assert.Equal(_path, _exception.Path);
        Assert.StartsWith(_path, _exception.Message, StringComparison.Ordinal);
    }

    [Then("the exception message holds the message of the parser")]
    public void ThenParser()
    {
        Assert.NotNull(_exception);
        var parser = Assert.ThrowsAny<System.Xml.XmlException>(() => System.Xml.Linq.XDocument.Parse(_raw!));
        Assert.Contains(parser.Message, _exception.Message, StringComparison.Ordinal);
    }

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s);

    private string Build()
    {
        var defs = string.Concat(_tests.Select(t =>
            $"<UnitTest name=\"{Esc(t.Name)}\" id=\"{t.Id}\"><TestMethod className=\"{Esc(t.Cls)}\" name=\"{Esc(t.Method)}\" /></UnitTest>\n"));
        var results = string.Concat(_tests.Select(t =>
        {
            var err = t.Message is null ? "" :
                $"<Output><ErrorInfo><Message>{Esc(t.Message)}</Message><StackTrace>at X.Y()</StackTrace></ErrorInfo></Output>";
            return $"<UnitTestResult testId=\"{t.Id}\" testName=\"{Esc(t.Name)}\" outcome=\"{t.Outcome}\">{err}</UnitTestResult>\n";
        }));
        return $"{Header}<Results>\n{results}</Results>\n<TestDefinitions>\n{defs}</TestDefinitions>\n</TestRun>\n";
    }
}
