# Assessment and Rationale

This document introduces the rationale, goals, constraints, core concepts, and trade-offs behind `buildcharts`. It is intentionally pre-implementation and focuses on architectural direction rather than concrete implementation details.

## Document purpose

**Audience**

This document is intended for architects, principal engineers, platform engineers, and reviewers evaluating `buildcharts` as a standardized CI/CD build-plan generator.

**Objectives**

- Describe the problem `buildcharts` is trying to solve.
- Explain the core architecture and execution model.
- Capture design principles, scope, and non-goals.
- Identify trade-offs, risks, governance concerns, and future refinement areas.

## Concept

`buildcharts` is a framework for defining and generating CI/CD build plans using declarative project metadata (`build.yml`) and reusable, versioned pipeline templates distributed as OCI artifacts, conceptually similar to Helm charts. It generates executable build plans (Docker Buildx Bake files) that can be run identically in CI and locally.

- Define build intent in `build.yml` with targets such as `build`, `test`, `nuget`, and `docker`.
- Each target maps to a chart‑hosted Dockerfile.
- Charts are versioned OCI artifacts.
- Each chart defines Docker build stages with target implementation, for example `dotnet build`.
- `buildcharts` resolves those templates and renders a deterministic Buildx Bake file such as `docker-bake.hcl`.
- Execution happens with standard Docker tooling through `docker buildx bake`.

## Problem domain

At scale, CI/CD systems tend to suffer from the same structural issues:

- Pipeline YAML is duplicated across many repositories with minor variations.
- Shared templates are hard to version, test, and roll out incrementally.
- CI behavior diverges from local developer workflows.
- CI providers become tightly coupled to pipeline logic.
- Small pipeline changes have disproportionately large blast radius.
- Vendor lock-in.
- Centralized YAML tends to be hard to maintain and lacks proper rollout support.

The core issue is not a lack of CI tooling, but the absence of a portable, declarative abstraction for build intent that cleanly separates what a project needs to build from how that build is implemented.

## Motivation

This effort came after running into over-complex, hard-to-troubleshoot CI/CD setups across multiple cloud providers and platforms. Seeing different strategies across organizations made the fragmentation obvious:

- Shared and nested YAML templates.
- Mixed versioning across branches, tags, and folders.
- Often platform-specific, leading to vendor lock-in.
- Not all platforms support dynamically created downstream pipelines.
- Reusable actions as pipeline steps are not consistently supported across platforms.
- Mixed scripting choices such as PowerShell, Bash, .NET scripts, and Dockerfiles, which often reflect developer preference more than architectural intent.
- Shifting requirements and weak discipline often turn a shared pipeline into incompatible variants.
- Not locally debuggable; troubleshooting requires repeated pipeline runs, which is slow and error-prone.
- Updates are fragile due to unintended breaking changes.
- Template-based creation is easy to start with but often lacks a lifecycle perspective.

Keeping pipelines up to date is also hard: changing a template is easy, but rolling it out across every service is not, so teams drift on old versions or migrate without knowing what changed. Platform teams try to lead the rollout but hit new requirements and inflexible solutions, which turns into long troubleshooting sessions and painful local setup to mimic CI, often with hidden secrets and configuration.

*The goal is a single, portable approach that replaces ad-hoc scripts with consistent Docker-based build steps while staying flexible and provider-agnostic.*

## Constraints and assumptions

### Constraints and realities

- Repositories are heterogeneous across languages, outputs, and tooling.
- CI providers vary, but build logic should remain consistent.
- Developers need fast local feedback loops.
- Platform teams need a controlled way to evolve build logic centrally.
- Pipeline changes must scale across many services without mass rewrites.

These constraints rule out fully bespoke per-repository pipelines, fully centralized provider-specific pipelines, and CI-only execution models that cannot be reproduced locally.

### Assumptions

