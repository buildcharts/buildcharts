# npm-test sample

This sample includes simple Node.js unit tests and validates results by parsing the generated JUnit file.

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
cd samples/npm-test
./build.ps1
```

The Bake command sets the build context to `src`, where `package.json` and
`pnpm-lock.yaml` are located. Dependencies come from the public npm registry.

JUnit and Cobertura coverage files are exported to `.buildcharts/output/test`.
The script fails if the JUnit report is missing or contains failed tests.
