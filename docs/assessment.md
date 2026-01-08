# Assessment and Rationale
This document introduces the **rationale, goals, and core concepts behind `buildcharts`**. It is intentionally pre-implementation and focuses on architectural direction rather than concrete implementation details.

## Concept
`buildcharts` is a framework for defining and generating CI/CD build plans using declarative project metadata (`build.yml`) and reusable, versioned pipeline templates distributed as OCI artifacts (conceptually similar to Helm charts). It generates executable build plans (Docker Buildx Bake files) that can be run identically in CI and locally.

- Define build intent in `build.yml` (targets like `build`, `test`, `nuget`, `docker`).
- Each target maps to a chart‑hosted Dockerfile/template stored in an OCI registry.
- Each chart defines Dockerbuild stages with target mplementation (eg. `dotnet build`).
- `buildcharts` resolves those templates and renders a Buildx Bake file (e.g., `docker-bake.hcl`).
- Run with `docker buildx bake`.

## Motivation
This effort came after running into over-complex, hard-to-troubleshoot CI/CD setups across multiple cloud providers and platforms. Seeing different strategies across organizations made the fragmentation obvious:

- Shared and nested YAML templates.
- Mixed versioning across branches, tags and folders.
- Often platform-specific, leading to vendor lock-in.
- Not all platforms support dynamically created downstream pipelines.
- Reusable actions as pipeline steps are not consistently supported across platforms.
- Mixed scripting choices (PowerShell, Bash, .NET scripts, Dockerfiles), which often reflect developer preferences and can feel arbitrary.
- Shifting requirements and weak discipline often turn a shared pipeline into incompatible variants.
- Not locally debuggable - troubleshooting requires repeated pipeline runs (trial and error), which is slow and error-prone.
- Updates are fragile due to unintended breaking changes.
- Template-based creation is easy to start with but often lacks a lifecycle perspective.

Keeping pipelines up to date is also hard: changing a template is easy, but rolling it out across every service is not, so teams drift on old versions or migrate without knowing what changed. Platform teams try to lead the rollout but hit new requirements and inflexible solutions, which turns into long troubleshooting sessions and painful local setup to mimic CI (often with hidden secrets and configuration).

*The goal is a single, portable approach that replaces ad-hoc scripts with consistent Docker-based build steps while staying flexible and provider-agnostic*. 

## Goals
1) Centralize and standardize pipeline logic in Dockerfiles stored as versioned OCI charts.
1) Chart updates can be automated via `Chart.yaml` and tools like Dependabot/Renovate.
1) Developer-friendly by expressing build intent in `build.yml` while abstracting implementation complexity behind charts.
1) Easily extensible via custom OCI charts and plugins.
1) Scale across many repos/services using shared, versioned templates.
1) Dynamically generate build pipelines (where custom Dockerfile targets replace provider-specific steps).
1) Locally debuggable.
1) Keep tooling familiar (Docker, BuildKit, Buildx Bake).
1) Support optional lock files with SHA digests for repeatable builds.
1) Support version selection: pinned versions for stable, controlled updates, or floating tags like `latest` for faster updates (higher risk), aligned with common migration and deprecation workflows.
1) Platform-agnostic, reproducible, parallelized, cache-friendly, and containerized using Docker Buildx Bake.

## Non-goals
- Supporting non‑containerized build workflows (more generators e.g. Buildah, Kaniko, Podman).
- Replacing CI/CD platforms.
- Replacing release orchestration systems that coordinate multi‑repo release channels.
- Eliminating the need to understand Dockerfiles or container build basics.
- Toolchain for packaging or publishing charts.
- Toolchain for signing and verifying charts.
- Providing scheduling, secrets, or artifact hosting
- Managing deployments or runtime configuration - `buildcharts` is purely for builds.

## Scope
<!-- 
### Producing OCI charts/templates
`buildcharts` does not require a specific packaging tool. Any OCI-compliant CLI can package and push artifacts (e.g., Helm, ORAS). Helm is common for templating, but it is not a runtime dependency. The CLI consumes OCI artifacts by registry/repo/digest and remains tool-agnostic.

### Provenance and signing
`buildcharts` does not provide its own trust system. Provenance and authenticity are handled by OCI-native signing tools. Artifacts can be signed with Notation (Notary v2) or Cosign (OCI referrers), or Helm `.prov` files where relevant. Signature creation, key management, and policy enforcement remain external to `buildcharts`.
 -->

### Packaging OCI charts/templates

- `buildcharts` **does not mandate** a specific toolchain for producing or publishing charts. Any OCI-compliant CLI may be used to package and push OCI charts to a registry, including but not limited to Helm and ORAS.

    Helm is commonly used for its mature chart packaging and templating capabilities, but it is not a runtime dependency of `buildcharts` itself. The `buildcharts` CLI consumes OCI artifacts by reference (registry, repository, digest) and remains agnostic to the tool used to produce them.

### Provenance and Signing