- Build pipelines are platform infrastructure, not application logic.
- Build logic should be versioned, reviewed, and distributed like code.
- Most repositories fit into a small number of repeatable build patterns.
- Declarative metadata is easier to reason about, validate, and govern than imperative pipeline scripts.
- Docker, BuildKit, and Buildx provide a sufficiently expressive and portable execution substrate.

## Primary personas

### Platform engineer

As a platform engineer, I want to define and version build templates centrally so that I can evolve CI/CD behavior across all services without modifying every repository individually.

**Goals**

- Standardize build logic and security practices.
- Reduce pipeline maintenance surface area.
- Roll out changes safely across many services.

**Pain points**

- Diverging pipelines with unclear ownership.
- Risky global changes and unclear blast radius.
- Poor visibility into how pipelines are constructed.

### Service owner / application developer

As a service owner, I want to declare what my project produces and requires, not how CI works, so that I can focus on application logic instead of pipeline mechanics.

**Goals**

- Ship code quickly with minimal CI friction.
- Debug builds locally using familiar tools.
- Avoid becoming a CI expert.

**Pain points**

- Copy-pasted YAML nobody fully understands.
- CI failures that cannot be reproduced locally.
- Fear of touching pipeline definitions.

## Goals

1. Centralize and standardize pipeline logic in Dockerfiles stored as versioned OCI charts.
2. Allow chart updates to be automated via `Chart.yaml` and tools like Dependabot or Renovate.
3. Keep the developer experience friendly by expressing build intent in `build.yml` while abstracting implementation complexity behind charts.
4. Remain extensible through custom OCI charts and plugins.
5. Scale across many repositories and services using shared, versioned templates.
6. Dynamically generate build pipelines where Dockerfile targets replace provider-specific steps.
7. Be locally debuggable.
8. Keep tooling familiar through Docker, BuildKit, and Buildx Bake.
9. Support optional lock files with SHA digests for repeatable builds.
10. Support version selection, including pinned versions for controlled updates and floating tags such as `latest` for faster updates with higher risk.
11. Be platform-agnostic, reproducible, parallelized, cache-friendly, and containerized through Docker Buildx Bake.

### Success criteria

- Measurable reduction in duplicated CI configuration across repositories.
- Higher percentage of builds reproducible locally with matching CI results.
- Lower mean time to diagnose pipeline failures.
- Increased adoption of charts and automated rollout workflows.

## Design principles

- **Separation of intent and implementation:** repositories declare what to build; platform-owned charts define how it runs.
- **Versioned, immutable pipeline logic:** charts are treated as artifacts, not mutable scripts.
- **Local parity with CI:** the same rendered plan should run locally and in CI.
- **Minimal CI-provider coupling:** pipelines are generated rather than handwritten for each provider.
- **Performance by default:** parallelism, caching, and reuse are first-class concerns.

## Non-goals

- Supporting non-containerized build workflows, though additional generators such as Buildah, Kaniko, or Podman could be explored later.
- Replacing CI/CD platforms.
- Replacing release orchestration systems that coordinate multi-repository release channels.
- Eliminating the need to understand Dockerfiles or container build basics.
- Providing a toolchain for packaging or publishing charts.
- Providing a toolchain for signing and verifying charts.
- Providing scheduling, secrets, or artifact hosting.
- Managing deployments or runtime configuration; `buildcharts` is purely for builds.

## Scope

### Packaging OCI charts/templates

- `buildcharts` does not mandate a specific toolchain for producing or publishing charts. Any OCI-compliant CLI may be used to package and push OCI charts to a registry, including but not limited to Helm and ORAS.
- Helm is commonly used for its mature chart packaging and templating capabilities, but it is not a runtime dependency of `buildcharts` itself. The `buildcharts` CLI consumes OCI artifacts by reference, including registry, repository, tag, and digest, and remains agnostic to the tool used to produce them.
- Published charts should be treated as immutable artifacts. Rollout happens through version changes and digest pinning, not by mutating released artifacts.

### Provenance and signing

