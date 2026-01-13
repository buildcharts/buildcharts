# `build.yml` metadata specification

This document describes the structure of `build.yml`, which controls how `buildcharts` generates build pipelines.

## Top-level fields

### `version`
The metadata version. `latest` is accepted.

### `variables`
A list of global variables exposed to pipeline execution. 

A variable can be defined in different ways:

1. **Sequence of names**

```yaml
variables:
  - VERSION
  - COMMIT
```

2. **Sequence with inline values**

```yaml
variables:
  - VERSION: "1.0.0-local"
  - IMAGE: "mcr.microsoft.com/dotnet/aspnet:9.0"
```

3. **Sequence with default blocks**

```yaml
variables:
  - VERSION:
      default: "1.0.0-local"
  - COMMIT: ""
```

4. **Mapping form**

```yaml
variables:
  VERSION: "1.0.0-local"
  COMMIT: ""
```

### `targets`
Mapping of project or solution files to one or more target definitions. The key is the path to a `.sln` or `.csproj` file. Each value defines the build targets for that source.

A target can be specified in different ways:

1. **Single entry**

```yaml
src/Project/Project.csproj:
  type: nuget
  with:
    base: mcr.microsoft.com/dotnet/sdk:9.0
```

2. **List of entries**

```yaml
src/Project/Project.csproj:
  - type: test
  - type: docker
    with:
      base: mcr.microsoft.com/dotnet/aspnet:9.0
      tags: ["docker.io/username/project:${VERSION}-${COMMIT}"]
```

3. **Shorthand array of types**

```yaml
src/Project/Project.csproj:
  type: [nuget, test]
```

This is equivalent to the list of entries syntax and will be normalized internally.

#### Target properties
- `type` - The build chart alias to use. Common values include `build`, `test`, `nuget`, and `docker`. 
- `with` - Optional dictionary passed through to the chart templates.
  - `base` - Base image used for the chart's `base` context.
  - `tags` - Docker image tags (array of strings), used by `docker` targets.
  - `dockerfile` - Override the Dockerfile path for the chart (defaults to `./.buildcharts/<chart>/Dockerfile`).
  - `allow` - BuildKit entitlements (array of strings) forwarded to bake, e.g. `network.host` or `security.insecure`.
  - `args` - Build args map passed to the chart as build arguments.

## Example

```yaml
version: latest

variables:
  - VERSION
  - COMMIT

targets:
  buildcharts.sln:
    type: build
    with:
      base: mcr.microsoft.com/dotnet/sdk:9.0

  src/BuildCharts.Tool/BuildCharts.Tool.csproj:
    - type: nuget
    - type: docker
      with:
        base: mcr.microsoft.com/dotnet/aspnet:9.0
        tags: ["docker.io/buildcharts/buildcharts:${VERSION}-${COMMIT}"]
        dockerfile: ./Dockerfile.aks
        allow: ["network.host"]
        args:
          RUNTIME: "linux-x64"
```

Use `buildcharts generate` to produce `docker-bake.hcl` based on this metadata.
