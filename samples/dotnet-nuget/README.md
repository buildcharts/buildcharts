# dotnet-nuget sample

This sample includes a minimal .NET class library that is packaged as a NuGet package.

## File structure
```text
.
├── build.yml            # Build metadata
├── build.ps1            # Generate + bake + list nupkg output
├── charts/
│   └── buildcharts/
│       └── Chart.yaml   # Chart dependency data
└── src/
    ├── dotnet-nuget.csproj
    └── Greeter.cs
```

## Run
```powershell
cd samples/dotnet-nuget
./build.ps1
```
