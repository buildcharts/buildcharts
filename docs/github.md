# GitHub integration

This document describes how to use `buildcharts` to automate your .NET project workflows using **GitHub Actions**.

## Actions
- https://github.com/buildcharts/generate-action
- https://github.com/buildcharts/setup-action

## Usage

```yaml
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

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Run Docker Bake
        uses: docker/bake-action@v4
        with:
          source: .
          files: ./buildcharts.hcl
```

## Enable caching

Use Docker cache storage backend [GitHub Actions cache (gha)](https://docs.docker.com/build/cache/backends/gha/) for  efficient caching using [Cache Action](https://github.com/actions/cache).

```yaml
- name: Docker build and test
  uses: docker/bake-action@v6
  with:
    files: ./buildcharts.hcl
    set: |
      _common.cache-from=type=gha,scope=buildcharts
      _common.cache-to=type=gha,scope=buildcharts,mode=max
  env:
    VERSION: ${{ inputs.version }}
    COMMIT:  ${{ inputs.commit }}
```