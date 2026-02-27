# dotnet-testcontainers sample

This sample includes one unit test using Testcontainers.

When running `buildcharts generate` the `TestcontainersDinD@v1` plugin will spin up a DinD (Docker-in-Docker) daemon a container named `buildcharts-dind`.

## File structure
```text
.
├── build.yml            # Build metadata
├── build.ps1            # Generate + bake + parse TRX
├── charts/
│   └── buildcharts/
│       └── Chart.yaml   # Chart dependency data
└── src/
    ├── dotnet-testcontainers.csproj
    └── NginxContainerTests.cs
```

## Run
```powershell
cd samples/dotnet-testcontainers
./build.ps1
```

## Verify DinD activity
You can inspect Docker activity inside the DinD container while the build/test is running:

```powershell
docker exec -it buildcharts-dind watch docker ps
```
