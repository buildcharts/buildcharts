###################################
# Target: pre-nuget
###################################
FROM build AS pre-nuget

ARG BUILDCHARTS_SRC
ARG VERSION
ARG CONFIGURATION=Release
ARG PATHS_INCLUDE
ARG SOURCELINK_STRICT=1

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
      if [ "$SOURCELINK_STRICT" = "1" ] || [ "$SOURCELINK_STRICT" = "true" ]; then \
        echo "SourceLink prerequisites completed with warnings" >&2; \
        exit 1; \
      else \
        echo "SourceLink prerequisites completed with warnings (SOURCELINK_STRICT=$SOURCELINK_STRICT; continuing)" >&2; \
      fi; \
    fi

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    set -eu; \
    mkdir -p /output; \
    PATHS_INCLUDE="${PATHS_INCLUDE:-}"; \
    if [ -n "$PATHS_INCLUDE" ]; then \
        COMPARE_REF=""; \
        if git -C /src rev-parse --verify --quiet origin/main >/dev/null; then \
            COMPARE_REF="origin/main...HEAD"; \
        elif git -C /src rev-parse --verify --quiet origin/master >/dev/null; then \
            COMPARE_REF="origin/master...HEAD"; \
        elif git -C /src rev-parse --verify --quiet origin/HEAD >/dev/null; then \
            COMPARE_REF="origin/HEAD...HEAD"; \
        fi; \
        if [ -n "$COMPARE_REF" ]; then \
            echo "Detecting changes in $PATHS_INCLUDE against $COMPARE_REF" >&2; \
            branch_clean=0; \
            git -C /src diff --quiet --exit-code "$COMPARE_REF" -- $PATHS_INCLUDE || branch_clean=$?; \
            worktree_clean=0; \
            # Check unstaged changes
            git -C /src diff --quiet --exit-code -- $PATHS_INCLUDE || worktree_clean=$?; \
            index_clean=0; \
            # Check staged changes
            git -C /src diff --cached --quiet --exit-code -- $PATHS_INCLUDE || index_clean=$?; \
            if [ "$branch_clean" -eq 0 ] && [ "$worktree_clean" -eq 0 ] && [ "$index_clean" -eq 0 ]; then \
                echo "No changes detected ($COMPARE_REF). Skipping nuget pack..." >&2; \
                exit 0; \
            fi; \
        else \
            # No remote compare ref found. With fetchDepth=0 + explicit fetch in CI,
            # this path should be rare. Fail open to avoid false skips.
            echo "No origin compare ref found. Running dotnet pack..." >&2; \
        fi; \
    fi; \
    echo "Changes detected. Running dotnet pack..." >&2; \
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
