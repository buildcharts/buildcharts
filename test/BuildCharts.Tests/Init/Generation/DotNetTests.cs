using DotNetScaffolder = BuildCharts.Tool.Init.Generation.DotNet;

namespace BuildCharts.Tests.Init.Generation;

[TestClass]
[DoNotParallelize]
public sealed class DotNetTests : TestBase
{
    private string _directory;
    private string _originalDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        _originalDirectory = Directory.GetCurrentDirectory();
        _directory = Path.Combine(Path.GetTempPath(), "buildcharts-init-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Directory.SetCurrentDirectory(_directory);
        File.WriteAllText("Example.sln", "");
        File.WriteAllText("global.json", """{"sdk":{"version":"10.0.100"}}""");
        Directory.CreateDirectory("src/Example.Worker");
        File.WriteAllText("src/Example.Worker/Example.Worker.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Worker">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText("src/Example.Worker/Dockerfile", "FROM base");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Directory.SetCurrentDirectory(_originalDirectory);
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    [DataRow("Microsoft.NET.Sdk.Worker", "", "runtime")]
    [DataRow("Microsoft.NET.Sdk.Web", "", "aspnet")]
    [DataRow("Microsoft.NET.Sdk", "", "aspnet")]
    [DataRow("Microsoft.NET.Sdk.Worker", "<ItemGroup><FrameworkReference Include='Microsoft.AspNetCore.App' /></ItemGroup>", "aspnet")]
    [DataRow("Microsoft.NET.Sdk.Worker;Microsoft.NET.Sdk.Web", "", "aspnet")]
    [DataRow("Microsoft.NET.Sdk.Worker", "<!-- <FrameworkReference Include='Microsoft.AspNetCore.App' /> -->", "aspnet")]
    public async Task CreateBuildConfig_ShouldSelectRuntimeImage(string sdk, string references, string image)
    {
        // Arrange
        File.WriteAllText("src/Example.Worker/Example.Worker.csproj", $"""
            <Project Sdk="{sdk}">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              {references}
            </Project>
            """);

        // Act
        var result = await DotNetScaffolder.CreateBuildConfig("build.yml", default);

        // Assert
        var contexts = (Dictionary<string, string>)result["src/Example.Worker/Example.Worker.csproj"].With["contexts"];
        Assert.AreEqual($"docker-image://mcr.microsoft.com/dotnet/{image}:10.0", contexts["base"]);
        CollectionAssert.AreEqual(new[] { "docker" }, result["src/Example.Worker/Example.Worker.csproj"].Types);
        var buildContexts = (Dictionary<string, string>)result["Example.sln"].With["contexts"];
        Assert.AreEqual("docker-image://mcr.microsoft.com/dotnet/sdk:10.0", buildContexts["base"]);
        StringAssert.Contains(await File.ReadAllTextAsync("build.yml"), $"base: docker-image://mcr.microsoft.com/dotnet/{image}:10.0");
    }

    [TestMethod]
    public async Task CreateBuildConfig_ShouldSupportSdkElements()
    {
        // Arrange
        File.WriteAllText("src/Example.Worker/Example.Worker.csproj", """
            <Project>
              <Sdk Name="Microsoft.NET.Sdk.Worker" />
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        // Act
        var result = await DotNetScaffolder.CreateBuildConfig("build.yml", default);

        // Assert
        var contexts = (Dictionary<string, string>)result["src/Example.Worker/Example.Worker.csproj"].With["contexts"];
        Assert.AreEqual("docker-image://mcr.microsoft.com/dotnet/runtime:10.0", contexts["base"]);
    }

    [TestMethod]
    [DataRow("net8.0", "8.0")]
    [DataRow("net9.0-windows", "9.0")]
    [DataRow("$(TargetFrameworkVersion)", "10.0")]
    public async Task CreateBuildConfig_ShouldUseProjectRuntimeVersion(string framework, string expectedVersion)
    {
        // Arrange
        File.WriteAllText("src/Example.Worker/Example.Worker.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk.Worker">
              <PropertyGroup><TargetFramework>{framework}</TargetFramework></PropertyGroup>
            </Project>
            """);

        // Act
        var result = await DotNetScaffolder.CreateBuildConfig("build.yml", default);

        // Assert
        var contexts = (Dictionary<string, string>)result["src/Example.Worker/Example.Worker.csproj"].With["contexts"];
        Assert.AreEqual($"docker-image://mcr.microsoft.com/dotnet/runtime:{expectedVersion}", contexts["base"]);
        var buildContexts = (Dictionary<string, string>)result["Example.sln"].With["contexts"];
        Assert.AreEqual("docker-image://mcr.microsoft.com/dotnet/sdk:10.0", buildContexts["base"]);
    }
}
