
using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Plugins;
using BuildCharts.Tool.Plugins.TestcontainersDinD_v1.Helpers;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Spins up a Docker-in-Docker daemon using Testcontainers so subsequent build steps can talk to a dedicated Docker engine.
/// </summary>
public sealed class TestcontainersDinDPlugin : IBuildChartsPlugin
{
    public string Name => "TestcontainersDinD@v1";
    private const string CONTAINER_NAME = "buildcharts-dind";
    private const string CONTAINER_IMAGE = "docker:27-dind";
    private string _containerIp;

    public async Task OnBeforeGenerateAsync(BuildConfig buildConfig, CancellationToken ct)
    {
        try
        {
            var runningContainer = await IfAlreadyExistsAndSameImageOtherwiseKill(ct);
            if (runningContainer is not null)
            {
                _containerIp = runningContainer.NetworkSettings.Networks.First().Value.IPAddress;
                Console.WriteLine($"Successfully started buildcharts-dind container (already running with status: {runningContainer.Status})");
            }
            else
            {
                var image = Environment.GetEnvironmentVariable("BUILDCHARTS_DIND_IMAGE") ?? CONTAINER_IMAGE;

                var builder = new ContainerBuilder()
                    .WithImage(image)
                    .WithName(CONTAINER_NAME)
                    .WithPrivileged(true)
                    .WithPortBinding(2375, 2375)
                    .WithEnvironment("DOCKER_TLS_CERTDIR", "")
                    .WithCommand("--tls=false")
                    .WithCleanUp(false)
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(2375));

                var container = builder.Build();
                await container.StartAsync(ct);

                _containerIp = container.IpAddress;

                WriteValueToFile("container_debuginfo.json", JsonSerializer.Serialize(container, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Successfully started buildcharts-dind container");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{Name} failed with error: {ex}");
            throw;
        }
    }

    public Task OnAfterGenerateAsync(BuildConfig buildConfig, ChartConfig cartConfig, StringBuilder hclStringBuilder, CancellationToken ct)
    {
        BakeHclPatchHelper.Execute(hclStringBuilder, _containerIp);
        return Task.CompletedTask;
    }
    
    private void WriteValueToFile(string key, string value)
    {
        var folder = Path.Combine(Directory.GetCurrentDirectory(), ".buildcharts", "plugins", Name);
        var filePath = Path.Combine(folder, key);

        Directory.CreateDirectory(folder);
        File.WriteAllText(filePath, value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine($"Successfully created file '{Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}'");
    }

    private static async Task<ContainerListResponse> IfAlreadyExistsAndSameImageOtherwiseKill(CancellationToken ct)
    {
        var host = Environment.GetEnvironmentVariable("DOCKER_HOST");

        if (string.IsNullOrWhiteSpace(host))
        {
            host = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "npipe://./pipe/docker_engine"
                : "unix:///var/run/docker.sock";
        }

        using var client = new DockerClientConfiguration(new Uri(host)).CreateClient();

        // Query containers by name (Docker returns names with leading '/').
        var list = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool>
                {
                    [CONTAINER_NAME] = true,
                },
            },
        }, ct);

        // If a buildcharts-dind container with the desired image exists, do nothing.
        var alreadyRunningWithImage = list.FirstOrDefault(x => x.Names.Contains("/buildcharts-dind") && string.Equals(x.Image, CONTAINER_IMAGE, StringComparison.OrdinalIgnoreCase));
        if (alreadyRunningWithImage?.State == "running")
        {
            return alreadyRunningWithImage;
        }

        // Otherwise, stop/remove all containers that match by name.
        foreach (var c in list)
        {
            try
            {
                // Best-effort stop (ignore failures)
                try
                {
                    await client.Containers.StopContainerAsync(c.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 2 }, ct);
                }
                catch
                {
                    // ignore
                }

                await client.Containers.RemoveContainerAsync(c.ID, new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = true,
                }, ct);
            }
            catch
            {
                // If there are multiple matches, keep trying the rest.
            }
        }

        return null;
    }
}

