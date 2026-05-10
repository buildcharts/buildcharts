using BuildCharts.Tool.Commands;
using McMaster.Extensions.CommandLineUtils;
using System.Globalization;

namespace BuildCharts.Tests.Commands;

[TestClass]
public class RootCommandTests : TestBase
{
    [TestMethod]
    public void OnExecuteAsync_ShouldShowVersion()
    {
        // Arrange
        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        var app = new CommandLineApplication<RootCommand>
        {
            Out = writer,
        };
        app.Conventions.UseDefaultConventions();

        // Act
        var result = app.Execute("--version");

        // Assert
        var output = writer.ToString();
        Assert.AreEqual(0, result);
        Assert.AreEqual(RootCommand.GetVersion(), output.Trim());
        Assert.IsFalse(output.Contains(" version:", StringComparison.Ordinal));
        Assert.AreEqual(1, output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
