# npm-npm sample

This sample includes a minimal JavaScript library that is packaged as an npm package.

## File structure
```text
.
├── build.yml            # Build metadata
├── build.ps1            # Generate + bake + list tgz output
├── charts/
│   └── buildcharts/
│       └── Chart.yaml   # Chart dependency data
└── src/
    ├── .dockerignore
    ├── build.mjs
    ├── greeter.js
    ├── package.json
    └── pnpm-lock.yaml
```

## Run
```powershell
cd samples/npm-npm
./build.ps1
```

The Bake command sets the build context to `src`, where `package.json` and
`pnpm-lock.yaml` are located. Dependencies come from the public npm registry.

The package is exported to `.buildcharts/output/npm`. The sample does not publish it.
