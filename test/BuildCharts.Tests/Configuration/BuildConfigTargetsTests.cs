using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;
using YamlDotNet.Core;

namespace BuildCharts.Tests.Configuration;

[TestClass]
public sealed class BuildConfigTargetsTests : TestBase
{
    [TestMethod]
    public void ReadBuildConfigAsync_ParsesScalarType()
    {
        // Arrange
        var yaml =
            """
            version: v1beta
            
            targets:
              buildcharts.sln: build
            """;

        // Act
        var config = ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        var targets = config.Targets["buildcharts.sln"];
        Assert.AreEqual(1, targets.Count);
        Assert.AreEqual("build", targets[0].Type);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ParsesTypeArrayMapping()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            targets:
              buildcharts.sln:
                type: [build, nuget]
                with:
                  base: mcr.microsoft.com/dotnet/sdk:10.0
            """;

        // Act
        var config = ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);


        // Assert
        var targets = config.Targets["buildcharts.sln"];
        var types = targets.Select(t => t.Type).ToList();

        CollectionAssert.AreEquivalent(new[] { "build", "nuget" }, types);
        Assert.IsTrue(targets.All(t => t.With.TryGetValue("base", out var baseImage) && Equals(baseImage, "mcr.microsoft.com/dotnet/sdk:10.0")));
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ParsesSequenceEntries()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            targets:
              buildcharts.sln:
                - type: build
                - type: docker
                  with:
                    base: mcr.microsoft.com/dotnet/aspnet:10.0
            """;

        // Act
        var config = ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        var targets = config.Targets["buildcharts.sln"];
        Assert.AreEqual(2, targets.Count);
        Assert.AreEqual("build", targets[0].Type);
        Assert.AreEqual("docker", targets[1].Type);
        Assert.AreEqual("mcr.microsoft.com/dotnet/aspnet:10.0", targets[1].With["base"]);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ThrowsForMappingWithoutType()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            targets:
              buildcharts.sln:
                with:
                  base: mcr.microsoft.com/dotnet/sdk:10.0
            """;

        // Act
        void Act() => ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        Assert.ThrowsException<YamlException>(Act);
    }
}
