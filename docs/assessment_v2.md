# BuildCharts — Architectural Assessment

## Purpose

BuildCharts is a framework for generating CI/CD pipelines by separating **build intent** from **build implementation**.

Instead of defining CI pipelines directly in provider-specific YAML, repositories declare a small, stable set of metadata in `build.yaml`. That metadata is then combined with reusable, versioned **charts** (OCI-hosted templates) to produce a concrete execution plan, typically a `docker-bake.hcl`, which is executed using Docker Buildx and BuildKit.

The goal is to make CI pipelines:
- Easier to reason about
- Easier to evolve safely
- Easier to reproduce locally
- Easier to standardize across large numbers of repositories

---

## Problem Statement

At scale, CI/CD systems tend to suffer from the same structural issues:

- Pipeline YAML is duplicated across many repositories with minor variations
- Shared templates are hard to version, test, and roll out incrementally
- CI behavior diverges from local developer workflows
- CI providers become tightly coupled to pipeline logic
- Small pipeline changes have disproportionately large blast radii

These problems worsen as the number of services grows, particularly in microservice-oriented environments.

BuildCharts addresses this by introducing a **metadata-driven pipeline model** with **versioned, container-native distribution of pipeline logic**.

---

## Core Concepts

### Build Metadata (`build.yaml`)

`build.yaml` expresses *what* should be built, not *how* it is built.  
It is intentionally concise and stable, designed to change infrequently.

Typical concerns expressed here include:
- Project type (e.g., library, service, image)
- Build outputs
- Target platforms
- High-level build options

The file is not a pipeline. It is a declarative description of intent.

---

### Charts (OCI Artifacts)

Charts encapsulate build implementation details:
- Dockerfile templates
- Bake target definitions
- CI integration logic
- Conventions and defaults

Charts are:
- Versioned
- Distributed as OCI artifacts
- Immutable once published

This allows pipeline logic to evolve independently of application repositories and enables controlled rollout of changes using standard dependency tooling (e.g., Renovate, Dependabot).

---

### Generator

The generator combines:
- `build.yaml`
- One or more charts
- Optional lock files

and produces a concrete execution plan (e.g., `docker-bake.hcl`).

The generator is deterministic: given the same inputs, it produces the same output. This property is essential for reproducibility and debugging.

---

### Execution Engine

BuildCharts delegates execution to Docker Buildx / BuildKit.

This provides:
- Parallel execution of build targets
- Shared layer caching across repositories
- Consistent behavior locally and in CI
- Access to advanced BuildKit features (secrets, mounts, cache exports)

BuildCharts intentionally does not implement its own execution engine.

---

## Design Principles

- **Separation of intent and implementation**  
  Application repositories declare intent; platform teams own implementation.

- **Versioned, immutable pipeline logic**  
  Charts are treated as artifacts, not scripts.

- **Local parity with CI**  
  The same plan can be executed locally and in CI.

- **Minimal CI-provider coupling**  
  Pipelines are generated, not handwritten per provider.

- **Performance by default**  
  Parallelism and caching are first-class concerns.

---

## Non-Goals

BuildCharts explicitly does not attempt to:
- Replace CI providers
- Reimplement scheduling, RBAC, or approvals
- Abstract every CI provider feature
- Eliminate Docker from the build process

BuildCharts assumes a Docker- and BuildKit-centric environment.

---

## Benefits

### Standardization at Scale

By centralizing pipeline logic in charts, organizations can:
- Enforce consistent build practices
- Reduce duplication
- Apply changes across many repositories safely

---

### Safer Evolution

Versioned charts allow pipeline changes to be:
- Reviewed independently
- Tested in isolation
- Rolled out incrementally

This reduces the blast radius of CI changes.

---

### Local Reproducibility

Because pipelines are rendered into executable build plans, developers can:
- Run CI-equivalent builds locally
- Debug failures without waiting for CI
- Inspect generated plans directly

---

### Performance and Cost Efficiency

Using Buildx Bake and BuildKit enables:
- Parallel builds across services and targets
- Shared caching
- Reduced CI runtime and resource usage

---

## Trade-offs and Risks

### Docker Buildx Dependency

BuildCharts is tightly coupled to Docker Buildx and BuildKit.  
This is an intentional design decision, but it limits applicability in environments where these tools are unavailable or unsupported.

---

### Increased Indirection

Introducing charts and generation adds an abstraction layer.  
While this improves maintainability, it can increase initial debugging complexity if tooling and diagnostics are insufficient.

---

### Supply Chain Considerations

Charts are part of the build supply chain.  
They must be treated with the same care as base images:
- Versioned
- Reviewed
- Trusted
- Audited

---

## Future Considerations

Areas identified for further refinement include:
- Formal schema versioning for `build.yaml`
- Clear compatibility and deprecation policies for charts
- Enhanced diagnostics and render-time validation
- Stronger guidance around secrets and artifact provenance

---

## Summary

BuildCharts provides a structured approach to CI/CD pipeline generation that scales with organizational size and complexity. By separating intent from implementation and leveraging container-native tooling, it enables safer evolution, better performance, and improved developer experience.

It is not a universal solution for all CI problems, but for Docker-centric organizations with many similar services, it offers a pragmatic and principled foundation for build standardization.
