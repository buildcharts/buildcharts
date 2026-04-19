###################################
# Target: pre-docker
###################################
FROM build AS pre-docker

ARG BUILDCHARTS_SRC
ARG CONFIGURATION=Release

# Publish application and dependencies for deployment.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.local/share/NuGet/v3-cache \
    --mount=type=cache,target=/root/.local/share/NuGet/plugins-cache \
    --mount=type=cache,target=/tmp/NuGetScratchroot \
    dotnet publish "/src/$BUILDCHARTS_SRC" \
        --no-build \
        --no-restore \
        --configuration "$CONFIGURATION" \
        --output "/output"

# Ensure static app.* startup files for ENTRYPOINT ["dotnet","/app/app.dll"].
# Dotnet host also requires matching app.deps.json and app.runtimeconfig.json.
# Resolve the published app name from the runtimeconfig file so this still works when AssemblyName differs from the project file name.
RUN set -eu; \
    RUNTIMECONFIG_PATH="$(find /output -maxdepth 1 -name '*.runtimeconfig.json' | head -n 1)"; \
    if [ -z "$RUNTIMECONFIG_PATH" ]; then \
        echo "No runtimeconfig file found in /output" >&2; \
        exit 1; \
    fi; \
    APP_NAME="$(basename "$RUNTIMECONFIG_PATH" .runtimeconfig.json)"; \
    cp -f "/output/$APP_NAME.dll" /output/app.dll; \
    cp -f "/output/$APP_NAME.deps.json" /output/app.deps.json; \
    cp -f "/output/$APP_NAME.runtimeconfig.json" /output/app.runtimeconfig.json

###################################
# Target: docker
###################################
FROM base AS docker

WORKDIR /app
COPY --link --from=pre-docker /output .
ENTRYPOINT ["dotnet", "/app/app.dll"]
