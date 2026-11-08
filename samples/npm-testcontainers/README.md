# npm-testcontainers sample

This sample includes one Node.js unit test using Testcontainers.

When running `buildcharts generate`, the `TestcontainersDinD@v1` plugin starts a
Docker-in-Docker daemon in a container named `buildcharts-dind`.

## File structure
```text
.
├── build.yml            # Build metadata
├── build.ps1            # Generate + bake + parse JUnit
├── charts/
│   └── buildcharts/
│       └── Chart.yaml   # Chart dependency data
└── src/
    ├── .dockerignore
    ├── build.mjs
    ├── greeter.js
    ├── greeter.test.js
    ├── package.json
    ├── pnpm-lock.yaml
    └── run-tests.mjs
```

## Run
```powershell
cd samples/npm-testcontainers
./build.ps1
```

The Bake command sets the build context to `src`, where `package.json` and
`pnpm-lock.yaml` are located. Dependencies come from the public npm registry.

JUnit and Cobertura coverage files are exported to `.buildcharts/output/test`.
The script fails if the JUnit report is missing or contains failed tests.

## Verify DinD activity
You can inspect Docker activity inside the DinD container while the test is running:

```powershell
docker exec -it buildcharts-dind watch docker ps
```
