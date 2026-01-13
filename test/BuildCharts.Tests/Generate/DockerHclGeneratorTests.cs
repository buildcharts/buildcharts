using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Generate;

namespace BuildCharts.Tests.Generate;

[TestClass]
public sealed class DockerHclGeneratorTests
{
    [TestMethod]
    public async Task GenerateAsync_EmitsPublishRuntimeMatrix()
    {
        // Arrange
        var buildConfig = new BuildConfig
        {
            Variables = new Dictionary<string, VariableDefinition>(),
            Targets = new Dictionary<string, List<TargetDefinition>>
            {
                ["src/dotnet-krp/dotnet-krp.csproj"] =
                [
                    new TargetDefinition
                    {
                        Type = "publish", 
                        With = new Dictionary<string, object>(),
                    },
                ],
            },
            Types = new Dictionary<string, TypeMatrixDefinition>
            {
                ["publish"] = new()
                {
                    Matrix = new Dictionary<string, List<string>>
                    {
                        ["runtime"] = ["win-x64", "win-arm64"]
                    },
                },
            },
        };

        var chartConfig = new ChartConfig
        {
            Dependencies =
            [
                new ChartDependency
                {
                    Alias = "publish", 
                    Name = "dotnet-publish",
                },
            ],
        };

        var sut = new DockerHclGenerator();

        // Act
        var result = await sut.GenerateAsync(buildConfig, chartConfig, useInlineDockerFile: false);
        var hcl = result.ToString();

        // Assert
        StringAssert.Contains(hcl, "runtime = [\"win-x64\", \"win-arm64\"]");
        StringAssert.Contains(hcl, "RUNTIME = \"${runtime}\"");
    }
}
