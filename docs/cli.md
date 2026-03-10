# Command-line interface
Supports dynamic generation at runtime (e.g., pipeline generates new pipelines via API or buildx bake).

```bash
buildcharts
  init         # scaffold
  update       # resolve chart dependencies + update lock
  generate     # render CI/CD pipelines
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
Pulled: docker.io/buildcharts/dotnet-build:0.0.1 (1123 bytes)
Digest: sha256:4da50de6250055a119d51c620e2ed825529d281b2d27a9e2bb1f17b912d1a11c
```

```console
# buildcharts pull oci://registry-1.docker.io/buildcharts/dotnet-build@sha256:4da50de6250055a119d51c620e2ed825529d281b2d27a9e2bb1f17b912d1a11c
Pulled: registry-1.docker.io/buildcharts/dotnet-build@sha256:4da50de6250055a119d51c620e2ed825529d281b2d27a9e2bb1f17b912d1a11c (1123 bytes)
Digest: sha256:4da50de6250055a119d51c620e2ed825529d281b2d27a9e2bb1f17b912d1a11c
```

### `buildcharts generate`

Generates build pipeline using metadata. Outputs a Docker bake file `.buildcharts/docker-bake.hcl`. It also validates `Chart.yaml` digests by comparing them to chart tags, and cleans the `.buildcharts` folder to keep a clean state.

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
   • docker-bake.hcl
```

### `buildcharts update`

Resolves chart dependencies from `charts/buildcharts/Chart.yaml` and updates `charts/buildcharts/Chart.lock`.

```console
# buildcharts update
Updating 4 dependencies...
Pulled: registry-1.docker.io/buildcharts/dotnet-build:0.0.1 (1111 bytes) (cached)
Digest: sha256:ca7e6c16d053721518ebf6186c5c0663ed870c14c2eda6b0f62588b49b2a1ab6 (cached)
Pulled: registry-1.docker.io/buildcharts/dotnet-test:0.0.1 (900 bytes) (cached)
Digest: sha256:ab2b1d00fbc03f0300d2b10a78ed60ee8615e6bcafc60222083e77ce572583a9 (cached)
Pulled: registry-1.docker.io/buildcharts/dotnet-nuget:0.0.1 (1853 bytes) (cached)
Digest: sha256:bab5a1e71c486731e152c55e2aa54eb045921f865441ac948ef8a572346ae21e (cached)
Pulled: registry-1.docker.io/buildcharts/dotnet-docker:0.0.2 (752 bytes) (cached)
Digest: sha256:978e3277b9618f6a0a56978b96d0b3fd23246cd77b972b66820c53e145c42de4 (cached)

✅ Generated files:
   • charts/buildcharts/Chart.lock
```

### `buildcharts version`

Prints the `buildcharts` CLI version.

```console
# buildcharts version
buildcharts
 version:       1.0.0+a882a7c19eb72f60a9cf1da3a4aee00691bdc4ba
 built:         2026-01-09T01:55:30Z
 os/arch:       Microsoft Windows 10.0.26200/x64
 cpu/mem:       32 cores/127.65 GB
 .NET version:  10.0.1
```

### `buildcharts summary`

Generates a `SUMMARY.md` file from the latest `docker buildx` history logs.

```console
# buildcharts summary
Generating summary for build: <id> (<job>)

✅ Generated files:
   • SUMMARY.md
```
