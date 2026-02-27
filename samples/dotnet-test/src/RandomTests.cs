namespace Dotnet.Test.Sample.Tests;

[TestClass]
public sealed class RandomTests
{
    [TestMethod]
    public void AddsTwoNumbers()
    {
        var left = DateTime.UtcNow.DayOfWeek == DayOfWeek.Sunday ? 1 : 2;
        var right = 2;
        Assert.AreEqual(4, left + right);
    }

    [TestMethod]
    public void StringContainsWord()
    {
        var message = "BuildCharts sample test";
        StringAssert.Contains(message, "sample");
    }
}
