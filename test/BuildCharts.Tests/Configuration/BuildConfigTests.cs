using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;
using YamlDotNet.Core;

namespace BuildCharts.Tests.Configuration;

[TestClass]
public sealed class BuildConfigTests : TestBase
{
    [TestMethod]
    public void ReadBuildConfigAsync_ShouldParseScalarTargetType()
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
    public void ReadBuildConfigAsync_ShouldParseTargetSequenceEntries()
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
    public void ReadBuildConfigAsync_ShouldParseTargetWithArgsMapping()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            targets:
              buildcharts.sln:
                type: build
                with:
                  args:
                    image: mcr.microsoft.com/dotnet/sdk:10.0
                    features: [aot, trimmed]
            """;

        // Act
        var config = ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        var target = config.Targets["buildcharts.sln"][0];
        Assert.IsTrue(target.With.TryGetValue("args", out var rawArgs));
        Assert.IsInstanceOfType<Dictionary<object, object>>(rawArgs);

        var args = (Dictionary<object, object>)rawArgs;
        Assert.AreEqual("mcr.microsoft.com/dotnet/sdk:10.0", args["image"]);
        CollectionAssert.AreEqual(new List<object> { "aot", "trimmed" }, (List<object>)args["features"]);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ShouldParseTypeArrayMapping()
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
    public void ReadBuildConfigAsync_ShouldParseTypeMatrix()
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

    [TestMethod]
    public void ReadBuildConfigAsync_ShouldParseVariableMapping()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            variables:
              VERSION: "1.0.0-local"
              COMMIT: "0000000000000000000000000000000000000000"
            """;

        // Act
        var config = ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        var version = FindVariable(config.Variables, "VERSION");
        var commit = FindVariable(config.Variables, "COMMIT");

        Assert.IsNotNull(version);
        Assert.AreEqual("1.0.0-local", version.Default);
        Assert.IsNotNull(commit);
        Assert.AreEqual("0000000000000000000000000000000000000000", commit.Default);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ShouldParseVariableSequence()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            variables:
              - VERSION
              - COMMIT
            """;

        // Act
        var config = ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        var version = FindVariable(config.Variables, "VERSION");
        var commit = FindVariable(config.Variables, "COMMIT");

        Assert.IsNotNull(version);
        Assert.AreEqual(string.Empty, version.Default);
        Assert.IsNotNull(commit);
        Assert.AreEqual(string.Empty, commit.Default);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ShouldParseVariableSequenceWithDefaultBlock()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            variables:
              - VERSION:
                  default: "1.0.0-local"
              - COMMIT: ""
            """;

        // Act
        var config = ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        var version = FindVariable(config.Variables, "VERSION");
        Assert.IsNotNull(version);
        Assert.AreEqual("1.0.0-local", version.Default);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ShouldParseVariableSequenceWithInlineDefaults()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            variables:
              - VERSION: "1.0.0-local"
              - COMMIT: ""
            """;

        // Act
        var config = ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        var version = FindVariable(config.Variables, "VERSION");
        var commit = FindVariable(config.Variables, "COMMIT");

        Assert.IsNotNull(version);
        Assert.AreEqual("1.0.0-local", version.Default);
        Assert.IsNotNull(commit);
        Assert.AreEqual(string.Empty, commit.Default);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ShouldThrowYamlExceptionForEmptyVariableName()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            variables:
              - "":
                  default: "1.0.0-local"
            """;

        // Act
        void Act() => ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        Assert.Throws<YamlException>(Act);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ShouldThrowYamlExceptionForMappingWithMultipleEntries()
    {
        // Arrange
        var yaml =
            """
            version: v1beta

            variables:
              - VERSION:
                  default: "1.0.0-local"
                COMMIT: ""
            """;

        // Act
        void Act() => ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        Assert.Throws<YamlException>(Act);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ShouldThrowYamlExceptionForMappingWithoutType()
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
        Assert.Throws<YamlException>(Act);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ShouldThrowYamlExceptionForScalarWithEquals()
    {
        // Arrange
        var yaml =
            """
            version: v1beta
            variables:
              - IMAGE=mcr.microsoft.com/dotnet/aspnet:9.0
            targets:
              buildcharts.sln:
                type: build
            """;

        // Act
        void Act() => ConfigurationManager.Deserializer.Deserialize<BuildConfig>(yaml);

        // Assert
        Assert.Throws<YamlException>(Act);
    }

    private static VariableDefinition FindVariable(IReadOnlyDictionary<string, VariableDefinition> variables, string name)
    {
        return variables.TryGetValue(name, out var value) ? value : null;
    }
}
