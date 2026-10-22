using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Plugins.Helpers;
using BuildCharts.Tool.Plugins.NpmAuthenticate_v1.Helpers;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NpmAuthenticate_v1;

public class NpmAuthenticatePlugin : IBuildChartsPlugin
{
    public string Name => "NpmAuthenticate@v1";

    public async Task OnBeforeGenerateAsync(BuildConfig buildConfig, CancellationToken ct)
    {
        var workingFile = Environment.GetEnvironmentVariable("NPM_AUTHENTICATE_WORKING_FILE") ?? ".npmrc";
        var file = new FileInfo(workingFile);
        if (!file.Exists || !file.Name.EndsWith(".npmrc", StringComparison.OrdinalIgnoreCase) || file.LinkTarget != null)
        {
            throw new InvalidOperationException("NpmAuthenticate requires an existing, regular .npmrc file.");
        }

        var secretPath = Path.Combine(".buildcharts", "secrets", "npmrc");
        Directory.CreateDirectory(Path.GetDirectoryName(secretPath));
        File.Delete(secretPath);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var helperFolder = Path.Combine(".buildcharts", "plugins", Name, "vsts-npm-auth");
        var authenticator = new NpmrcAuthenticator(http, Environment.GetEnvironmentVariable, (registryConfiguration, cancellationToken) => VstsNpmAuthHelper.FetchCredentialsAsync(http, helperFolder, registryConfiguration, cancellationToken));
        var content = await authenticator.AuthenticateAsync(await File.ReadAllTextAsync(file.FullName, ct), ct);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        await using var output = new FileStream(secretPath, options);
        await output.WriteAsync(Encoding.UTF8.GetBytes(content), ct);
        Console.WriteLine("Prepared npmrc BuildKit secret.");
    }

    public Task OnAfterGenerateAsync(BuildConfig buildConfig, ChartConfig cartConfig, StringBuilder hclStringBuilder, CancellationToken ct)
    {
        BakeSecretHelper.AddSecrets(hclStringBuilder, "type=file,id=npmrc,src=.buildcharts/secrets/npmrc");
        return Task.CompletedTask;
    }
}