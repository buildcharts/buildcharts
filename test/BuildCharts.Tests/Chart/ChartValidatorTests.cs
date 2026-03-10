using AutoFixture;
using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Configuration.Models;

namespace BuildCharts.Tests.Chart;

[TestClass]
public sealed class ChartValidatorTests : TestBase
{
    private BuildConfig _buildConfig;
    private ChartConfig _chartConfig;
    private ChartLock _chartLock;

    [TestInitialize]
    public void TestInitialize()
    {
        _buildConfig = Fixture.Create<BuildConfig>();
        _buildConfig.Targets = new Dictionary<string, List<TargetDefinition>>
        {
            ["buildcharts.sln"] = [new TargetDefinition { Type = "build" }],
        };

        _chartConfig = CreateChartConfig(alias: "build");
        _chartLock = CreateChartLock(name: "dotnet-build", version: "0.0.1", repository: "oci://registry-1.docker.io/buildcharts/dotnet-build");
    }

    [TestMethod]
    public void CalculateChartLockMismatches_ShouldReturnMismatch_WhenLockEntryMissing()
    {
        // Arrange
        _chartLock = Fixture.Create<ChartLock>();
        _chartLock.Dependencies = [];

        // Act
        var result = ChartValidator.CalculateChartLockMismatches(_chartConfig, _chartLock);

        // Assert
        Assert.AreEqual(1, result.Count);
        StringAssert.Contains(result[0], "Missing entry");
    }

    [TestMethod]
    public void CalculateChartLockMismatches_ShouldReturnMismatch_WhenLockHasOrphanedDependency()
    {
        // Arrange
        _chartLock.Dependencies.Add(new ChartLockDependency
        {
            Name = "dotnet-test",
            Version = "0.0.1",
            Repository = "oci://registry-1.docker.io/buildcharts/dotnet-test",
            Digest = "sha256:bbb",
        });

        // Act
        var result = ChartValidator.CalculateChartLockMismatches(_chartConfig, _chartLock);

        // Assert
        Assert.AreEqual(1, result.Count);
        StringAssert.Contains(result[0], "Orphaned lock entry");
    }

    [TestMethod]
    public void CalculateChartLockMismatches_ShouldReturnMismatch_WhenLockVersionDiffers()
    {
        // Arrange
        _chartConfig.Dependencies[0].Version = "0.0.2";

        // Act
        var result = ChartValidator.CalculateChartLockMismatches(_chartConfig, _chartLock);

        // Assert
        Assert.AreEqual(1, result.Count);
        StringAssert.Contains(result[0], "Version mismatch");
    }

    [TestMethod]
    public async Task ValidateConfigAsync_ShouldSucceed_WhenSingleBuildTargetAndKnownTypes()
    {
        // Arrange
        _buildConfig.Targets["buildcharts.sln"] =
        [
            new TargetDefinition { Type = "build" },
            new TargetDefinition { Type = "docker" },
        ];
        _chartConfig.Dependencies.Add(new ChartDependency
        {
            Alias = "docker",
            Name = "dotnet-docker",
            Version = "0.0.1",
            Repository = "oci://registry-1.docker.io/buildcharts",
        });

        // Act
        var result = ChartValidator.ValidateConfigAsync(_buildConfig, _chartConfig);

        // Assert
        await result;
    }

    [TestMethod]
    public void ValidateConfigAsync_ShouldThrowInvalidOperation_WhenBuildTargetCountGreaterThanOne()
    {
        // Arrange
        _buildConfig.Targets = new Dictionary<string, List<TargetDefinition>>
        {
            ["a.sln"] = [new TargetDefinition { Type = "build" }],
            ["b.sln"] = [new TargetDefinition { Type = "build" }],
        };

        // Act
        Action action = () => ChartValidator.ValidateConfigAsync(_buildConfig, _chartConfig).GetAwaiter().GetResult();

        // Assert
        var result = Assert.Throws<InvalidOperationException>(action);
        StringAssert.Contains(result.Message, "Only 1 build target is supported");
    }

    [TestMethod]
    public void ValidateConfigAsync_ShouldThrowInvalidOperation_WhenBuildTargetMissing()
    {
        // Arrange
        _buildConfig.Targets["buildcharts.sln"] = [new TargetDefinition { Type = "test" }];

        // Act
        Action action = () => ChartValidator.ValidateConfigAsync(_buildConfig, _chartConfig).GetAwaiter().GetResult();
        var result = Assert.Throws<InvalidOperationException>(action);

        // Assert
        StringAssert.Contains(result.Message, "Missing build target");
    }

    [TestMethod]
    public void ValidateConfigAsync_ShouldThrowInvalidOperation_WhenUnknownTargetTypeExists()
    {
        // Arrange
        _buildConfig.Targets["buildcharts.sln"] =
        [
            new TargetDefinition { Type = "build" },
            new TargetDefinition { Type = "publish" },
        ];

        // Act
        Action action = () => ChartValidator.ValidateConfigAsync(_buildConfig, _chartConfig).GetAwaiter().GetResult();

        // Assert
        var result = Assert.Throws<InvalidOperationException>(action);
        StringAssert.Contains(result.Message, "Unknown target type(s): publish");
    }

    [TestMethod]
    public async Task ValidateLockFileAsync_ShouldSucceed_WhenLockFileDisabled()
    {
        // Arrange
        _chartLock = Fixture.Create<ChartLock>();
        _chartLock.Dependencies = [];

        // Act
        var result = ChartValidator.ValidateLockFileAsync(_chartConfig, _chartLock, false, CancellationToken.None);

        // Assert
        await result;
    }

    [TestMethod]
    public async Task ValidateLockFileAsync_ShouldSucceed_WhenNoMismatches()
    {
        // Act
        var result = ChartValidator.ValidateLockFileAsync(_chartConfig, _chartLock, true, CancellationToken.None);

        // Assert
        await result;
    }

    [TestMethod]
    public void ValidateLockFileAsync_ShouldThrowInvalidOperation()
    {
        // Arrange
        _chartLock.Dependencies = [];

        // Act
        Action action = () => ChartValidator.ValidateLockFileAsync(_chartConfig, _chartLock, true, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        var result = Assert.Throws<InvalidOperationException>(action);
        StringAssert.Contains(result.Message, "Chart.lock is out of sync");
        StringAssert.Contains(result.Message, "Run `buildcharts update`");
    }

    private static ChartConfig CreateChartConfig(string alias, string version = "0.0.1", string name = "dotnet-build")
    {
        return new ChartConfig
        {
            Dependencies =
            [
                new ChartDependency
                {
                    Alias = alias,
                    Name = name,
                    Version = version,
                    Repository = "oci://registry-1.docker.io/buildcharts",
                },
            ],
        };
    }

    private static ChartLock CreateChartLock(string name, string version, string repository)
    {
        return new ChartLock
        {
            Dependencies =
            [
                new ChartLockDependency
                {
                    Name = name,
                    Version = version,
                    Repository = repository,
                    Digest = "sha256:aaa",
                },
            ],
        };
    }
}
