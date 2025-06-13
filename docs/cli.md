# CLI
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

Pulls the OCI chart from the container registry.

```console
# buildcharts pull oci://docker.io/buildcharts/dotnet-build:0.0.1                    
Pulled: docker.io/buildcharts/dotnet-build:0.0.1 (582 bytes)
Digest: sha256:f8fa3e928f25cc651f541a408222978941cde466beaaae7e60be6b5b1ca02ff9
```

### `buildcharts generate`

Generates build pipeline using metadata. Outputs a Docker bake file `buildcharts.hcl`.

```console
# buildcharts generate
Pulled: registry-1.docker.io/buildcharts/dotnet-build:0.0.1 (582 bytes)
Digest: sha256:f8fa3e928f25cc651f541a408222978941cde466beaaae7e60be6b5b1ca02ff9
Pulled: registry-1.docker.io/buildcharts/dotnet-test:0.0.1 (581 bytes)
Digest: sha256:d119b77008ac9c37445dd312004f4c0f87f0238dc9bff9e9b952f751b982eeed
Pulled: registry-1.docker.io/buildcharts/dotnet-nuget:0.0.1 (582 bytes)
Digest: sha256:13177b860402678c9f24955aad4ab646bf20f14bafee346acc7c8e7fb51fcb8a
Pulled: registry-1.docker.io/buildcharts/dotnet-docker:0.0.1 (583 bytes)
Digest: sha256:86c21c8028bdc3a3fe27422b8812426c32c30ef08c4747bfcf85c0ed33ca3676
Generated docker-bake.hcl
```

### `buildcharts run`

Triggers docker buildx bake on the generated `buildcharts.hcl`.

 - `$env:VERSION="1.2.3"; $env:COMMIT="abc123"; docker buildx bake`