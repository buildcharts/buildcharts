# dotnet-test sample

This sample includes simple unit tests and validates results by parsing the generated TRX file.

## File structure
```text
.
├── build.yml            # Build metadata
├── build.ps1            # Generate + bake + parse TRX
├── charts/
│   └── buildcharts/
│       └── Chart.yaml   # Chart dependency data
└── src/
    ├── dotnet-test.csproj
    └── RandomTests.cs
```

## Run
```powershell
cd samples/dotnet-test
./build.ps1
```
