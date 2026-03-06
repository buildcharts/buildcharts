###################################
# Target: pre-docker
###################################
FROM build AS pre-docker

ARG BUILDCHARTS_SRC
ARG CONFIGURATION=Release

# Publish application and dependencies for deployment.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "/src/$BUILDCHARTS_SRC" \
        --no-build \
        --no-restore \
        --configuration "$CONFIGURATION" \
        --output "/output"

# Ensure static app.* startup files for ENTRYPOINT ["dotnet","/app/app.dll"].
# Dotnet host also requires matching app.deps.json and app.runtimeconfig.json.
RUN DLL_NAME=$(basename "$BUILDCHARTS_SRC" .csproj).dll && \
    APP_NAME="${DLL_NAME%.dll}" && \
    cp -f "/output/$DLL_NAME" /output/app.dll && \
    cp -f "/output/$APP_NAME.deps.json" /output/app.deps.json && \
    cp -f "/output/$APP_NAME.runtimeconfig.json" /output/app.runtimeconfig.json

###################################
# Target: docker
###################################
FROM base AS docker

WORKDIR /app
COPY --link --from=pre-docker /output .
ENTRYPOINT ["dotnet", "/app/app.dll"]