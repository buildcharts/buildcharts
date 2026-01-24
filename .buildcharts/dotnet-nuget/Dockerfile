###################################
# Target: pre-nuget
###################################
FROM build AS pre-nuget

ARG BUILDCHARTS_SRC
ARG VERSION
ARG CONFIGURATION=Release
ARG PATHS_INCLUDE

# Follow this for SourceLink + deterministic NuGet packages:
# - Ensure '.dockerignore' does NOT list '.git' (when not following the minimal git repository metadata guide in docs).
# - Ensure '.dockerignore' does NOT list '.gitignore' (so bin/ and obj/ are ignored correctly).
# - Ensure 'bin/' and 'obj/' are in .gitignore.
# - Add this to your .csproj:
#     <EmbedUntrackedSources>true</EmbedUntrackedSources>
#     <PublishRepositoryUrl>true</PublishRepositoryUrl>
# - Add this to your .csproj to include PDBs in the package (when not using symbol packages):
#     <AllowedOutputExtensionsInPackageBuildOutputFolder>$(AllowedOutputExtensionsInPackageBuildOutputFolder);.pdb</AllowedOutputExtensionsInPackageBuildOutputFolder>

RUN set -eu; \
    echo "Verifying SourceLink prerequisites" >&2; \
    root="/src"; \
    warnings=0; \
    if [ ! -d "$root/.git" ] && [ ! -f "$root/.git" ]; then \
      warnings=1; \
      echo "warning: .git is missing inside the container (expected at: $root/.git)." >&2; \
      echo "- reason: .git is probably listed in .dockerignore, so it was not copied into the build context." >&2; \
      echo "- impact: SourceLink cannot embed repository metadata and deterministic validation may fail." >&2; \
      echo "- fix:    Remove '.git' from .dockerignore and rebuild the image." >&2; \
    fi; \
    if [ ! -f "$root/.gitignore" ]; then \
      warnings=1; \
      echo "warning: .gitignore is missing inside the container (expected at: $root/.gitignore)." >&2; \
      echo "- reason: .gitignore is probably listed in .dockerignore, so it was not copied into the build context." >&2; \
      echo "- impact: bin/ and obj/ may be treated as untracked sources and can break SourceLink/deterministic validation." >&2; \
      echo "- fix:    Remove '.gitignore' from .dockerignore and rebuild the image." >&2; \
    else \
      has_ignore_rule() { \
        pattern="$1"; \
        awk -v pat="$pattern" ' \
          /^[[:space:]]*#/ {next}                     # skip comments \
          /^[[:space:]]*$/ {next}                     # skip empty lines \
          { gsub(/^[[:space:]]+/, "", $0); }          # trim leading spaces \
          index($0, pat) == 1 { found=1 }             # line starts with pattern \
          END { exit(found ? 0 : 1) }                 \
        ' "$root/.gitignore"; \
      }; \
      if ! has_ignore_rule "bin/"; then \
        warnings=1; \
        echo "warning: 'bin/' is not ignored in .gitignore inside the container." >&2; \
        echo "- impact: build outputs under 'bin/' may be treated as tracked/untracked sources." >&2; \
        echo "- fix:    Add 'bin/' to the root .gitignore and commit the change." >&2; \
      fi; \
      if ! has_ignore_rule "obj/"; then \
        warnings=1; \
        echo "warning: 'obj/' is not ignored in .gitignore inside the container." >&2; \
        echo "- impact: SourceLink and deterministic builds may treat files in 'obj/' as sources (e.g. validation errors like 'contains untracked obj')." >&2; \
        echo "- fix:    Add 'obj/' to the root .gitignore and commit the change." >&2; \
      fi; \
    fi; \
    if [ "$warnings" -eq 0 ]; then \
      echo "SourceLink prerequisites OK" >&2; \
    else \
      echo "SourceLink prerequisites completed with warnings" >&2; \
      exit 1; \
    fi

RUN set -eu; \
    PATHS_INCLUDE="${PATHS_INCLUDE:-}"; \
    if [ -n "$PATHS_INCLUDE" ]; then \
        echo "Detecting changes in $PATHS_INCLUDE when comparing to origin/HEAD" >&2; \
        if git -C /src diff --quiet --exit-code origin/HEAD -- $PATHS_INCLUDE; then \
            mkdir -p /output; \
            echo "No changes detected. Skipping nuget pack..." >&2; \
            exit 0; \
        else \
            echo "Changes detected" >&2; \
        fi; \
    # Optional: Enable this to only pack nuget if there are changes in the $BUILDCHARTS_DIR when comparing to origin/HEAD.
    # else \
    #     echo "Detecting changes in  $BUILDCHARTS_DIR when comparing to origin/HEAD" >&2; \
    #     BUILDCHARTS_DIR="$(dirname "$BUILDCHARTS_SRC")"; \
    #     if git -C /src diff --quiet --exit-code origin/HEAD -- "$BUILDCHARTS_DIR"; then \
    #         mkdir -p /output; \
    #         echo "No changes detected. Skipping nuget pack..." >&2; \
    #         exit 0; \
    #     else \
    #         echo "Changes detected" >&2; \
    #     fi; \
    fi;

# Pack the code into a nuget package when changes are detected.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet pack "/src/$BUILDCHARTS_SRC" \
        --no-build \
        --configuration "$CONFIGURATION" \
        --output "/output" \
        /p:PackageVersion="$VERSION"

###################################
# Target: nuget
###################################
FROM scratch AS nuget
COPY --from=pre-nuget /output /