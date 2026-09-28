using System.Security;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class TestReportSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("test-report-").FullName;
    private readonly List<(string Class, string Method, string Outcome, string? Message)> _results = [];
    private string? _rawContent;
    private string _path = "";
    private IReadOnlyList<ReportedTest> _tests = [];

    [Given("a TRX file with a passed test {string} of the class {string}")]
    public void GivenAPassedTest(string method, string className) => _results.Add((className, method, "Passed", null));

    [Given("a TRX file with a failed test {string} of the class {string} with a stack trace")]
    public void GivenAFailedTest(string method, string className) => _results.Add((className, method, "Failed", ""));

    [Given("its failure message is the three lines {string}, {string} and {string}")]
    public void GivenItsFailureMessage(string one, string two, string three)
    {
        var last = _results[^1];
        _results[^1] = (last.Class, last.Method, last.Outcome, $"{one}\n{two}\n{three}");
    }

    [Given("a TRX file with a test {string} of the class {string} with the outcome {string}")]
    public void GivenATestWithOutcome(string method, string className, string outcome)
        => _results.Add((className, method, outcome, null));

    [Given("a TRX file with two results of the method {string} of the class {string}, one for each example row of its outline")]
    public void GivenTwoResults(string method, string className)
    {
        _results.Add((className, method, "Passed", null));
        _results.Add((className, method, "Failed", "row failed"));
    }

    [Given("one of the results passed and the other failed")]
    public void GivenOnePassedOneFailed()
    {
        Assert.Equal(["Passed", "Failed"], _results.Select(result => result.Outcome));
    }

    [Given("a TRX file with a test run with no results")]
    public void GivenNoResults() => _rawContent = Wrap("", "");

    [Given("a TRX file that holds the one line {string}")]
    public void GivenOneLine(string line) => _rawContent = line + "\n";

    [When("TestReport reads the TRX file")]
    public void WhenTestReportReads()
    {
        _path = Path.Combine(_root, "results.trx");
        File.WriteAllText(_path, _rawContent ?? BuildTrx());
        try
        {
            _tests = TestReport.Read(_path);
        }
        catch (TestReportException exception)
        {
            exceptionState.Exception = exception;
        }
    }

    [Then("TestReport returns one test")]
    public void ThenOneTest() => Assert.Single(_tests);

    [Then("TestReport returns two tests")]
    public void ThenTwoTests() => Assert.Equal(2, _tests.Count);

    [Then("TestReport returns an empty list")]
    public void ThenEmpty() => Assert.Empty(_tests);

    [Then("the test has the class {string} and the method {string}")]
    public void ThenClassAndMethod(string className, string method)
    {
        ReportedTest test = Assert.Single(_tests);
        Assert.Equal(className, test.ClassName);
        Assert.Equal(method, test.Method);
    }

    [Then("the test has the outcome passed")]
    public void ThenPassed() => Assert.Equal(TestOutcome.Passed, Assert.Single(_tests).Outcome);

    [Then("the test has the outcome failed")]
    public void ThenFailed() => Assert.Equal(TestOutcome.Failed, Assert.Single(_tests).Outcome);

    [Then("the test has the outcome skipped")]
    public void ThenSkipped() => Assert.Equal(TestOutcome.Skipped, Assert.Single(_tests).Outcome);

    [Then("the test message is the three lines {string}, {string} and {string}")]
    public void ThenMessage(string one, string two, string three)
        => Assert.Equal($"{one}\n{two}\n{three}", Assert.Single(_tests).Message);

    [Then("each test has the method {string}")]
    public void ThenEachMethod(string method) => Assert.All(_tests, test => Assert.Equal(method, test.Method));

    [Then("one test has the outcome passed and one test has the outcome failed")]
    public void ThenOneEach()
        => Assert.Equal([TestOutcome.Passed, TestOutcome.Failed], _tests.Select(test => test.Outcome).Order());

    [Then("TestReport throws a TestReportException")]
    public void ThenThrows() => Assert.IsType<TestReportException>(exceptionState.Exception);

    [Then("the exception message names the path of the file")]
    public void ThenNamesPath()
        => Assert.Contains(_path, Assert.IsType<TestReportException>(exceptionState.Exception).Message);

    [Then("the exception message holds the message of the parser")]
    public void ThenHoldsParserMessage()
    {
        string expected = "";
        try
        {
            System.Xml.Linq.XDocument.Load(_path);
        }
        catch (System.Xml.XmlException exception)
        {
            expected = exception.Message;
        }

        Assert.NotEqual("", expected);
        Assert.Contains(expected, Assert.IsType<TestReportException>(exceptionState.Exception).Message);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string BuildTrx() => TrxBuilder.Build(_results);

    private static string Wrap(string definitions, string results) => TrxBuilder.Wrap(definitions, results);
}

/// <summary>Writes TRX text in the shape the TRX logger writes.</summary>
internal static class TrxBuilder
{
    public static string Wrap(string definitions, string results) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
        + "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">"
        + $"<Results>{results}</Results><TestDefinitions>{definitions}</TestDefinitions></TestRun>";

    public static string Build(IEnumerable<(string Class, string Method, string Outcome, string? Message)> tests)
    {
        System.Text.StringBuilder definitions = new();
        System.Text.StringBuilder results = new();
        int index = 0;
        foreach ((string className, string method, string outcome, string? message) in tests)
        {
            string id = $"00000000-0000-0000-0000-{index++:D12}";
            definitions.Append(System.Globalization.CultureInfo.InvariantCulture, $"<UnitTest name=\"{method}\" id=\"{id}\"><TestMethod className=\"{SecurityElement.Escape(className)}\" name=\"{SecurityElement.Escape(method)}\" /></UnitTest>");
            string output = message is null
                ? ""
                : $"<Output><ErrorInfo><Message>{SecurityElement.Escape(message)}</Message><StackTrace>   at {method}() in File.cs:line 1</StackTrace></ErrorInfo></Output>";
            results.Append(System.Globalization.CultureInfo.InvariantCulture, $"<UnitTestResult testId=\"{id}\" testName=\"{method}(x: 1)\" outcome=\"{outcome}\">{output}</UnitTestResult>");
        }

        return Wrap(definitions.ToString(), results.ToString());
    }
}
