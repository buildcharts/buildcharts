using BuildCharts.Tool;
using System.Globalization;

namespace BuildCharts.Tests;

[TestClass]
public sealed class ProgramTests : TestBase
{
    [TestMethod]
    public async Task Main_ShouldReturnUnknownCommand_WhenCommandIsUnrecognized()
    {
        // Arrange
        await using var error = new StringWriter(CultureInfo.InvariantCulture);
        var originalError = Console.Error;

        try
        {
            Console.SetError(error);

            // Act
            var result = await Program.Main(["asd"]);

            // Assert
            Assert.AreEqual(1, result);
            Assert.AreEqual($"buildcharts: unknown command: buildcharts asd{Environment.NewLine}{Environment.NewLine}Run 'buildcharts --help' for more information{Environment.NewLine}", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