- `buildcharts` does not introduce a custom signing or trust mechanism. Artifact provenance and authenticity are delegated to OCI-native signing solutions.
- Depending on organizational requirements, charts may be signed using Notation (Notary v2) or Cosign, which attach signatures as OCI referrer artifacts and enable registry-native policy enforcement.
- Helm chart provenance (`.prov`) may also be used where compatibility with existing Helm-based workflows matters.
- `buildcharts` only requires that artifacts be verifiable at consumption time; signature creation, key management, and trust policy enforcement are intentionally externalized to standard OCI tooling.

## Features

- **Metadata-driven builds:** Projects declare their build intent in `build.yml`, listing variables and targets such as build, tests, NuGet packages, or Docker image creation. The file is intentionally concise and stable, and each target maps to a chart alias such as `build`, `test`, `nuget`, or `docker`.
- **Chart-based templates:** Charts point to OCI-hosted Dockerfile templates that hold the actual pipeline logic. Pulling these charts centralizes implementation details while letting repositories keep lightweight metadata.
- **Deterministic generation:** Given the same `build.yml`, chart references, and lock file inputs, the generator should render the same Bake plan, which is essential for reproducibility and debugging.
- **Reproducible builds:** Lock files, using the same format as Helm's `Chart.lock`, pin exact chart versions and digests to reduce drift from upstream chart changes.
- **CLI workflow:** The `buildcharts` CLI resolves chart dependencies, validates metadata, and renders CI/CD artifacts. Running `buildcharts generate` produces a Docker Buildx Bake file such as `docker-bake.hcl`, which is then executed with `docker buildx bake`.
- **Parallelized builds:** Buildx Bake plans enable parallel target execution, speeding multi-target or multi-architecture builds while reusing shared steps through BuildKit caching.
- **Targeted runs:** Bake targets can be invoked directly, for example `docker buildx bake test`, so teams can build a single image or run a single export target without executing the full pipeline.
- **De-duplicated shared stages:** Bake coordinates multiple Docker builds while each Dockerfile owns its dependency stages. BuildKit caching lets Bake de-duplicate shared stages across different Dockerfiles, reducing repeated work.
- **Reusable outputs:** By relying on Docker BuildKit and templated charts, builds run in consistent containers and can emit images, test results, and other artifacts without duplicating pipeline YAML across services.
- **Caching:** Docker layer caching can be more effective and simpler than platform-provided pipeline caching, especially when using BuildKit cache exports for ecosystems such as NuGet or npm.
- **Platform-agnostic execution:** The same Bake plan runs across CI providers and locally with Docker, making pipelines easier to reproduce, debug, and keep consistent.
- **Extensibility:** Teams can add new Dockerfiles and targets to define custom pipelines, such as building database migration images or running E2E test containers.
- **Secret handling:** BuildKit supports secret mounts through `RUN --mount=type=secret` so credentials can be provided at build time without baking them into images or logs.
- **Separation:** Intent in `build.yml` defines what to build; implementation in charts and templates defines how it runs.
- **No runtime dependency on packaging tools:** `buildcharts` validates and consumes OCI artifacts by digest and does not depend on the presence of Helm, ORAS, or any other specific packaging tool at runtime.

## Compatibility, diagnostics, and evolution

- Formal schema versioning for `build.yml` should be defined early so repositories can evolve without ambiguous breaking changes.
- Charts should have explicit compatibility and deprecation policies, especially for major version transitions and target-level behavior changes.
- Render-time validation should produce clear error messages that point back to the originating metadata and chart inputs.
- Diagnostics should make it easy to trace generated Bake targets back to source metadata, chart version, and template origin.
- Migration guidance should exist for schema upgrades, chart major version changes, and deprecated targets.

## Example workflows

### Repository author flow

1. Author writes or updates `build.yml`.
2. Optional lock file is created or updated for chart versions and digests.
3. The generator produces a Bake plan.
4. The plan is executed locally and in CI.

