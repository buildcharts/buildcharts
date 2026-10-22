using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Plugins.NpmAuthenticate_v1;

public class NpmrcAuthenticator(HttpClient http, Func<string, string> getEnvironmentVariable)
{
    public async Task<string> AuthenticateAsync(string content, CancellationToken ct)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        var registries = new Dictionary<string, Uri>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }
            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }
            var key = line[..separator].Trim();
            if (!key[(key.IndexOf(':') + 1)..].Equals("registry", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var value = Regex.Match(line[(separator + 1)..].Trim(), "^(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^;#]*))").Groups["value"].Value.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var registry) || registry.Scheme is not ("http" or "https") || registry.UserInfo.Length > 0)
            {
                throw new InvalidOperationException("An npm registry must be an absolute HTTP or HTTPS URL without embedded credentials.");
            }
            var normalized = new UriBuilder(registry) { Path = registry.AbsolutePath.TrimEnd('/') + "/" }.Uri;
            registries[key] = normalized;
            lines[i] = $"{key}={normalized.AbsoluteUri}";
        }

        if (registries.Values.GroupBy(x => x.Authority, StringComparer.OrdinalIgnoreCase).Any(g => g.Select(x => x.Scheme).Distinct().Count() > 1))
        {
            throw new InvalidOperationException("npm credentials cannot be scoped by scheme. Do not mix HTTP and HTTPS registries on the same host and port.");
        }

        var token = getEnvironmentVariable("SYSTEM_ACCESSTOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            var systemAuth = getEnvironmentVariable("ENDPOINT_AUTH_SYSTEMVSSCONNECTION");
            if (!string.IsNullOrWhiteSpace(systemAuth))
            {
                using var auth = ParseAuthorization(systemAuth);
                if (!GetProperty(auth.RootElement, "scheme").GetString().Equals("OAuth", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("SYSTEMVSSCONNECTION must use OAuth authentication.");
                }
                token = GetProperty(GetProperty(auth.RootElement, "parameters"), "AccessToken").GetString();
            }
        }

        var origins = await GetPackagingOriginsAsync(token, ct);
        var endpoints = new List<(Uri Registry, string Credentials)>();
        var endpointIds = (getEnvironmentVariable("NPM_AUTHENTICATE_CUSTOM_ENDPOINTS") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var id in endpointIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var suffix = id.Replace('.', '_').Replace(' ', '_').ToUpperInvariant();
            if (!Uri.TryCreate(getEnvironmentVariable($"ENDPOINT_URL_{suffix}"), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length > 0)
            {
                throw new InvalidOperationException("An npm service connection URL is missing or invalid.");
            }
            using var authorization = ParseAuthorization(getEnvironmentVariable($"ENDPOINT_AUTH_{suffix}"));
            var scheme = GetProperty(authorization.RootElement, "scheme").GetString();
            var parameters = GetProperty(authorization.RootElement, "parameters");
            string credentials;
            if (scheme.Equals("UsernamePassword", StringComparison.OrdinalIgnoreCase))
            {
                var username = GetProperty(parameters, "username").GetString();
                credentials = FormatBasic(endpoint, username, GetProperty(parameters, "password").GetString(), username);
            }
            else if (scheme.Equals("Token", StringComparison.OrdinalIgnoreCase))
            {
                var apiToken = GetProperty(parameters, "apitoken").GetString();
                credentials = await IsAzureDevOpsEndpointAsync(endpoint, ct)
                    ? FormatBasic(endpoint, "VssToken", apiToken, "VssEmail")
                    : $"{Nerf(endpoint)}:_authToken={CredentialValue(apiToken)}\n{Nerf(endpoint)}:always-auth=true";
            }
            else
            {
                throw new InvalidOperationException("Unsupported npm service connection authentication. Use Token or UsernamePassword.");
            }
            endpoints.Add((endpoint, credentials));
        }

        foreach (var registry in registries.Values.DistinctBy(x => Origin(x) + Nerf(x)))
        {
            var credentials = endpoints.FirstOrDefault(x => Origin(x.Registry) == Origin(registry) && Nerf(x.Registry) == Nerf(registry)).Credentials;
            if (credentials == null && origins.Contains(Origin(registry)))
            {
                credentials = $"{Nerf(registry)}:_authToken={CredentialValue(token)}";
            }
            if (credentials == null)
            {
                continue;
            }
            var prefix = Nerf(registry) + ":";
            lines.RemoveAll(line => line.TrimStart().StartsWith(prefix, StringComparison.Ordinal)
                && Regex.IsMatch(line.TrimStart()[prefix.Length..], @"^(?:_authToken|_auth|_password|username|email|always-auth)\s*=", RegexOptions.IgnoreCase));
            lines.Add(credentials);
        }
        return string.Join('\n', lines).TrimEnd('\n') + "\n";
    }

    private async Task<HashSet<string>> GetPackagingOriginsAsync(string token, CancellationToken ct)
    {
        var origins = new HashSet<string>(StringComparer.Ordinal);
        var collection = getEnvironmentVariable("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI") ?? getEnvironmentVariable("SYSTEM_COLLECTIONURI");
        if (string.IsNullOrWhiteSpace(collection))
        {
            return origins;
        }
        if (!Uri.TryCreate(collection.TrimEnd('/') + "/", UriKind.Absolute, out var collectionUri) || collectionUri.Scheme is not ("http" or "https") || collectionUri.UserInfo.Length > 0)
        {
            throw new InvalidOperationException("The Azure DevOps collection URL is invalid.");
        }
        origins.Add(Origin(collectionUri));
        if (!string.Equals(getEnvironmentVariable("SYSTEM_SERVERTYPE"), "Hosted", StringComparison.OrdinalIgnoreCase))
        {
            return origins;
        }
        using var area = await GetJsonAsync(new Uri(collectionUri, "_apis/resourceAreas/4c83cfc1-f33a-477e-a789-29d38ffca52e?api-version=7.1-preview.1"), token, ct);
        var serviceUri = new Uri(GetProperty(area.RootElement, "locationUrl").GetString().TrimEnd('/') + "/");
        using var connection = await GetJsonAsync(new Uri(serviceUri, "_apis/connectionData?connectOptions=1"), token, ct);
        var mappings = GetProperty(GetProperty(connection.RootElement, "locationServiceData"), "accessMappings");
        foreach (var mapping in mappings.EnumerateArray())
        {
            origins.Add(Origin(new Uri(GetProperty(mapping, "accessPoint").GetString())));
        }
        return origins;
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("vsts:" + CredentialValue(token))));
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        try
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Azure DevOps returned invalid packaging location data.");
        }
    }

    private async Task<bool> IsAzureDevOpsEndpointAsync(Uri endpoint, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("X-TFS-FedAuthRedirect", "Suppress");
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.Headers.Concat(response.Content.Headers).Any(x => x.Key.Contains("x-tfs", StringComparison.OrdinalIgnoreCase) || x.Key.Contains("x-vss", StringComparison.OrdinalIgnoreCase));
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string FormatBasic(Uri registry, string username, string password, string email)
    {
        var prefix = Nerf(registry);
        return $"{prefix}:username={CredentialValue(username)}\n{prefix}:_password={Convert.ToBase64String(Encoding.UTF8.GetBytes(CredentialValue(password)))}\n{prefix}:email={CredentialValue(email)}\n{prefix}:always-auth=true";
    }

    private static JsonElement GetProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }
        throw new InvalidOperationException("Required npm authentication or packaging location data is missing.");
    }

    private static JsonDocument ParseAuthorization(string json)
    {
        try
        {
            return JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("An npm service connection authorization is missing or invalid.");
        }
    }

    private static string CredentialValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains('\r') || value.Contains('\n'))
        {
            throw new InvalidOperationException("An npm credential is missing or contains a line break.");
        }
        return value;
    }

    private static string Nerf(Uri uri) => "//" + uri.Authority.ToLowerInvariant() + uri.AbsolutePath.TrimEnd('/') + "/";
    private static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
}