# Phase 7 plan — Cloud deployment (optional, additive)

## Goal
A developer with an Azure subscription runs `make deploy` and sees a public
FjordWatch instance in Norway North within 20 minutes. Costs default to a
scale-to-zero footprint that idles below 30 EUR/month. The local docker
compose stack continues to work unchanged.

## Files to create

### Bicep
1. `infrastructure/bicep/main.bicep` — entry-point template that wires every module and exposes outputs (FQDN of the web app, ACR login server, Key Vault URI, App Insights connection string).
2. `infrastructure/bicep/parameters.dev.bicepparam` — dev environment defaults.
3. `infrastructure/bicep/modules/registry.bicep` — Azure Container Registry (Standard SKU, admin enabled false; the deploy workflow uses an OIDC-federated managed identity).
4. `infrastructure/bicep/modules/postgres.bicep` — Postgres Flexible Server with `azure_ai`, `pg_stat_statements`, `pgvector`, and `postgis` extensions in `azure.extensions`. Public access on the dev SKU; private endpoints noted as a phase-7 follow-up.
5. `infrastructure/bicep/modules/storage.bicep` — Storage Account (StorageV2 + Hot tier) with `sar-tiles` and `fixtures` containers.
6. `infrastructure/bicep/modules/keyvault.bicep` — Key Vault with RBAC. The deploy workflow's MI gets `Key Vault Secrets User`.
7. `infrastructure/bicep/modules/insights.bicep` — Log Analytics workspace + Application Insights, wired so Container Apps stream to the workspace.
8. `infrastructure/bicep/modules/aca-env.bicep` — Container Apps Environment + a managed Redis (Container Apps managed Redis or `redislabs/redis-stack:7-alpine` as a sidecar; we use the latter to keep cost predictable).
9. `infrastructure/bicep/modules/aca-app.bicep` — generic Container App module parameterized by image, env vars, secrets, ingress, and min/max replicas. Reused by every service.

### Workflow + tooling
10. `.github/workflows/deploy.yml` — manual dispatch only by default (`on: workflow_dispatch`). Steps: Azure OIDC login, `az acr build` for every service in parallel, then `az deployment group create` against `main.bicep`.
11. `Makefile` gains `deploy`, `deploy-bicep`, `deploy-images` targets that wrap the same `az` calls. `make deploy DRY_RUN=1` runs `az deployment group what-if` for review.

### Docs
12. `docs/deployment.md` — runbook + cost estimate. Documents the assumed scale-to-zero idle (~5 EUR/month for ACR Standard + Storage + Log Analytics), the active-hour budget (~25 EUR/month with all six services pinned to 1 replica), and the phase-7 follow-ups (private endpoints, Front Door, Cosmos for vector at scale).

## Deviations from spec

- **`make deploy` requires Azure CLI + `bicep` CLI on the host.** No way around it; documented in the runbook.
- **No automatic deploy on push.** The workflow is `workflow_dispatch` only. A PR-trigger or main-trigger would surprise the developer with cloud charges; opt-in is the right default.
- **Redis runs as a sidecar Container App, not as Azure Cache for Redis.** ACR + Container Apps + Postgres + Storage + Key Vault + App Insights is already a substantial monthly bill; adding Azure Cache for Redis (smallest Basic SKU is ~16 EUR/month) doubles the floor for a feature most demos do not need. Sidecar Redis with persistent storage covers Streams + cache; the runbook calls out the trade-off.
- **No Bicep test harness.** Bicep `what-if` is the verification path; we do not stand up a parallel test resource group. The workflow has `--mode Complete` enabled for predictable diffs but only behind a guard variable so a misclick cannot delete unrelated resources.
- **No multi-region.** A single Norway North region keeps the demo simple; multi-region is a phase 7 polish.

## Verification

| Gate | How |
|---|---|
| `bicep build infrastructure/bicep/main.bicep` clean. | CI step in `deploy.yml` runs on PR. |
| `az deployment group what-if` against the dev parameter file is empty on the second run. | Runbook step. |
| `make deploy` end-to-end on a developer Azure subscription brings the web app up at `https://fjordwatch-web.<env>.<region>.azurecontainerapps.io`. | Manual once. |
| Cost report after 7 idle days is below 10 EUR. | Manual once. |

## Risks

- **PostGIS extension availability.** Postgres Flexible Server's allowlist for `azure.extensions` rotates; the runbook documents the current version and links to the Microsoft Learn page that lists supported extensions.
- **OIDC federation on the deploy workflow.** Requires a federated credential on a managed identity in the target tenant. The runbook includes the `az` commands; mistakes here are the most common cause of first-run failure.
- **Egress costs from MinIO -> Storage migration.** Sentinel-1 tiles can run to GBs/day; cloud Storage costs ~0.02 EUR/GB at rest plus egress. We default to scale-to-zero on the SAR pipeline and document the egress profile.
- **The local stack must keep working.** Every change in this phase is additive; no edits to `docker-compose.yml` or service Dockerfiles unless they are also necessary for cloud deployment (e.g., `${PORT}` env var support on Container Apps).
