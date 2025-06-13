# `buildcharts`

[![dotnet tool](https://img.shields.io/nuget/v/dotnet-buildcharts?color=brightgreen&label=dotnet-krp&logo=dotnet&logoColor=white)](https://www.nuget.org/packages/dotnet-krp)
[![docker](https://img.shields.io/docker/v/buildcharts/buildcharts?color=brightgreen&label=docker&logo=docker&logoColor=white)](https://hub.docker.com/r/eddietisma/krp)

`buildcharts` is a framework for defining and generating CI/CD pipelines using declarative metadata (`build.yml`) and templated pipeline definitions stored as OCI artifacts (similar to Helm charts). It enables scalable, centralized, and consistent build processes.

### Features
- **Scaling**: Handle 100+ microservices with shared pipeline logic.
- **Control**: Centralize logic or custom validation to all builds.
- **Extensibility**: You can easily extend with custom OCI images.
- **Charts**: Stored as OCI images in Docker Container Registry.
- **Pipeline Generation**: Pipelines are generated dynamically based on the metadata file and templates.
- **Artifact Outputs**: Supports Docker images, NuGet packages, test results, etc.

## **How `buildcharts` works**

### File structure

```
src/
├── .buildcharts/        # Created during generation, contains OCI images and build outputs
├── build.yml            # Build metadata
├── charts/
│   └── buildcharts/
│       └── Chart.yaml   # Chart dependency data
```

### Generation

1. Uses `build.yml` with project-specific metadata.

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

  - name: dotnet-docker
    alias: docker
    version: 0.0.1
    repository: oci://registry-1.docker.io/buildcharts

  - name: dotnet-nuget
    alias: nuget
    version: 0.0.1
    repository: oci://registry-1.docker.io/buildcharts

  - name: dotnet-test
    alias: test
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

      # - name: Set up BuildCharts
      #   uses: buildcharts/setup-action@v1
      
      # - name: Generate BuildCharts
      #   uses: buildcharts/generate-action@v1

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Docker build and test
        uses: docker/bake-action@v6
        with:
          source: . # TODO: Is this needed?
          files: buildcharts.hcl
          # Enable Docker Buildx caching
          set: |
            base.cache-from=type=gha,scope=buildcharts
            build.cache-to=type=gha,scope=buildcharts,mode=max
        env:
          VERSION: ${{ github.ref_name }}
          COMMIT: ${{ github.sha }}
```

### Results
- `buildcharts generate` outputs a Docker bake file `buildcharts.hcl`
  - Each `type: docker` will be resolved using chart `alias: docker`.
  - Each chart dockerfile will be included in the HCL.
- `docker buildx bake` executes the build.
  - Utilizing docker for isolated and reproducible builds.
  - Built-in support for caching, scaling, metadata, provenance, SBOM etc.

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
- https://docs.docker.com/build/bake/contexts/#deduplicate-context-transfer
- https://docs.docker.com/build/cache/backends/registry/
- https://docs.docker.com/guides/bake/#exporting-build-artifacts
- https://docs.docker.com/build/concepts/context/#git-repositories
- https://docs.docker.com/build/cache/backends/registry/
- https://github.com/docker/metadata-action?tab=readme-ov-file
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
```

## Documentation

- [docs/cli.md](docs/cli.md) – CLI Tool
- [docs/build.yml.md](docs/build.yml.md) – Metadata specification
- [docs/plugins.md](docs/plugins.md) – Plugin system
- [docs/github.md](docs/github.md) – GitHub integration
- [buildcharts/charts](https://github.com/buildcharts/charts/) – Repository with build charts
