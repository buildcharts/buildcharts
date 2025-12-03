# `buildcharts`

[![dotnet tool](https://img.shields.io/nuget/v/dotnet-buildcharts?color=brightgreen&label=dotnet-buildcharts&logo=dotnet&logoColor=white)](https://www.nuget.org/packages/dotnet-buildcharts)
[![docker](https://img.shields.io/docker/v/buildcharts/buildcharts?color=brightgreen&label=docker&logo=docker&logoColor=white)](https://hub.docker.com/r/buildcharts/buildcharts)

`buildcharts` is a framework for defining and generating CI/CD pipelines using declarative metadata (`build.yml`) and templated pipeline definitions stored as OCI artifacts (similar to Helm charts). Enabling scalable, centralized, and consistent build processes.

### Features
- **Scaling**: Handle 100+ microservices with shared pipeline logic.
- **Control**: Centralize logic or custom validation to all builds.
- **Extensibility**: You can easily extend with custom OCI images.
- **Charts**: Stored as OCI images in Docker Container Registry.
- **Pipeline Generation**: Pipelines are generated dynamically based on the metadata file and templates.
- **Artifact Outputs**: Supports Docker images, NuGet packages, test results, etc.

### Prerequisites
- Docker (**20.10+**) or Docker Desktop (**4.38+**) must be installed to support running builds with `docker buildx bake`.

## **How `buildcharts` works**

### File structure

```
src/
├── build.yml            # Build metadata
├── charts/
│   └── buildcharts/
│       └── Chart.yaml   # Chart dependency data
```

### Generation

1. Uses `build.yml` with project-specific metadata.

```yaml 
version: v1beta

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

  test/BuildCharts.Tests/BuildCharts.Tests.csproj:
    type: test

  test/BuildCharts.Tests1/BuildCharts.Tests1.csproj:
    type: test

```

2. Uses `charts/buildcharts/Chart.yaml` with chart dependency data.

```yaml 
apiVersion: v2
name: buildcharts
version: 0.0.1
description: A meta-chart to define build pipeline targets and templates
type: application

dependencies:
  - name: dotnet-build
    alias: build
    version: 0.0.1
    repository: oci://registry-1.docker.io/buildcharts

  - name: dotnet-test
    alias: test
    version: 0.0.1
    repository: oci://registry-1.docker.io/buildcharts

  - name: dotnet-nuget
    alias: nuget
    version: 0.0.1
    repository: oci://registry-1.docker.io/buildcharts

  - name: dotnet-docker
    alias: docker
    version: 0.0.1
    repository: oci://registry-1.docker.io/buildcharts
```
 
3. Run `buildcharts generate` CLI to generate build using metadata and OCI charts from pipeline.

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Set up BuildCharts
        uses: buildcharts/setup-action@v1
      
      - name: Generate BuildCharts
        uses: buildcharts/generate-action@v1

      - name: Setup Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Docker build and test
        uses: docker/bake-action@v6
        with:
          source: .
          files: .buildcharts/docker-bake.hcl
        env:
          VERSION: ${{ github.ref_name }}
          COMMIT: ${{ github.sha }}
```

### Results
- Running `buildcharts generate` inspects build metadata in `build.yml` and produces a Docker Bake configuration file: `docker-bake.hcl`.
  - Each entry with `type: docker` is resolved using the chart identified by `alias: docker`.
  - Dockerfiles defined within each chart are referenced directly in the generated Bake configuration.
- Execute the build using `docker buildx bake`.
  - Builds run inside Docker, ensuring isolated and reproducible environments.
  - Uses **high-level builds** with [Buildx Bake](https://docs.docker.com/build/bake/) configurations using BuildKit from the [Moby project](https://github.com/moby/moby).
  - Built-in features such as caching, scalability, metadata, provenance, Software Bill of Materials (SBOM).
  
Templates support:
- 🐳 Docker image builds
- 🧪 Test runners
- 📦 NuGet packaging
- 📂 Artifact collection
- 🧰 Custom plugins (versioning, summaries, contributors, etc.)

## Links
- https://github.com/dotnet/arcade
- https://github.com/facebook/buck
- https://github.com/twitter/pants
- https://nebula-plugins.github.io
- https://kustomizer.dev
- https://kustomizer.dev/guides/fluxcd
- https://docs.docker.com/reference/dockerfile/#onbuild
- https://docs.docker.com/build/bake/reference#target
- https://docs.docker.com/build/bake/contexts/#deduplicate-context-transfer
- https://docs.docker.com/build/cache/backends/registry/
- https://docs.docker.com/build/concepts/context/#git-repositories
- https://docs.docker.com/build/cache/backends/registry/
- https://docs.docker.com/guides/bake/#exporting-build-artifacts
- https://github.com/docker/buildx/issues/1991
- https://github.com/docker/metadata-action?tab=readme-ov-file
- https://github.com/microsoft/azure-pipelines-tasks/blob/master/docs/authoring/commands.md
- https://github.com/moby/buildkit/blob/master/.github/workflows/buildkit.yml

## CLI

Read the [CLI documentation](/docs/cli.md) for more info.

```bash
buildcharts
  init         # scaffold
  update       # resolve templates + create lock
  generate     # render CI/CD pipelines
  run          # trigger or test the pipelines
  diff         # show changes vs lock
  validate     # schema and template check
  clean        # remove generated artifacts
  version      # version info
  package      # package chart
  pull         # pull chart 
  summary      # summarize build logs
```

## Documentation

- [docs/cli.md](docs/cli.md) – CLI Tool
- [docs/build.yml.md](docs/build.yml.md) – Metadata specification
- [docs/plugins.md](docs/plugins.md) – Plugin system
- [docs/github.md](docs/github.md) – GitHub integration
- [buildcharts/charts](https://github.com/buildcharts/charts/) – Collection of build charts