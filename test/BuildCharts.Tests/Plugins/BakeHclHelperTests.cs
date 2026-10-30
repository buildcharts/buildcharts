using BuildCharts.Tool.Plugins.Helpers;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class BakeHclHelperTests : TestBase
{
    [TestMethod]
    [DataRow("{ nested = { value = 1 } }", '{', '}')]
    [DataRow("{ value = \"}\\\"{\" }", '{', '}')]
    [DataRow("[\"escaped\\\" ]\", [\"nested\"]]", '[', ']')]
    public void FindClosingDelimiter_ShouldFindClosingDelimiter(string text, char opening, char closing)
    {
        var result = BakeHclHelper.FindClosingDelimiter(text, 0, opening, closing);

        Assert.AreEqual(text.Length - 1, result);
    }

    [TestMethod]
    [DataRow("{ value = { }", '{', '}')]
    [DataRow("[\"value\"", '[', ']')]
    public void FindClosingDelimiter_ShouldReturnMinusOneForUnclosedDelimiter(string text, char opening, char closing)
    {
        var result = BakeHclHelper.FindClosingDelimiter(text, 0, opening, closing);

        Assert.AreEqual(-1, result);
    }
}
