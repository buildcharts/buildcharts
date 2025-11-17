###################################
# Target: test
###################################
FROM build AS test

ARG BUILDCHARTS_SRC
ARG CONFIGURATION=Release

# Configure Testcontainers to talk to dind daemon used by the TestcontainersDinD@v1 plugin.
ARG DOCKER_HOST=tcp://host.docker.internal:2375
ARG TESTCONTAINERS_HOST_OVERRIDE=

# Execute tests and copy the TRX files to output.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet test "/src/$BUILDCHARTS_SRC" \
        --no-build \
        --no-restore \
        --configuration "$CONFIGURATION" \
        --logger "trx;LogFileName=$(basename "$BUILDCHARTS_SRC" .csproj)_$(date +%Y%m%d-%H%M%S).trx" \
        --results-directory /results || \
        echo "⚠️ dotnet test failed, continuing so results can be verified later" ; \
    mkdir -p /output && cp /results/*.trx /output/ 2>/dev/null || true
