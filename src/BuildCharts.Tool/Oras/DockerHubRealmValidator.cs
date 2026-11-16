using OrasProject.Oras.Registry.Remote.Auth;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Oras;

public sealed class DockerHubRealmValidator : IRealmValidator
{
    private readonly DefaultRealmValidator _defaultValidator = new();
    private readonly DefaultRealmValidator _dockerHubValidator = new()
    {
        TrustedRealmHosts = new HashSet<string> { "auth.docker.io" },
    };

    public Task<bool> IsRealmAllowedAsync(Uri registryUri, Uri realmUri, CancellationToken cancellationToken = default)
    {
        var isDockerHub = registryUri.Scheme == Uri.UriSchemeHttps && registryUri.IsDefaultPort &&
            registryUri.Host.TrimEnd('.') is "registry-1.docker.io" or "docker.io" or "index.docker.io";
        var validator = isDockerHub ? _dockerHubValidator : _defaultValidator;
        return validator.IsRealmAllowedAsync(registryUri, realmUri, cancellationToken);
    }
}