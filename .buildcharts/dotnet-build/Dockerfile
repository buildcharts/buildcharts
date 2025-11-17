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

# Install the self-contained Azure Artifacts Credential Provider to enable support for VSS_NUGET_EXTERNAL_FEED_ENDPOINTS.
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh | sh

# Copy project files and restore as distinct layer.
COPY --link --parents *.sln **/*.csproj **/NuGet.Config **/Directory.Build.props **/Directory.Build.targets **/Directory.Packages.props global.json ./

# Optional: Clean is only needed when bin/obj folders are NOT included in .dockerignore, which is best practice, so we skip it.
#RUN dotnet clean 

# Restore with secrets.
RUN --mount=type=secret,id=VSS_NUGET_ACCESSTOKEN,env=VSS_NUGET_ACCESSTOKEN \
    --mount=type=secret,id=VSS_NUGET_EXTERNAL_FEED_ENDPOINTS,env=VSS_NUGET_EXTERNAL_FEED_ENDPOINTS \
    --mount=type=cache,id=nuget,target=/root/.nuget/packages \
      dotnet restore "/src/$BUILDCHARTS_SRC" --locked-mode --use-lock-file

# Copy code.
COPY --link . .

# Optional: Allow git operations to operate on the repository. Recent versions of git require the .git folder to be owned by the same user.
# - This can be skipped when no git-operations in dockerfiles.
# RUN git config --global --add safe.directory /src

# Optional: Normalize line endings for SourceLink/determinism.
# - Prefer .gitattributes: '* text=auto eol=lf' to enforce LF.
# - If incorrect git (when running inside docker) may automatically add CRLF line endings which breaks determinism (PDB files links to non-existing sources in git).
# - Validate with `sourcelink test <filename>.pdb` (for PDB files inside the nuget package).
RUN git -C /src config core.autocrlf false \
    && git -C /src checkout-index -f --all

# Execute build.
RUN --mount=type=secret,id=VSS_NUGET_ACCESSTOKEN,env=VSS_NUGET_ACCESSTOKEN \
    --mount=type=secret,id=VSS_NUGET_EXTERNAL_FEED_ENDPOINTS,env=VSS_NUGET_EXTERNAL_FEED_ENDPOINTS \
    --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet build "/src/$BUILDCHARTS_SRC" \
        --no-restore \
        --configuration "$CONFIGURATION" \
        /p:ContinuousIntegrationBuild=true \
        /p:SourceRevisionId="$COMMIT" \
        /p:Version="$VERSION"