using AutoFixture;
using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Generate;

namespace BuildCharts.Tests.Generate;

[TestClass]
public sealed class DockerHclGeneratorTests : TestBase
{
    private BuildConfig _buildConfig;
    private ChartConfig _chartConfig;
    private DockerHclGenerator Sut => Fixture.Freeze<DockerHclGenerator>();

    [TestInitialize]
    public void TestInitialize()
    {
        _buildConfig = new BuildConfig
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
            Types = new Dictionary<string, TypeMatrixDefinition>(),
        };

        _chartConfig = new ChartConfig
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
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitAllMatrixAxesInSortedOrder()
    {
        // Arrange
        _buildConfig.Types = new Dictionary<string, TypeMatrixDefinition>
        {
            ["publish"] = new()
            {
                Matrix = new Dictionary<string, List<string>>
                {
                    ["runtime"] = ["win-x64", "win-arm64"],
                    ["configuration"] = ["debug", "release"],
                },
            },
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "configuration = [\"debug\", \"release\"]");
        StringAssert.Contains(hcl, "runtime = [\"win-x64\", \"win-arm64\"]");

        var configurationIndex = hcl.IndexOf("configuration = [\"debug\", \"release\"]", StringComparison.Ordinal);
        var runtimeIndex = hcl.IndexOf("runtime = [\"win-x64\", \"win-arm64\"]", StringComparison.Ordinal);
        Assert.IsTrue(configurationIndex >= 0 && runtimeIndex >= 0 && configurationIndex < runtimeIndex);
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitAllowInTarget()
    {
        // Arrange
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].With = new Dictionary<string, object>
        {
            ["allow"] = new List<object> { "network.host" },
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "  allow = \"${item.allow}\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitAllowInMatrix()
    {
        // Arrange
        _buildConfig.Targets = new Dictionary<string, List<TargetDefinition>>
        {
            ["src/dotnet-krp/dotnet-krp.csproj"] =
            [
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>
                    {
                        ["allow"] = new List<object> { "network.host", "security.insecure" },
                    },
                },
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>(),
                },
            ],
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "allow = [\"network.host\",\"security.insecure\"]");
        StringAssert.Contains(hcl, "allow = []");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitArgsInTarget()
    {
        // Arrange
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].With = new Dictionary<string, object>
        {
            ["args"] = new Dictionary<object, object>
            {
                ["image"] = "mcr.microsoft.com/dotnet/sdk:10.0",
            },
        };
        _buildConfig.Types = new Dictionary<string, TypeMatrixDefinition>
        {
            ["publish"] = new()
            {
                Matrix = new Dictionary<string, List<string>>
                {
                    ["runtime"] = ["win-x64", "win-arm64"],
                },
            },
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "  args = {");
        StringAssert.Contains(hcl, "    BUILDCHARTS_SRC = item.src");
        StringAssert.Contains(hcl, "    BUILDCHARTS_TYPE = \"publish\"");
        StringAssert.Contains(hcl, "    IMAGE = \"${item.image}\"");
        StringAssert.Contains(hcl, "    RUNTIME = \"${runtime}\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitArgsInMatrix()
    {
        // Arrange
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].With = new Dictionary<string, object>
        {
            ["args"] = new Dictionary<object, object>
            {
                ["image"] = "mcr.microsoft.com/dotnet/sdk:10.0",
                ["features"] = new List<object> { "aot", "trimmed" },
            },
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "image = \"mcr.microsoft.com/dotnet/sdk:10.0\"");
        StringAssert.Contains(hcl, "features = \"aot,trimmed\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitBaseContextInTarget()
    {
        // Arrange
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].With = new Dictionary<string, object>
        {
            ["base"] = "mcr.microsoft.com/dotnet/aspnet:10.0",
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "    base = \"docker-image://${item.base}\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitBaseInMatrix()
    {
        // Arrange
        _buildConfig.Targets = new Dictionary<string, List<TargetDefinition>>
        {
            ["src/dotnet-krp/dotnet-krp.csproj"] =
            [
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>
                    {
                        ["base"] = "mcr.microsoft.com/dotnet/aspnet:10.0",
                    },
                },
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>(),
                },
            ],
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "base = \"mcr.microsoft.com/dotnet/aspnet:10.0\"");
        StringAssert.Contains(hcl, "base = \"\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitBuildContextInTarget()
    {
        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);
        var hcl = result.ToString();

        // Assert
        StringAssert.Contains(hcl, "  contexts = {");
        StringAssert.Contains(hcl, "    build = \"target:build\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitDefaultDockerfilePathInTarget()
    {
        // Arrange
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].With = new Dictionary<string, object>
        {};

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "  dockerfile = \"./.buildcharts/dotnet-publish/Dockerfile\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitDefaultGroupTargetsInDefaultGroup()
    {
        // Arrange
        _buildConfig.Targets = new Dictionary<string, List<TargetDefinition>>
        {
            ["src/a/a.csproj"] =
            [
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>(),
                },
            ],
            ["src/b/b.csproj"] =
            [
                new TargetDefinition
                {
                    Type = "docker",
                    With = new Dictionary<string, object>(),
                },
            ],
            ["src/c/c.csproj"] =
            [
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>(),
                },
            ],
        };
        _chartConfig.Dependencies =
        [
            new ChartDependency { Alias = "publish", Name = "dotnet-publish", },
            new ChartDependency { Alias = "docker", Name = "dotnet-docker", },
        ];

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "group \"default\" {");
        StringAssert.Contains(hcl, "  targets = [\"publish\", \"docker\"]");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitDockerfileInMatrix()
    {
        // Arrange
        _buildConfig.Targets = new Dictionary<string, List<TargetDefinition>>
        {
            ["src/dotnet-krp/dotnet-krp.csproj"] =
            [
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>
                    {
                        ["dockerfile"] = "./custom/Dockerfile",
                    },
                },
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>(),
                },
            ],
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);
        var hcl = result.ToString();

        // Assert
        StringAssert.Contains(hcl, "dockerfile = \"./custom/Dockerfile\"");
        StringAssert.Contains(hcl, "dockerfile = \"./.buildcharts/dotnet-publish/Dockerfile\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitDockerfileInlineInTarget()
    {
        // Arrange
        var chartName = $"publish-inline-{Guid.NewGuid():N}";
        _chartConfig.Dependencies[0].Alias = "publish";
        _chartConfig.Dependencies[0].Name = chartName;
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].With = new Dictionary<string, object>();

        var dockerfileDir = Path.Combine(".buildcharts", chartName);
        var dockerfilePath = Path.Combine(dockerfileDir, "Dockerfile");
        Directory.CreateDirectory(dockerfileDir);
        await File.WriteAllTextAsync(dockerfilePath, "FROM alpine:3.20\nRUN echo hello\n");

        try
        {
            // Act
            var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: true);

            // Assert
            var hcl = result.ToString();
            StringAssert.Contains(hcl, "  dockerfile-inline = <<EOF");
            StringAssert.Contains(hcl, "FROM alpine:3.20");
            StringAssert.Contains(hcl, "RUN echo hello");
            StringAssert.Contains(hcl, "EOF");
        }
        finally
        {
            if (Directory.Exists(dockerfileDir))
            {
                Directory.Delete(dockerfileDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitDockerfileReferenceInTarget()
    {
        // Arrange
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].With = new Dictionary<string, object>
        {
            ["dockerfile"] = "./custom/Dockerfile",
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "  dockerfile = \"${item.dockerfile}\"");
    }

    [TestMethod]
    [DataRow("build", "output = [\"type=cacheonly,mode=max\"]", "", "")]
    [DataRow("docker", "output = [\"type=docker\"]", "", "")]
    [DataRow("publish", "output = [", "\"type=cacheonly,mode=max\",", "\"type=local,dest=.buildcharts/output/publish\"")]
    public async Task GenerateAsync_ShouldEmitOutputInTarget(string type, string expectedLine1, string expectedLine2, string expectedLine3)
    {
        // Arrange
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].Type = type;
        _chartConfig.Dependencies[0].Alias = type;

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, $"target \"{type}\" {{");
        StringAssert.Contains(hcl, expectedLine1);
        StringAssert.Contains(hcl, expectedLine2);
        StringAssert.Contains(hcl, expectedLine3);
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitPublishRuntimeInMatrix()
    {
        // Arrange
        _buildConfig.Types = new Dictionary<string, TypeMatrixDefinition>
        {
            ["publish"] = new()
            {
                Matrix = new Dictionary<string, List<string>>
                {
                    ["runtime"] = ["win-x64", "win-arm64"]
                },
            },
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "name = \"${item.name}_${runtime}\"");
        StringAssert.Contains(hcl, "runtime = [\"win-x64\", \"win-arm64\"]");
        StringAssert.Contains(hcl, "RUNTIME = \"${runtime}\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitTagsInTarget()
    {
        // Arrange
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].With = new Dictionary<string, object>
        {
            ["tags"] = new List<object> { "repo/app:1.0.0" },
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "  tags = \"${item.tags}\"");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldEmitTagsInMatrix()
    {
        // Arrange
        _buildConfig.Targets = new Dictionary<string, List<TargetDefinition>>
        {
            ["src/dotnet-krp/dotnet-krp.csproj"] =
            [
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>
                    {
                        ["tags"] = new List<object> { "repo/app:1.0.0", "repo/app:latest" },
                    },
                },
                new TargetDefinition
                {
                    Type = "publish",
                    With = new Dictionary<string, object>(),
                },
            ],
        };

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "tags = [\"repo/app:1.0.0\",\"repo/app:latest\"]");
        StringAssert.Contains(hcl, "tags = []");
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldNotEmitBuildContextInTarget()
    {
        // Arrange
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].Type = "build";
        _chartConfig.Dependencies[0].Alias = "build";

        // Act
        var result = await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: false);

        // Assert
        var hcl = result.ToString();
        StringAssert.Contains(hcl, "  contexts = {");
        Assert.IsFalse(hcl.Contains("    build = \"target:build\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GenerateAsync_ShouldThrowFileNotFoundExceptionForInlineDockerfileInTarget()
    {
        // Arrange
        var chartName = $"publish-inline-missing-{Guid.NewGuid():N}";
        _chartConfig.Dependencies[0].Alias = "publish";
        _chartConfig.Dependencies[0].Name = chartName;
        _buildConfig.Targets["src/dotnet-krp/dotnet-krp.csproj"][0].With = new Dictionary<string, object>();

        // Act + Assert
        try
        {
            await Sut.GenerateAsync(_buildConfig, _chartConfig, useInlineDockerFile: true);
            Assert.Fail("Expected FileNotFoundException was not thrown.");
        }
        catch (FileNotFoundException ex)
        {
            StringAssert.Contains(ex.Message, $".buildcharts/{chartName}/Dockerfile");
        }
    }
}
