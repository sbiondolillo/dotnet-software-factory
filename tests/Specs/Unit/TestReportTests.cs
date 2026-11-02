using Core;
using Xunit;

namespace Specs.Unit;

public sealed class TestReportTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("test-report-unit-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string content)
    {
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".trx");
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("Failed")]
    [InlineData("Aborted")]
    [InlineData("")]
    public void AnOutcomeOtherThanPassedOrNotExecutedIsFailed(string outcome)
    {
        string path = Write(Steps.TrxBuilder.Build([("A.B", "M", outcome, null)]));
        Assert.Equal(TestOutcome.Failed, Assert.Single(TestReport.Read(path)).Outcome);
    }

    [Fact]
    public void NotExecutedIsSkippedAndPassedIsPassed()
    {
        string path = Write(Steps.TrxBuilder.Build([("A.B", "M", "NotExecuted", null), ("A.B", "N", "Passed", null)]));
        Assert.Equal([TestOutcome.Skipped, TestOutcome.Passed], TestReport.Read(path).Select(test => test.Outcome));
    }

    [Fact]
    public void NonXmlThrowsWithPathAndParserMessage()
    {
        string path = Write("This is not a test report.");
        TestReportException exception = Assert.Throws<TestReportException>(() => TestReport.Read(path));
        Assert.Equal(path, exception.Path);
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyFileThrowsNamingThePath()
    {
        string path = Write("");
        Assert.Contains(path, Assert.Throws<TestReportException>(() => TestReport.Read(path)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WellFormedXmlThatIsNotATrxThrowsNamingThePath()
    {
        string path = Write("<html/>");
        Assert.Contains(path, Assert.Throws<TestReportException>(() => TestReport.Read(path)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADynamicSkipIsSkippedWithItsReason()
    {
        // The shape that xunit.v3 3.2.2 writes for Assert.SkipUnless, measured on 2026-11-02 (#451).
        string path = Write(Steps.TrxBuilder.Wrap(
            "<UnitTest name=\"FactSkipsUnlessFalse\" id=\"4cd4a360-dddb-e657-4036-f8868034c02a\">"
            + "<TestMethod className=\"Specs.Unit.ZzMeasureSkipTests\" name=\"FactSkipsUnlessFalse\" /></UnitTest>",
            "<UnitTestResult testId=\"4cd4a360-dddb-e657-4036-f8868034c02a\" testName=\"Specs.Unit.ZzMeasureSkipTests.FactSkipsUnlessFalse\" outcome=\"NotExecuted\">"
            + "<Output><ErrorInfo><Message>measure: the tool is absent</Message></ErrorInfo></Output></UnitTestResult>"));

        ReportedTest test = Assert.Single(TestReport.Read(path));

        Assert.Equal(TestOutcome.Skipped, test.Outcome);
        Assert.Equal("Specs.Unit.ZzMeasureSkipTests", test.ClassName);
        Assert.Equal("FactSkipsUnlessFalse", test.Method);
        Assert.Equal("measure: the tool is absent", test.Message);
    }

    [Fact]
    public void AMissingFileThrowsFileNotFound()
        => Assert.Throws<FileNotFoundException>(() => TestReport.Read(Path.Combine(_root, "none.trx")));

    [Fact]
    public void AFailedResultWithNoErrorInfoHasNoMessage()
    {
        string path = Write(Steps.TrxBuilder.Build([("A.B", "M", "Failed", null)]));
        ReportedTest test = Assert.Single(TestReport.Read(path));
        Assert.Equal(TestOutcome.Failed, test.Outcome);
        Assert.Null(test.Message);
    }

    [Fact]
    public void TheMessageHoldsNoStackTrace()
    {
        string path = Write(Steps.TrxBuilder.Build([("A.B", "M", "Failed", "boom")]));
        Assert.Equal("boom", Assert.Single(TestReport.Read(path)).Message);
    }

    [Fact]
    public void AResultWithNoDefinitionThrowsNamingThePath()
    {
        string path = Write(Steps.TrxBuilder.Wrap("", "<UnitTestResult testId=\"x\" outcome=\"Passed\" />"));
        Assert.Contains(path, Assert.Throws<TestReportException>(() => TestReport.Read(path)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoResultsOfOneMethodAreTwoTestsInFileOrder()
    {
        string path = Write(Steps.TrxBuilder.Build([("A.B", "TheGreeterGreetsWho", "Passed", null), ("A.B", "TheGreeterGreetsWho", "Failed", "x")]));
        IReadOnlyList<ReportedTest> tests = TestReport.Read(path);
        Assert.Equal([TestOutcome.Passed, TestOutcome.Failed], tests.Select(test => test.Outcome));
        Assert.All(tests, test => Assert.Equal("TheGreeterGreetsWho", test.Method));
    }

    [Fact]
    public void NoResultsIsAnEmptyList()
        => Assert.Empty(TestReport.Read(Write(Steps.TrxBuilder.Wrap("", ""))));
}
