using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;

namespace BuildCharts.Tests.Configuration;

[TestClass]
public sealed class BuildConfigMatrixTests
{
    [TestMethod]
    public void ReadBuildConfigAsync_ParsesTypeMatrix()
    {
        // Arrange
        var yaml =
            """
            version: v1beta
            
            targets:
              buildcharts.sln:
                type: build
                
            types:
              publish:
                matrix:
                  runtime: ["win-x64", "win-arm64"]
            """;

        // Act
        var config = ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        var matrix = config.Types["publish"].Matrix;
        CollectionAssert.AreEquivalent(new List<string> { "win-x64", "win-arm64" }, matrix["runtime"]);
    }
}