### Platform team flow

1. Publish a versioned OCI chart.
2. Validate it with tests, linting, static analysis, and security checks.
3. Roll it out through Dependabot, Renovate, or explicit repository updates.

## Maintainability and version control

- **Central chart updates with opt-in overrides:** Because templates are delivered as versioned OCI charts, platform teams can update templates in one place and let services consume new capabilities. Individual repositories can override specific chart versions when needed, trading speed for stability.
- **Lock files for repeatability:** `buildcharts update` produces optional lock files for pinning chart digests, enabling reproducible, immutable builds across local builds and CI runners while still allowing intentional bumps.
- **Automated dependency hygiene:** OCI chart versioning means Renovate or Dependabot can automatically propose version bumps for chart dependencies, keeping pipelines current without manual toil.
- **Governance:** Storing charts in a dedicated Git repository enables governance through standard branch protection, mandatory PR reviews, and required automated quality gates. These gates should include Dockerfile linting, static analysis, policy checks, and security scanning before charts are published as OCI artifacts. Continuous delivery can then publish the resulting OCI artifacts to a preferred container registry.

## Pros

- `buildcharts` is not a CI system; it is a build-plan generator that works with zero background infrastructure.
- Zero platform tax: no CI cluster to maintain, no HA concerns, no upgrades, no authentication plumbing, no databases, and no persistent state.
- True local parity: local runs are identical to CI runs, using the same generated Bake file, the same Dockerfile logic, and the same BuildKit cache behavior.
- Near-zero adoption friction for teams that already use Docker and Dockerfiles.
- `buildcharts` competes directly with hand-written CI YAML, copy-pasted pipelines, per-repository Docker build logic, and ad-hoc scripting.
- The deliberate trade-off is less native job orchestration in exchange for zero infrastructure, full local reproducibility, strong standardization, low operational cost, and provider neutrality.
- There is no new execution DSL beyond the metadata layer because execution stays in Dockerfiles and standard container tooling.

## Cons

- Strong dependence on Docker and Buildx as the execution substrate.
- Requires Docker and Buildx availability, which can be heavy for some CI runners or developers.
- Chart indirection adds a learning curve; teams must understand both metadata and underlying templates to debug issues.
- Ecosystem maturity of charts and plugins determines how many scenarios work out of the box; custom needs may require authoring new charts.
- OCI charts and generated artifacts add more layers to inspect when troubleshooting failures.

## Critical concerns

- **Docker dependency risk:** Tying builds so tightly to Docker tooling such as Buildx, BuildKit, and Bake creates a supply-chain dependency; tooling changes, licensing shifts, or major CVEs can impact all pipelines.
- **buildcharts dependency risk:** Tying builds to `buildcharts` creates generator dependency risk. As a fallback, teams can commit the generated `docker-bake.hcl` or inline Dockerfiles to remove the runtime generator dependency, at the cost of drifting from upstream chart updates.
- **Operational fragility for non-Docker shops:** Teams without deep Docker expertise face a steep operational burden when debugging BuildKit or Bake issues, configuring registry auth, and managing remote builders.
- **Scale and caching pitfalls:** Parallel Bake plans can saturate shared runners or exhaust cache storage. Mis-tuned cache exports and imports can lead to slow, flaky builds that are harder to diagnose than straightforward CI steps.
- **Opaque template failures:** Chart indirection can obscure root causes; when generation or rendering breaks, engineers may have to inspect templated artifacts, OCI chart versions, and CLI behavior before finding the issue.
- **Pull-based OCI supply chain:** OCI distribution introduces dependencies on registry uptime, authentication, and artifact integrity. This is partially mitigated by cached chart digests for offline-friendly operations.
- **Chart integrity and provenance:** Without signing or verification, chart artifacts remain a supply-chain risk even when pinned by digest.
- **Builder infrastructure drift:** Buildx builder version or configuration differences can cause inconsistent results across environments.
- **Secrets lifecycle:** Secret injection uses BuildKit mounts and plugins, but provisioning and auditability still depend on the CI provider and surrounding platform controls.

