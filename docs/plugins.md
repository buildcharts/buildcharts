# Plugin system

Plugins hook into the `buildcharts` generation pipeline to run custom logic before and/or after the build configuration is produced.  They are an extensibility point for tasks uch as injecting environment secrets, generating additional output files or performing validation.

## Using plugins in `build.yml`

To enable plugins for a particular repository, declare a top‑level `plugins` list in your `build.yml`. Each entry should be the name of a registered plugin. When you run `buildcharts generate`, these plugins will execute in sequence and can modify the generated HCL or write additional files.

For example, to enable the built‑in NuGet authentication plugin you would write:

```yaml
plugins:
  - NuGetAuthenticate@v1
```

You can combine multiple plugins by listing them one per line.  Plugins are executed in the order they appear in the list.

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
5. Patches the generated `buildcharts.hcl` so these secrets are mounted into the Docker build.

#### Usage

Add the plugin to your `build.yml` at the top level:

```yaml
# build.yml
version: v1beta

plugins:
  - NuGetAuthenticate@v1

environment:
  - VERSION
  - COMMIT

targets:
  # define your solution and project targets here
```

With this configuration in place, running `buildcharts generate` will automatically inject the necessary secrets and modifications so that dotnet restore and NuGet push operations can authenticate against Azure Artifacts feeds.

