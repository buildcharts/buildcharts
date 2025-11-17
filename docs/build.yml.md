# `build.yml` metadata specification

This document describes the structure of `build.yml`, which controls how `buildcharts` generates build pipelines.

## Top-level fields

### `version`
The metadata version. `latest` is accepted.

### `variables`
A list of globa variables exposed to pipeline execution. Each item may be just the variable name or `NAME=value` to provide a default.

```
variables:
  - VERSION
  - COMMIT
```

2. **Sequence with inline values**

```yaml
variables:
  - VERSION: "1.0.0-local"
  - IMAGE: "mcr.microsoft.com/dotnet/aspnet:10.0"
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
    base: mcr.microsoft.com/dotnet/sdk:10.0
```

2. **List of entries**

```yaml
src/Project/Project.csproj:
  - type: test
  - type: docker
    with:
      base: mcr.microsoft.com/dotnet/aspnet:10.0
      tags: ["docker.io/username/project:${VERSION}-${COMMIT}"]
```

3. **Shorthand array of types**

```yaml
src/Project/Project.csproj:
  type: [nuget, test]
```

This is equivalent to the list of entries syntax and will be normalized internally.

#### Target properties
- `type` – The build chart alias to use. Common values include `build`, `test`, `nuget`, and `docker`. 
- `with` – Optional dictionary passed through to the chart templates. Typical keys are `base` for the base Docker image and `tags` for Docker image tags.

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
      base: mcr.microsoft.com/dotnet/sdk:10.0

  src/BuildCharts.Tool/BuildCharts.Tool.csproj:
    - type: nuget
    - type: docker
      with:
        base: mcr.microsoft.com/dotnet/aspnet:10.0
        tags: ["docker.io/buildcharts/buildcharts:${VERSION}-${COMMIT}"]
```

Use `buildcharts generate` to produce `docker-bake.hcl` based on this metadata.
