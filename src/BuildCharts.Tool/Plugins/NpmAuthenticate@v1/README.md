# NpmAuthenticate

Enable npm authentication in `build.yml`:

```yaml
plugins:
  - NpmAuthenticate@v1
```

The plugin follows the credential selection and formatting rules of Azure DevOps [`npmAuthenticate@0`](https://github.com/microsoft/azure-pipelines-tasks/tree/master/Tasks/NpmAuthenticateV0):

- Read default and scoped registries from the selected `.npmrc`.
- Discover internal registry origins through the Azure DevOps npm resource area and packaging access mappings. Azure DevOps Server uses the collection origin.
- Prefer matching npm service connections over the job OAuth token. Match the origin and complete registry path, with normalized trailing slashes.
- Use `_authToken` for internal-feed OAuth and external npm tokens. Probe token-based service connections without credentials; Azure DevOps response headers select `VssToken` and a base64-encoded `_password` for Azure DevOps PATs.
- Use the service connection username, base64-encoded password and email for `UsernamePassword` connections.
- Replace credentials only for registries receiving new authentication. Preserve unrelated configuration and existing credentials for other registries.

The BuildCharts lifecycle differs from the Azure task. The plugin leaves the source file unchanged and writes `.buildcharts/secrets/npmrc`, then adds it to the generated Bake secrets as `npmrc`. There is no source-file restoration step. The generated secret must remain available until Bake finishes; the next generation removes it. Do not publish `.buildcharts/secrets` as an artifact.

## Local Windows sign-in

When an Azure DevOps Services npm feed has no supplied credentials, the plugin downloads Microsoft's `vsts-npm-auth` **0.43.0** directly from `https://registry.npmjs.org/vsts-npm-auth/-/vsts-npm-auth-0.43.0.tgz`. It extracts the complete package under:

```text
.buildcharts/plugins/NpmAuthenticate@v1/vsts-npm-auth/0.43.0/package/bin/
```

Set `NPM_AUTHENTICATE_HELPER_VERSION` to download another exact version. The download and installation directory use that version. Unset or empty uses the default `0.43.0`.

```powershell
$env:NPM_AUTHENTICATE_HELPER_VERSION = '0.43.0'
```

It runs `vsts-npm-auth.exe` directly. Neither npm nor a global helper installation is required for authentication. The helper may prompt for Windows sign-in and requests its standard read-write package token.

The plugin derives the feed URLs from the project's selected `.npmrc` on each generation. It supplies only Azure feeds lacking credentials to the helper, using temporary `-Config` and `-TargetConfig` files under `.buildcharts`. The project and user-level `.npmrc` files are not modified or synchronized. Temporary helper files are removed after the call, and the resulting credentials go into the existing `npmrc` BuildKit secret.

Existing job tokens, service connections and credentials in the working file take precedence. `TF_BUILD=true`, `GITHUB_ACTIONS=true`, and `CI=true` or `CI=1` disable local sign-in; missing Azure feed credentials fail generation in CI. The helper supports Windows and Azure DevOps Services only. Other platforms and Azure DevOps Server continue using supplied credentials.

As with the NuGet helper, a fresh `buildcharts generate` removes `.buildcharts`, so the helper is downloaded again after that cleanup. Within an existing generation directory, the selected version is reused.

## Configuration

| Environment variable | Purpose |
| --- | --- |
| `NPM_AUTHENTICATE_HELPER_VERSION` | Exact helper version to download. Defaults to `0.43.0`. |
| `NPM_AUTHENTICATE_WORKING_FILE` | The working `.npmrc` file. Defaults to `.npmrc` in the current directory. |
| `NPM_AUTHENTICATE_CUSTOM_ENDPOINTS` | Comma-separated npm service connection IDs. Equivalent to the task's `customEndpoint` selection. |
| `ENDPOINT_URL_<ID>` | Registry URL for a selected service connection. |
| `ENDPOINT_AUTH_<ID>` | Azure agent authorization JSON with `scheme` and `parameters`. Supports `Token` with `apitoken`, or `UsernamePassword` with `username` and `password`. |
| `SYSTEM_ACCESSTOKEN` | Job OAuth token for internal registries and packaging discovery. |
| `ENDPOINT_AUTH_SYSTEMVSSCONNECTION` | The agent's OAuth authorization JSON, used when `SYSTEM_ACCESSTOKEN` is absent. |
| `SYSTEM_TEAMFOUNDATIONCOLLECTIONURI` | Azure DevOps collection URL. `SYSTEM_COLLECTIONURI` is also accepted. |
| `SYSTEM_SERVERTYPE` | `Hosted` enables packaging service discovery; server installations use the collection origin. |

Endpoint environment names use uppercase IDs, with spaces and periods replaced by underscores. The caller must provide credentials for authorized service connections. A standalone CLI cannot declare Azure task inputs or request service-connection authorization; selecting an ID does not grant access or retrieve its secret from Azure DevOps. Outside Azure Pipelines, supply the same environment values or an already authenticated working file, or use the automatic Windows sign-in described above.

For internal feeds in Azure Pipelines:

```yaml
- bash: |
    dotnet buildcharts generate
    docker buildx bake --file .buildcharts/docker-bake.hcl
  env:
    SYSTEM_ACCESSTOKEN: $(System.AccessToken)
    SYSTEM_TEAMFOUNDATIONCOLLECTIONURI: $(System.TeamFoundationCollectionUri)
    SYSTEM_SERVERTYPE: $(System.ServerType)
```

The job identity needs access to the feed. Authentication does not change feed permissions.

## Dockerfile

Keep `.buildcharts` and the source `.npmrc` out of the Docker build context. Mount the authenticated file at the project's `.npmrc` path so project credentials cannot override it:

```dockerfile
WORKDIR /app
COPY package.json package-lock.json ./
RUN --mount=type=secret,id=npmrc,target=/app/.npmrc,required=true \
    --mount=type=cache,target=/root/.npm \
    npm ci
```

The generated HCL contains the secret file path, never the credentials. npm and NuGet authentication can be enabled together in either plugin order.
