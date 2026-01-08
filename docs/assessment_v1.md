# Assessment and Rationale

`buildcharts` is a framework for defining and generating CI/CD build plans using declarative project metadata (`build.yml`) and reusable, versioned pipeline templates distributed as OCI artifacts (conceptually similar to Helm charts). It generates executable build plans (Docker Buildx Bake files) that can be run identically in CI and locally.

---

## 1. Problem Domain

Modern organizations operating at scale typically maintain dozens or hundreds of repositories, each producing multiple artifacts (binaries, packages, container images, test outputs). CI/CD pipelines evolve organically across teams, providers, and time, leading to:

- Copy-pasted and diverging pipeline YAML
- Inconsistent build logic, security posture, and tooling
- Fragile upgrades and breaking changes
- High operational cost for platform teams
- Limited local debuggability and slow feedback loops

The core issue is not a lack of CI tooling, but the absence of a **portable, declarative abstraction for build intent** that cleanly separates *what a project needs to build* from *how that build is implemented*.

---

## 2. Constraints and Realities

Any solution in this space must operate within the following realities:

- Repositories are heterogeneous (languages, outputs, tooling)
- CI providers vary, but build logic should remain consistent
- Developers require fast, local feedback loops
- Platform teams need a controlled way to evolve build logic centrally
- Pipeline changes must scale across many services without mass rewrites

These constraints rule out:
- Fully bespoke per-repo pipelines
- Fully centralized, provider-specific pipelines
- CI-only execution models that cannot be reproduced locally

---

## 3. Assumptions

This project is based on the following assumptions:

- Build pipelines are platform infrastructure, not application logic
- Build logic should be versioned, reviewed, and distributed like code
- Most repositories fit into a small number of repeatable build patterns
- Declarative metadata is easier to reason about, validate, and govern than imperative pipeline scripts
- Docker and BuildKit provide a sufficiently expressive and portable execution substrate

---

## 4. Personas and User Stories

### 4.1 Platform Engineer

**Goals**
- Standardize build logic and security practices
- Reduce pipeline maintenance surface area
- Roll out changes safely across many services

**Pain Points**
- Diverging pipelines with unclear ownership
- Risky global changes and unclear blast radius
- Poor visibility into how pipelines are constructed

**User Story**
> As a platform engineer, I want to define and version build templates centrally so that I can evolve CI/CD behavior across all services without modifying every repository individually.

---

### 4.2 Service Owner / Application Developer

**Goals**
- Ship code quickly with minimal CI friction
- Debug builds locally using familiar tools
- Avoid becoming a CI expert

**Pain Points**
- Copy-pasted YAML nobody fully understands
- CI failures that cannot be reproduced locally
- Fear of touching pipeline definitions

**User Story**
> As a service owner, I want to declare what my project produces and requires, not how CI works, so that I can focus on application logic instead of pipeline mechanics.

---

## 5. Core Idea

- Projects declare **build intent** in `build.yml` (targets such as `build`, `test`, `nuget`, `docker`)
- Each target maps to a **chart-hosted Dockerfile/template** stored in an OCI registry
- `buildcharts` resolves these templates and renders a **Docker Buildx Bake plan** (e.g. `docker-bake.hcl`)
- Builds are executed using standard tooling:  
  `docker buildx bake`

This cleanly separates:
- **Intent** (metadata in repositories)
- **Implementation** (versioned templates owned by the platform)

---

## 6. Motivation

This project emerged from repeated exposure to over-complex, fragile CI/CD setups across organizations and cloud providers, especially at microservice scale:

- Deeply nested shared YAML templates
- Mixed versioning strategies (branches, tags, folders)
- Platform-specific features and vendor lock-in
- Inconsistent support for reusable steps or dynamic pipelines
- Arbitrary scripting choices (PowerShell, Bash, custom scripts)
- Poor local debuggability and slow trial-and-error CI feedback
- Fragile updates and unclear breaking changes
- Lack of lifecycle ownership for shared templates

Updating a template is easy; safely rolling it out across hundreds of services is not. The result is version drift, tribal knowledge, and brittle local CI emulation.

**The goal is a single, portable approach that replaces ad-hoc scripts with consistent, containerized build steps while remaining provider-agnostic and locally reproducible.**

---

## 7. Goals

