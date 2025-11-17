# Plugin system

Plugins hook into the `buildcharts` generation pipeline to run custom logic before and/or after the build configuration is produced.  They are an extensibility point for tasks such as injecting environment secrets, generating additional output files or performing validation.

## Using plugins in `build.yml`

To enable plugins for a particular repository, declare a top‑level `plugins` list in your `build.yml`. Each entry should be the name of a registered plugin. When you run `buildcharts generate`, these plugins will execute in sequence and can modify the generated HCL or write additional files.

Example:

```yaml
plugins:
  - NuGetAuthenticate@v1
  - TestcontainersDinD@v1
```

Combine multiple plugins by listing them one per line.  Plugins are executed in the order they appear in the list.

## Built‑in plugins

The BuildCharts tool ships with a handful of built‑in plugins.  These plugins are maintained alongside the tool and require no additional installation.  You can also author our own plugins by implementing the `IBuildChartsPlugin` interface and registering them via the `PluginManager`.

### `NuGetAuthenticate@v1`

This plugin configures NuGet tools to authenticate with Azure Artifacts and other NuGet repositories. Similar to the [NuGetAuthenticate@1](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/nuget-authenticate-v1?view=azure-pipelines) in Azure Devops. 

#### Details

1. Scans the local `NuGet.Config` for package sources whose host ends with `pkgs.dev.azure.com` or `pkgs.visualstudio.com`.
2. Ensures the [Microsoft Artifacts Credential Provider](https://github.com/microsoft/artifacts-credprovider) is installed locally.
3. Authenticates to the first matching feed using the credential provider.
4. Writes two secret files under `.buildcharts/secrets`:
   - `VSS_NUGET_EXTERNAL_FEED_ENDPOINTS` – a JSON payload describing the endpoint(s) and associated credentials.
   - `VSS_NUGET_ACCESSTOKEN` – the access token itself.
5. Patches the generated `docker-bake.hcl` so these secrets are mounted into the Docker build.

#### Usage

```yaml
# build.yml
version: v1beta

plugins:
  - NuGetAuthenticate@v1

variables:
  - VERSION
  - COMMIT

targets:
  # define your solution and project targets here
```

With this configuration in place, running `buildcharts generate` will automatically inject the necessary secrets and modifications so that dotnet restore and NuGet push operations can authenticate against Azure Artifacts feeds.

### `TestcontainersDinD@v1`

This plugin provisions a dedicated Docker-in-Docker (DinD) daemon and configures testcontainers to connect to it during build time, providing an isolated Docker engine.

#### Details

1. Starts `buildcharts-dind` container using `docker:27-dind` image.
    - If container already exists, re-use it, stop/remove if the image differs.
    - Uses privileged, binding port `2375`, disabling TLS.
    - Optional `BUILDCHARTS_DIND_IMAGE` environment variable to override the Docker image tag.
2. Patches the generated `docker-bake.hcl` for `target "test"`:
    - `TESTCONTAINERS_HOST_OVERRIDE` – arg with DinD container IP.
    - `host.docker.internal` – extra hosts resolving to host gateway.

#### Usage

```yaml
# build.yml
version: v1beta

plugins:
  - TestcontainersDinD@v1

variables:
  - VERSION
  - COMMIT

targets:
  test/BuildCharts.Tests/BuildCharts.Tests.csproj:
    type: test
  # define your solution and project targets here
```

When running `buildcharts generate`, the plugin will start (or reuse) the DinD container, update `.buildcharts/docker-bake.hcl`, and leave the Docker daemon running (memory footpint ~40mb).
