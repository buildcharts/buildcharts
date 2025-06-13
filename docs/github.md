# Integration with GitHub

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

      - name: Setup Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Run Docker Bake
        uses: docker/bake-action@v4
        with:
          files: ./buildcharts.hcl
```

## Enable caching

```yaml
- name: Docker build and test
  uses: docker/bake-action@v6
  with:
    files: ./buildcharts.hcl
  env:
    VERSION: ${{ inputs.version }}
    COMMIT:  ${{ inputs.commit }}
  with:
    set: |
      base.cache-from=type=gha,scope=buildcharts
      build.cache-to=type=gha,scope=buildcharts,mode=max
```