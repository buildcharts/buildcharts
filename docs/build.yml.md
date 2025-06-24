# `build.yml` metadata specification

This document describes the structure of `build.yml`, which controls how `buildcharts` generates build pipelines.

## Top-level fields

### `version`
The metadata version. `latest` is accepted.

### `environment`
A list of environment variables exposed to pipeline execution. Each item may be just the variable name or `NAME=value` to provide a default.

```
environment:
  - VERSION
  - COMMIT
  - IMAGE=mcr.microsoft.com/dotnet/aspnet:9.0
```

### `targets`
Mapping of project or solution files to one or more target definitions. The key is the path to a `.sln` or `.csproj` file. Each value defines the build targets for that source.

A target can be specified in two ways:

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

#### Target properties
- `type` – The build chart alias to use. Common values include `build`, `test`, `nuget`, and `docker`.
- `with` – Optional dictionary passed through to the chart templates. Typical keys are `base` for the base Docker image and `tags` for Docker image tags.

## Example

```yaml
version: latest

environment:
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
```

Use `buildcharts generate` to produce `buildcharts.hcl` based on this metadata.