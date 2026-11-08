using BuildCharts.Tool.Plugins.NuGetAuthenticate_v1.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NpmAuthenticate_v1.Helpers;

public static class NpmCredentialProviderHelper
{
    public static async Task<string> EnsureInstalledAsync(string folder, CancellationToken ct, string version = null)
    {
        await MicrosoftCredentialProviderHelper.EnsureInstalledAsync(folder, ct, version);
        return folder;
    }

    public static Task<string> FetchCredentialsAsync(string folder, Uri source, bool isRetry, CancellationToken ct)
    {
        return MicrosoftCredentialProviderHelper.FetchCredentialsAsync(folder, source, isRetry, ct);
    }
}
