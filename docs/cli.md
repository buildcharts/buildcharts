# Command-line interface
Supports dynamic generation at runtime (e.g., pipeline generates new pipelines via API or buildx bake).

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

## Usage

### `buildcharts init`

Scaffolds the working directory and automatically creates:
- `build.yml`
- `charts/buildcharts/Chart.yaml`

> [!NOTE]
> Currently only supports .NET


```console
# buildcharts init
buildcharts initialized

✅ Generated files:
   • build.yml
   • charts/buildcharts/Chart.yaml

✅ Targets:
   • buildcharts.sln → build
   • src/BuildCharts.Tool/BuildCharts.Tool.csproj → nuget

✅ Detected GitHub from .git folder:
   • .github/workflows/buildcharts.yml

👉 Next steps:
   • Edit `build.yml` to customize build pipeline
   • Run `buildcharts generate` to generate build pipeline
   • Run `docker buildx bake` to run build pipeline

💡 Tips:
   • Run `buildcharts update` to auto-sync chart dependencies
   • Customize default base images and tags in `charts/buildcharts/Chart.yaml`
```

### `buildcharts pull`

Pulls the OCI chart from the container registry. By default, it leverages Docker's authentication mechanism, utilizing existing credentials stored in `~/.docker/config.json`.

```console
# buildcharts pull oci://docker.io/buildcharts/dotnet-build:0.0.1                    
Pulled: docker.io/buildcharts/dotnet-build:0.0.1 (582 bytes)
Digest: sha256:f8fa3e928f25cc651f541a408222978941cde466beaaae7e60be6b5b1ca02ff9
```

### `buildcharts generate`

Generates build pipeline using metadata. Outputs a Docker bake file `buildcharts.hcl`.

```console
# buildcharts generate
Pulling charts...
Pulled: registry-1.docker.io/buildcharts/dotnet-test:0.0.1 (581 bytes)
Digest: sha256:e2fc7641da11faa2f90d2a4991fa8c37e97a0825988f1d4352758da4bc5dd587
Pulled: registry-1.docker.io/buildcharts/dotnet-docker:0.0.2 (583 bytes)
Digest: sha256:d3a3957520bff850383d6d79b692888595f437c872c2f860d75544751813ddde
Pulled: registry-1.docker.io/buildcharts/dotnet-build:0.0.1 (582 bytes)
Digest: sha256:aca33142a81a9e79d584de7882a740240163e27411ad0f4ebe0336ed2de0cb4e
Pulled: registry-1.docker.io/buildcharts/dotnet-nuget:0.0.1 (582 bytes)
Digest: sha256:6b6b99dd94c8b9f388890770fc3f1249c07561c9347d9eb98802c3bf44fbf47a

✅ Generated files:
   • buildcharts.hcl
```

### `buildcharts run`

Triggers docker buildx bake on the generated `buildcharts.hcl`.

 - `$env:VERSION="1.2.3"; $env:COMMIT="abc123"; docker buildx bake`

### `buildcharts summary`

Generates a `SUMMARY.md` file from the latest `docker buildx` history logs.

```console
# buildcharts summary
Generating summary for build: <id> (<job>)

✅ Generated files:
   • SUMMARY.md
```