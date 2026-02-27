# dotnet-docker sample

This sample includes a minimal .NET app that is built into a Docker image.

## File structure
```text
.
├── build.yml            # Build metadata
├── build.ps1            # Generate + bake + run
├── charts/
│   └── buildcharts/
│       └── Chart.yaml   # Chart dependency data
└── src/
    ├── dotnet-docker.csproj
    └── Program.cs
```

## Run
```powershell
cd samples/dotnet-docker
./build.ps1
```