## When this fits

- Teams already standardized on Docker and BuildKit with reliable registry access and cache infrastructure.
- Organizations that want to centralize CI logic and keep service repositories thin.
- Projects that benefit from parallel builds.
- Platforms that can invest in maintaining a curated chart ecosystem.
- Teams that want faster builds through parallelization and de-duplication, plus provider-agnostic workflows that are easy to debug locally with Docker.
- Environments where the added complexity pays off, especially microservice-heavy architectures.

## When it doesn't

- Environments that cannot run Docker daemons or lack BuildKit support.
- Teams without Docker expertise or with highly bespoke build steps.
- CI runners with tight CPU, memory, or storage budgets where Bake parallelism backfires.
- Regulated environments that require fully signed, tightly locked-down build chains unless the surrounding OCI and signing story is made explicit.

## Quality and feasibility

`buildcharts` is practical for teams invested in containerized builds and aligns with established patterns such as Helm-like artifact distribution and BuildKit-based execution. Quality hinges on the chart ecosystem and how well it is maintained, governed, and documented. On the flip side, organizations without strong Docker and OCI support or with highly bespoke pipelines will likely adopt more slowly due to the abstraction and dependency footprint. The centralized chart model reduces duplicated build logic across repositories, improving consistency and easing maintenance.

## Comparison

- https://github.com/dagger/dagger - Programmable, container-native build pipelines with strong local and CI parity, but introduces a new execution engine and SDK-driven workflow model.
- https://github.com/earthly/earthly - Build-centric automation on top of BuildKit that runs locally and in CI, but uses its own `Earthfile` DSL instead of OCI-hosted template artifacts.
- https://github.com/tektoncd/pipeline - Kubernetes-native CI/CD pipelines that can distribute definitions as OCI bundles, powerful at scale but dependent on a Kubernetes control plane and not local-first.
- Buildkite - Hosted or self-hosted job orchestration with strong pipeline ergonomics, but still centered on provider-managed execution rather than generated local-first build plans.
- Bazel - Excellent for hermetic builds and dependency graph execution, but it is a broader build system with a much steeper adoption and migration cost.
- Nix - Strong reproducibility and environment modeling, but with a very different ecosystem and learning curve than Docker-first teams typically expect.

## Glossary

- **Chart:** Versioned build implementation artifact.
- **Generator:** Tool that renders execution plans from metadata and charts.
- **OCI artifact:** Registry-distributed artifact format used for versioned chart delivery.
- **Bake plan:** A Buildx Bake definition used to execute parallelized builds.

## Prompts

```text
Review buildcharts from a technical principal engineering perspective.
- Are there any similar tools.
- Compare these for CI/CD pipelines.
- Also buildcharts run locally without any hosted service.
- Why buildcharts is different.
- Is it a good idea?
- Innovation perspective (containers for runtime, helm for deployments, buildcharts for builds).
- Give me DX perspective.
- Give me internal RFC.

https://raw.githubusercontent.com/eddietisma/buildcharts/refs/heads/main/docs/assessment.md
```

```text
You are a Platform Engineer reviewing a CI build tool proposal (`buildcharts`). Produce a structured technical review with:
- Architecture critique: inputs (`build.yml`), chart/template model, OCI distribution, lockfile/digest pinning, BuildKit/Buildx/Bake execution, artifact outputs.
- Correctness and reproducibility.
- Performance and caching.
- Developer experience and debuggability.
- Security and supply-chain integrity.
- Maintainability and extensibility.
- Portability (Linux/macOS/Windows) and local/CI parity.
- CI/CD integration.
- Governance at scale (100s of repos).
- Identify similar tools and compare trade-offs for CI/CD pipelines.

https://raw.githubusercontent.com/eddietisma/buildcharts/refs/heads/main/docs/assessment.md
```