- Centralize pipeline logic in Dockerfiles distributed as versioned OCI charts
- Allow automated chart updates via `Chart.yaml` and Renovate/Dependabot
- Express build intent declaratively in `build.yml`
- Abstract implementation complexity behind reusable templates
- Scale cleanly across many repositories and services
- Dynamically generate pipelines without provider-specific YAML
- Support local debugging with identical behavior to CI
- Reuse familiar tooling (Docker, BuildKit, Buildx Bake)
- Support optional lock files with SHA digests for reproducibility
- Allow both pinned and floating chart versions, aligned with migration strategies
- Enable parallel, cache-friendly, platform-agnostic builds

---

## 8. Non-Goals

- Supporting non-Docker/BuildKit execution environments  
  (additional generators like Buildah/Podman may be added later)
- Providing a hosted CI runner platform
- Replacing release orchestration or deployment systems
- Eliminating the need to understand Docker or container build fundamentals
- Deployment orchestration (out of scope; build-only)

---

## 9. Key Features

- **Metadata-driven builds:** Projects declare targets and variables in `build.yml`
- **OCI-hosted templates:** Build logic lives in versioned Dockerfile-based charts
- **CLI workflow:** `buildcharts generate` produces a Bake plan executed with Docker
- **Parallel execution:** Buildx Bake enables parallel targets and architectures
- **Targeted runs:** Individual targets can be executed directly
- **De-duplicated stages:** BuildKit caching reduces repeated work across targets
- **Reusable outputs:** Images, test results, and artifacts emitted consistently
- **Efficient caching:** BuildKit cache exports outperform many CI-native caches
- **Provider-agnostic execution:** Same plan runs locally and in CI
- **Extensibility:** Custom charts enable new pipelines (migrations, E2E, tooling)
- **Secure secret handling:** BuildKit secret mounts avoid leaking credentials
- **Clear separation:** Intent vs implementation is strictly enforced

---

## 10. Maintainability and Version Control

- **Centralized updates:** Charts are updated once and consumed everywhere
- **Opt-in overrides:** Repositories can pin versions when stability is required
- **Lock files:** Optional digest pinning enables fully reproducible builds
- **Automated hygiene:** Dependency bots manage chart upgrades
- **Governance:** Charts are reviewed, linted, validated, and published via CI
- **Policy enforcement:** Dockerfile linting, static analysis, and security checks gate publishing

---

## 11. Pros

- No CI infrastructure to operate or scale
- True local parity with CI
- Minimal adoption friction for Docker-based teams
- Strong standardization with low operational cost
- Provider neutrality
- Competes directly with hand-written YAML and ad-hoc scripting
- Explicit tradeoff: less native job orchestration for zero infrastructure and reproducibility

---

## 12. Cons

- Strong dependency on Docker/Buildx tooling
- Requires Docker availability and expertise
- Indirection through charts adds a learning curve
- Ecosystem quality depends on chart maturity
- Additional layers to inspect during troubleshooting

---

## 13. Critical Concerns

- **Docker supply-chain risk:** Buildx/BuildKit changes affect all pipelines
- **buildcharts dependency risk:** Generator dependency can be removed by committing generated artifacts, at the cost of drift
- **Operational burden:** Non-Docker-native teams may struggle
- **Caching pitfalls:** Misconfigured parallelism or caches can degrade reliability
- **Opaque failures:** Template indirection can obscure root causes
- **OCI distribution risk:** Registry availability and integrity are dependencies
- **Artifact provenance:** Unsigned charts remain a supply-chain risk
- **Builder drift:** Differences in Buildx configuration can cause inconsistencies
- **Secrets lifecycle:** Provisioning and auditing remain CI-provider concerns

---

## 14. When This Fits

- Docker-first organizations with BuildKit expertise
- Microservice-heavy architectures
- Teams seeking centralized CI logic with thin repos
- Platforms willing to curate and govern chart ecosystems
- Environments where parallel builds and caching matter

---

## 15. When It Doesn’t

- Environments without Docker daemon access
- Teams with highly bespoke build logic
- Resource-constrained CI runners
- Regulated environments requiring signed, locked-down build chains

---

## 16. Quality and Feasibility

This approach is viable and aligns with established infrastructure-as-code and Helm-style patterns. Its success depends primarily on the quality, governance, and evolution of the chart ecosystem. For Docker-centric organizations, it significantly reduces duplicated pipeline logic and improves consistency. For others, the abstraction and dependency footprint may outweigh the benefits.