- `buildcharts` **does not introduce** a custom signing or trust mechanism. Instead, artifact provenance and authenticity are delegated to **OCI-native signing solutions**.

    Depending on organizational requirements, `buildcharts` may be signed using:
    - Notation (Notary v2) or Cosign, which attach signatures as OCI referrer artifacts and enable registry-native policy enforcement.
    - Helm chart provenance (`.prov`), where applicable, for compatibility with existing Helm-based workflows.

    `buildcharts` itself only requires that artifacts be verifiable at consumption time; signature creation, key management, and trust policy enforcement are intentionally externalized to standard OCI tooling.

## Features
- **Metadata-driven builds:** Projects declare their build needs in `build.yml`, listing variables and targets such as build, tests, nugets, or Docker image creation. Each target maps to a chart alias (for example `build`, `test`, `nuget`, or `docker`) that describes how to execute the work.
- **Chart-based templates:** The chart points to OCI-hosted Dockerfile templates that hold the actual pipeline logic. Pulling these charts centralizes the implementation details while letting repositories keep only lightweight metadata.
- **Reproducible builds:** Lock files (same format as Helm's `Chart.lock`) pins exact chart versions and digests, ensuring consistent builds across CI and local runs, reducing drift from upstream chart changes.
- **CLI workflow:** The `buildcharts` CLI resolves chart dependencies, validates metadata, and renders CI/CD artifacts. Running `buildcharts generate` produces a Docker Buildx Bake file (`docker-bake.hcl`), which is then executed with `docker buildx bake` to produce outputs.
- **Parallelized builds:** Buildx Bake plans enable parallel target execution, speeding multi-target or multi-architecture builds while reusing shared steps via BuildKit's caching.
- **Targeted runs:** Bake targets can be invoked directly (for example `docker buildx bake test`), so teams can build a single service image or run a test/export target without executing the full pipeline.
- **De-duplicated shared stages:** Bake coordinates multiple Docker builds (build and packaging steps), while each Dockerfile owns its dependency stages. BuildKit caching allows Bake to de-duplicate shared stages across different Dockerfiles, reducing repeated work while the Bake plan orchestrates parallel targets.
- **Templated OCI charts/artifacts with Dockerfiles:** These effectively act like a distributed multi-Docker build - shared build definitions live in versioned OCI charts, while each repo pins or overrides the images it needs.
- **Reusable outputs:** By relying on Docker BuildKit and templated charts, builds run in consistent containers and can emit images, test results, and other artifacts wich can be reused between different layers without duplicating pipeline YAML across services.
- **Caching:** Docker layer caching can be more effective and simpler than platform-provided pipeline caching (e.g., for NuGet or NPM packages), especially when using BuildKit cache exports.
- **Platform-agnostic execution:** The same Bake plan runs across CI providers and locally with Docker, making pipelines easier to reproduce, debug, and keep consistent.
- **Extensibility:** Teams can add new Dockerfiles and targets to define custom pipelines, such as building database migration images (for example a standalone Entity Framework migration executable) or running E2E test containers.
- **Secret handling:** BuildKit supports secret mounts (`RUN --mount=type=secret`) so credentials can be provided at build time without baking them into images or logs.
- **Separation:** Intent (metadata in `build.yml`) defines what to build; implementation (charts/templates) defines how it runs across targets.
- **No runtime dependencies:** Validates and consumes OCI artifacts by digest and does not depend on the presence of Helm, ORAS, or any specific packaging tool at runtime.


## Maintainability and version control
- **Central chart updates with opt-in overrides:** Because templates are delivered as versioned OCI charts, platform teams can update templates in one place and let services consume new capabilities. Individual repositories can override specific chart versions when needed, trading speed for stability.
- **Lock files for repeatability:** `buildcharts update` produces optional lock files for pinning chart digests, enabling reproducible, immutable builds across local builds and CI runners while still allowing intentional bumps.
- **Automated dependency hygiene:** OCI chart versioning means Renovate or Dependabot can automatically propose version bumps for chart dependencies, keeping pipelines current without manual toil.
- **Governance:** Charts are stored in a dedicated Git repository and governed through standard branch protection, mandatory PR reviews, and required automated quality gates. These gates include Dockerfile linting, static analysis, and policy checks to enforce security, correctness, and versioning rules before charts are published as OCI artifacts. Continuous delivery is used to publish the resulting OCI images to a private container registry.

## Pros
- `buildcharts` is not a CI system; it is a build-plan generator that works with zero background infrastructure.
- Zero platform tax: no CI cluster to maintain, no HA concerns, no upgrades, no authentication plumbing, no databases, no persistent state.
- True local parity: local runs are identical to CI runs, using the same generated Bake file, the same Dockerfile, and the same BuildKit cache behavior.
- Near-zero adoption friction: teams already have Docker and Dockerfiles.
- `buildcharts` competes with hand-written CI YAML, copy-pasted pipelines, per-repository Docker build logic, and ad-hoc scripting.
- Deliberate tradeoff of native job orchestration in exchange for zero infrastructure, full local reproducibility, strong standardization, very low operational cost, and provider neutrality.
- No DSL or schema needed since the execution runs in Dockerfiles. Use docker syntax, bring your own scripts or use official docker images.

## Cons
- Strong dependence on Docker/Buildx as the execution substrate.
- Requires Docker/Buildx availability, which can be heavy for some CI runners or developers.
- Chart indirection adds a learning curve: teams must understand both metadata and the underlying templates to debug issues.
- Ecosystem maturity of charts and plugins determines how many scenarios work out of the box; custom needs may require authoring new charts.
- OCI charts and generated artifact adds indirection layers to inspect when troubleshooting failures.

## Critical concerns
- **docker dependency risk:** Tying builds so tightly to Docker tooling (Buildx, BuildKit, Bake) creates a brittle supply chain risk; any Docker tooling change, license shift, or CVE can impact all pipelines.
- **buildcharts dependency risk:** Tying builds to buildcharts creates supply-chain risk. As a fallback, you can commit the generated `docker-bake.hcl` (or inline Dockerfiles) to remove the runtime generator dependency, at the cost of drifting from upstream chart updates.
- **Operational fragility for non-Docker shops:** Teams without deep Docker expertise face a steep operational burden - debugging BuildKit/bake issues, configuring registry auth, and managing remote builders - which can erase the claimed productivity gains.
- **Scale and caching pitfalls:** Parallel Bake plans can saturate shared runners or exhaust cache storage; mis-tuned cache exports/imports often lead to slow, flaky builds that are harder to diagnose than straightforward CI steps.
- **Opaque template failures:** Chart indirection can obscure root causes; when generation or rendering breaks, engineers are forced to spelunk through templated artifacts, OCI chart versions, and CLI behavior, prolonging outages.
- **Pull-based OCI supply chain:** Pull-based OCI distribution introduces supply-chain dependencies to container registry (registry uptime, auth, vulnerability vectors). This is partially mitigated by caching chart digests; if a SHA digest already exists locally, the chart is not pulled again for offline-friendly operations.
- **Chart integrity/provenance:** Without signing or verification, chart artifacts remain a supply-chain risk even when pinned by digest.
- **Builder infra drift:** Buildx builder version/config differences can cause inconsistent results across environments.
- **Secrets lifecycle:** Secret injection uses BuildKit mounts/plugins, but provisioning and auditability still depend on the CI provider.

## When this fits
- Teams already standardized on Docker/BuildKit with reliable registry access and cache infrastructure.
- Organizations that want to centralize CI logic and keep service repos thin.
- Projects that benefit from parallel builds.
- Platforms that can invest in maintaining a curated chart ecosystem.
- Teams that want faster builds via parallelization and de-duplication, plus provider-agnostic workflows that are easy to debug locally with Docker.
- Environments where the added complexity pays off, as this was primarily designed for microservice-heavy architectures.

## When it doesn't
- Environments that cannot run Docker daemons or lack BuildKit support.
- Teams without Docker expertise or with highly bespoke build steps.
- CI runners with tight CPU, memory, or storage budgets where Bake parallelism backfires.

## Quality and feasibility
`buildcharts` is practical for teams invested in containerized builds and aligns with established patterns (Helm-like charts for CI/CD). Quality hinges on the chart ecosystem and its maintenance. On the flip side, orgs without strong Docker/OCI support or with highly bespoke pipelines will likely adopt more slowly due to the templating and dependency footprint. The centralized chart model reduces duplicated build logic across repos, improving consistency and easing maintenance.

## Comparison
- https://github.com/dagger/dagger - Programmable, container-native build pipelines with strong local/CI parity, but introduces a new execution engine and SDK-driven workflow model.
- https://github.com/earthly/earthly - Build-centric automation on top of BuildKit that runs locally and in CI, using its own Earthfile DSL (no longer maintained).
- https://github.com/tektoncd/pipeline - Kubernetes-native CI/CD pipelines distributed as OCI bundles, powerful at scale but requires a Kubernetes control plane and is not local-first.
- buildkite
- bazel
- nix

## Prompts
```
Review buildcharts from a technical perspective from microsoft principal engineer. 
- Are there any similar tools.
- Compare these for CI/CD pipelines.
- Also buildcharts run locally without any hosted service. 
- Why buildcharts is different
- Is it a good idea?
- Innovation perspectice (containers for runtime, helm for deployments, buildcharts for builds).
- Give me DX perspective
- Give me internal RFC

https://raw.githubusercontent.com/eddietisma/buildcharts/refs/heads/main/docs/assessment.md
```

```
You are a Platform Engineer reviewing a CI build tool proposal (buildcharts). Produce a structured technical review with:
- Architecture critique: inputs (build.yaml), chart/template model, OCI distribution, lockfile/digest pinning, BuildKit/Buildx/Bake execution, artifact outputs.
- Correctness & reproducibility
- Performance & caching
- Developer experience & debuggability
- Security & supply-chain integrity
- Maintainability & extensibility
- Portability (Linux/macOS/Windows) and local/CI parity
- CI/CD integration
- Governance at scale (100s of repos)
- Identify similar tools and compare tradeoffs for CI/CD pipelines. 

https://raw.githubusercontent.com/eddietisma/buildcharts/refs/heads/main/docs/assessment.md
```
