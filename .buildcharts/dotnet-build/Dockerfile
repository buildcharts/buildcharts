# syntax=docker/dockerfile:1-labs

###################################
# Target: build
###################################
FROM base AS build

ENV DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true

WORKDIR /src

ARG BUILDCHARTS_SRC
ARG VERSION
ARG CONFIGURATION=Release
ARG COMMIT

# Install the self-contained Azure Artifacts Credential Provider to enable support for ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS.
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh | sh

# Copy project files and restore as distinct layer.
COPY --link --parents *.sln *.slnx **/*.csproj **/*.dcproj **/[Nn]u[Gg]et.[Cc]onfig **/Directory.Build.props **/Directory.Build.targets **/Directory.Packages.props global.json ./

# Optional: Clean is only needed when bin/obj folders are NOT included in .dockerignore, which is best practice, so we skip it.
#RUN dotnet clean 

# Restore with secrets.
RUN --mount=type=secret,id=ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS,env=ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS \
    --mount=type=cache,id=nuget,target=/root/.nuget/packages \
      dotnet restore "/src/$BUILDCHARTS_SRC" --locked-mode --use-lock-file

# Copy code.
COPY --link . .

# Execute build.
RUN --mount=type=secret,id=ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS,env=ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS \
    --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet build "/src/$BUILDCHARTS_SRC" \
        --no-restore \
        --configuration "$CONFIGURATION" \
        /p:ContinuousIntegrationBuild=true \
        /p:SourceRevisionId="$COMMIT" \
        /p:Version="$VERSION"