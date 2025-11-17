###################################
# Target: nuget
###################################
FROM build AS nuget

ARG BUILDCHARTS_SRC
ARG VERSION
ARG CONFIGURATION=Release
ARG PATHS_INCLUDE

# Pack the code into a nuget package when changes are detected.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    mkdir -p /output; \
    BUILDCHARTS_DIR="$(dirname "$BUILDCHARTS_SRC")"; \
    if [ -n "$PATHS_INCLUDE" ]; then \
        if git -C /src diff --quiet --exit-code origin/HEAD -- $PATHS_INCLUDE; then \
            echo "No changes detected in $PATHS_INCLUDE when comparing to origin/HEAD"; \
            echo "Skipping nuget pack"; \
            exit 0; \
        else \
            echo "Changes detected in $PATHS_INCLUDE."; \
        fi; \
    # else \
    #     if git -C /src diff --quiet --exit-code origin/HEAD -- "$BUILDCHARTS_DIR"; then \
    #         echo "No changes detected in $BUILDCHARTS_DIR when comparing to origin/HEAD"; \
    #         echo "Skipping nuget pack"; \
    #         exit 0; \
    #     else \
    #         echo "Changes detected in $BUILDCHARTS_DIR."; \
    #     fi; \
    fi; \
    dotnet pack "/src/$BUILDCHARTS_SRC" \
        --no-build \
        --configuration "$CONFIGURATION" \
        --output "/output" \
        /p:PackageVersion="$VERSION"

