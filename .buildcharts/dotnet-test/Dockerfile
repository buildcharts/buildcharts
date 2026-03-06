###################################
# Target: pre-test
###################################
FROM build AS pre-test

ARG BUILDCHARTS_SRC
ARG CONFIGURATION=Release

# Configure Testcontainers to talk to dind daemon used by the TestcontainersDinD@v1 plugin.
ARG DOCKER_HOST=tcp://host.docker.internal:2375
ARG TESTCONTAINERS_HOST_OVERRIDE=

# Execute tests and collect TRX + coverage outputs.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet test "/src/$BUILDCHARTS_SRC" \
        --no-build \
        --no-restore \
        --configuration "$CONFIGURATION" \
        --collect "XPlat Code Coverage" \
        --logger "trx" \
        --results-directory "/results/$(basename "$BUILDCHARTS_SRC" .csproj)_$(date +%Y%m%d-%H%M%S)" || \
        echo "⚠️ dotnet test failed, continuing so results can be verified later"

# Copy TRX and coverage files while preserving their relative paths.
RUN mkdir -p /output && \
    find /results -name '*.trx' \
        -exec sh -c ' rel="${1#/results/}"; dst="/output/$(dirname "$rel")"; mkdir -p "$dst"; cp "$1" "$dst/" ' _ {} \; 2>/dev/null || true && \
    find /results -path '*/In/**/coverage.cobertura.xml' \
        -exec sh -c 'rel="${1#/results/}"; dst="/output/$(dirname "$rel")"; mkdir -p "$dst"; cp "$1" "$dst/" ' _ {} \; 2>/dev/null || true

# RUN mkdir -p /output && \
#     find /results \( -name '*.trx' -o -path '*/In/*/coverage.cobertura.xml' \) \
#       -exec sh -c 'rel="${1#/results/}"; dst="/output/$(dirname "$rel")"; mkdir -p "$dst"; cp "$1" "$dst/"' _ {} \; \
#       2>/dev/null || true

###################################
# Target: test
###################################
FROM scratch AS test
COPY --from=pre-test /output /