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

## Local sign-in

When an Azure DevOps Services npm feed has no supplied credentials, the plugin uses the same cross-platform Microsoft Azure Artifacts Credential Provider as NuGet authentication. It downloads the provider under `.buildcharts/plugins/NpmAuthenticate@v1/microsoft-artifacts-credprovider` and runs it through `dotnet` on Windows, Linux and macOS.

The provider receives each npm registry URL and may prompt for browser or device-code sign-in. Its returned password is Base64-encoded into `username`, `_password` and `email` entries for both the feed's `/npm/registry/` and `/npm/` paths. The authenticated configuration is written only to the `npmrc` BuildKit secret. Project and user-level `.npmrc` files remain unchanged.

Set `NPM_AUTHENTICATE_HELPER_VERSION` to an exact Microsoft Credential Provider release version, for example `1.4.1`. Unset or empty selects the latest release. Different versions use separate installation directories.

Existing job tokens, service connections and working-file credentials take precedence. `TF_BUILD=true`, `GITHUB_ACTIONS=true`, and `CI=true` or `CI=1` disable local sign-in; missing Azure feed credentials fail generation in CI. Azure DevOps Server requires supplied credentials.

A fresh `buildcharts generate` removes `.buildcharts`, so the helper is downloaded again after that cleanup.

## Configuration

| Environment variable | Purpose |
| --- | --- |
| `NPM_AUTHENTICATE_HELPER_VERSION` | Exact Microsoft Credential Provider release version. Defaults to latest. |
| `NPM_AUTHENTICATE_WORKING_FILE` | The working `.npmrc` file. Defaults to `.npmrc` in the current directory. |
| `NPM_AUTHENTICATE_CUSTOM_ENDPOINTS` | Comma-separated npm service connection IDs. Equivalent to the task's `customEndpoint` selection. |
| `ENDPOINT_URL_<ID>` | Registry URL for a selected service connection. |
| `ENDPOINT_AUTH_<ID>` | Azure agent authorization JSON with `scheme` and `parameters`. Supports `Token` with `apitoken`, or `UsernamePassword` with `username` and `password`. |
| `SYSTEM_ACCESSTOKEN` | Job OAuth token for internal registries and packaging discovery. |
| `ENDPOINT_AUTH_SYSTEMVSSCONNECTION` | The agent's OAuth authorization JSON, used when `SYSTEM_ACCESSTOKEN` is absent. |
| `SYSTEM_TEAMFOUNDATIONCOLLECTIONURI` | Azure DevOps collection URL. `SYSTEM_COLLECTIONURI` is also accepted. |
| `SYSTEM_SERVERTYPE` | `Hosted` enables packaging service discovery; server installations use the collection origin. |

Endpoint environment names use uppercase IDs, with spaces and periods replaced by underscores. The caller must provide credentials for authorized service connections. A standalone CLI cannot declare Azure task inputs or request service-connection authorization; selecting an ID does not grant access or retrieve its secret from Azure DevOps. Outside Azure Pipelines, supply the same environment values or an already authenticated working file, or use the automatic local sign-in described above.

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
