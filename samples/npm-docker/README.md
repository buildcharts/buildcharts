# npm-docker sample

This sample includes a minimal JavaScript app that is built into an Nginx Docker image.

## File structure
```text
.
├── build.yml            # Build metadata
├── build.ps1            # Generate + bake + run
├── charts/
│   └── buildcharts/
│       └── Chart.yaml   # Chart dependency data
└── src/
    ├── .dockerignore
    ├── app.js
    ├── assets/environment-settings.template.json
    ├── build.mjs
    ├── index.html
    ├── nginx-custom.conf
    ├── package.json
    └── pnpm-lock.yaml
```

## Run
```powershell
cd samples/npm-docker
./build.ps1
```

The Bake command sets the build context to `src`, where `package.json` and
`pnpm-lock.yaml` are located. Dependencies come from the public npm registry.

Open http://localhost:8080/ after the container starts. Press Ctrl+C to stop it.
