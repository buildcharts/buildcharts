using BuildCharts.Tool.Configuration;
using BuildCharts.Tool.Configuration.Models;

namespace BuildCharts.Tests.Configuration;

[TestClass]
public sealed class BuildConfigVariablesTests : TestBase
{
    [TestMethod]
    public void ReadBuildConfigAsync_ParsesVariableSequenceWithInlineDefaults()
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
    public void ReadBuildConfigAsync_ParsesVariableSequenceWithDefaultBlock()
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
    public void ReadBuildConfigAsync_ParsesVariableSequence()
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
    public void ReadBuildConfigAsync_ParsesVariableMapping()
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
    public void ReadBuildConfigAsync_ThrowsForScalarWithEquals()
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
        Assert.Throws<YamlDotNet.Core.YamlException>(Act);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ThrowsForMappingWithMultipleEntries()
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
        Assert.Throws<YamlDotNet.Core.YamlException>(Act);
    }

    [TestMethod]
    public void ReadBuildConfigAsync_ThrowsForEmptyVariableName()
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
        Assert.Throws<YamlDotNet.Core.YamlException>(Act);
    }

    private static VariableDefinition FindVariable(IReadOnlyDictionary<string, VariableDefinition> variables, string name)
    {
        return variables.TryGetValue(name, out var value) ? value : null;
    }
}
