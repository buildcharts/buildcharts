###################################
# Target: nuget
###################################
FROM build AS nuget

ARG BUILDCHARTS_SRC
ARG VERSION
ARG CONFIGURATION=Release
ARG PATHS_INCLUDE

# Follow this to for SourceLink + deterministic NuGet packages:
# - Ensure '.dockerignore' does NOT list '.git'  (when not folloing the minimal metadata guide in docs).
# - Ensure '.dockerignore' does NOT list '.gitignore' (so bin/ and obj/ are ignored correctly).
# - Ensure 'bin/' and 'obj/' are in .gitignore.
# - Add this to your .csproj:
#     <EmbedUntrackedSources>true</EmbedUntrackedSources>
#     <PublishRepositoryUrl>true</PublishRepositoryUrl>
# - Add this to your .csproj to include PDBs in the package (when not using symbol packages):
#     <AllowedOutputExtensionsInPackageBuildOutputFolder>$(AllowedOutputExtensionsInPackageBuildOutputFolder);.pdb</AllowedOutputExtensionsInPackageBuildOutputFolder>
# Verify .gitignore / bin / obj configuration for SourceLink-friendly NuGet packages (non-fatal)

RUN set -eu; \
    echo "Verifying SourceLink prerequisites" >&2; \
    root="/src"; \
    if [ ! -f "$root/.gitignore" ]; then \
      echo "WARNING: .gitignore is missing inside the container (expected at: $root/.gitignore)." >&2; \
      echo "         Reason: .gitignore is probably listed in .dockerignore, so it was not copied into the build context." >&2; \
      echo "         Impact: bin/ and obj/ may be treated as untracked sources and can break SourceLink/deterministic validation." >&2; \
      echo "         Fix:    Remove '.gitignore' from .dockerignore and rebuild the image." >&2; \
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
        echo "WARNING: 'bin/' is not ignored in .gitignore inside the container." >&2; \
        echo "         Impact: build outputs under 'bin/' may be treated as tracked/untracked sources." >&2; \
        echo "         Fix:    Add 'bin/' to the root .gitignore and commit the change." >&2; \
      fi; \
      if ! has_ignore_rule "obj/"; then \
        echo "WARNING: 'obj/' is not ignored in .gitignore inside the container." >&2; \
        echo "         Impact: SourceLink and deterministic builds may treat files in 'obj/' as sources" >&2; \
        echo "                 (e.g. validation errors like 'contains untracked obj')." >&2; \
        echo "         Fix:    Add 'obj/' to the root .gitignore and commit the change." >&2; \
      fi; \
    fi

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