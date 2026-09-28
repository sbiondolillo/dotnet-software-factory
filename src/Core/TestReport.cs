using System.Xml;
using System.Xml.Linq;

namespace Core;

/// <summary>The outcome of one test in a TRX file.</summary>
public enum TestOutcome
{
    /// <summary>The file says <c>Passed</c>.</summary>
    Passed,

    /// <summary>Any outcome other than <c>Passed</c> and <c>NotExecuted</c>.</summary>
    Failed,

    /// <summary>The file says <c>NotExecuted</c>.</summary>
    Skipped,
}

/// <summary>One test read from a TRX file.</summary>
/// <param name="ClassName">The class as the file names it, with its namespace.</param>
/// <param name="Method">The method, without arguments.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="Message">The failure message alone, or null when the file holds none.</param>
public sealed record ReportedTest(string ClassName, string Method, TestOutcome Outcome, string? Message);

/// <summary>Reads the tests of a TRX file.</summary>
public static class TestReport
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>Reads every test of the TRX file at <paramref name="path"/>, one per result, in file order.</summary>
    /// <exception cref="TestReportException">The file is not a TRX file the parser accepts, or a result has no test definition.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static IReadOnlyList<ReportedTest> Read(string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (XmlException exception)
        {
            throw new TestReportException(path, exception.Message);
        }

        XElement root = document.Root!;
        if (root.Name != Ns + "TestRun")
        {
            throw new TestReportException(path, "the root element is not a TestRun of the TRX namespace");
        }

        Dictionary<string, XElement> methods = [];
        foreach (XElement unitTest in root.Elements(Ns + "TestDefinitions").Elements(Ns + "UnitTest"))
        {
            if (unitTest.Attribute("id")?.Value is string id
                && unitTest.Element(Ns + "TestMethod") is XElement method)
            {
                methods[id] = method;
            }
        }

        List<ReportedTest> tests = [];
        foreach (XElement result in root.Elements(Ns + "Results").Elements(Ns + "UnitTestResult"))
        {
            string? testId = result.Attribute("testId")?.Value;
            if (testId is null || !methods.TryGetValue(testId, out XElement? method))
            {
                throw new TestReportException(path, $"the result of test {testId ?? "(no id)"} has no test definition");
            }

            string outcome = result.Attribute("outcome")?.Value ?? "";
            TestOutcome mapped = outcome switch
            {
                "Passed" => TestOutcome.Passed,
                "NotExecuted" => TestOutcome.Skipped,
                _ => TestOutcome.Failed,
            };
            string? message = result.Descendants(Ns + "ErrorInfo").FirstOrDefault()?.Element(Ns + "Message")?.Value;
            tests.Add(new ReportedTest(
                method.Attribute("className")?.Value ?? "",
                method.Attribute("name")?.Value ?? "",
                mapped,
                message));
        }

        return tests;
    }
}
