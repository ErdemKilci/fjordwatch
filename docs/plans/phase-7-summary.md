# Phase 7 summary — Cloud deployment (optional)

## What was built

### Bicep templates
- `infrastructure/bicep/main.bicep` — entry-point that wires every module and exposes outputs (web FQDN, ACR login server, Key Vault URI, App Insights connection string).
- `infrastructure/bicep/parameters.dev.bicepparam` — dev environment defaults; `postgresAdminPassword` is intentionally not committed.
- `modules/registry.bicep` — Standard ACR with admin disabled.
- `modules/postgres.bicep` — Flexible Server B1ms with `POSTGIS,VECTOR,PG_STAT_STATEMENTS` in `azure.extensions`, the `fjordwatch` database, and the "allow Azure services" firewall rule.
- `modules/storage.bicep` — StorageV2 + Hot tier with `sar-tiles` and `fixtures` containers.
- `modules/keyvault.bicep` — Key Vault with RBAC mode and a 7-day soft-delete window.
- `modules/insights.bicep` — Log Analytics workspace + Application Insights wired to the workspace via `LogAnalytics` ingestion mode.
- `modules/aca-env.bicep` — Container Apps Environment streaming logs to Log Analytics.
- `modules/aca-app.bicep` — generic Container App module reused by every service. Liveness probe on `/healthz`, readiness probe on `/readyz`, optional HTTP scale rule, KEDA min/max replicas.

### Deploy workflow + Make targets
- `.github/workflows/deploy.yml` — manual `workflow_dispatch` only, three jobs: Bicep what-if, image build matrix (one job per service via `az acr build`), and apply. Image build + apply are gated on `dry_run=false`.
- `Makefile` gains `deploy`, `deploy-rg`, `deploy-bicep`, `deploy-images` targets. `DRY_RUN=1` switches Bicep to what-if.

### Docs
- `docs/deployment.md` — runbook including OIDC federation setup, the three-step deploy flow, a per-resource cost table (~32 EUR/mo idle, ~42 EUR/mo with 4 hours/day of activity), rollback, and the phase-7 follow-up list.

## Verification

| Gate | Result |
|---|---|
| Bicep templates structurally consistent (modules, outputs, params). | Visual review. CI runs `bicep build` via the deploy workflow's `bicep-validate` job (workflow_dispatch only). |
| `make deploy DRY_RUN=1 POSTGRES_ADMIN_PASSWORD=...` runs `az deployment group what-if` end to end. | Manual when `az` is installed. |
| Local docker compose stack still works. | `make up`, no changes to `docker-compose.yml` or service Dockerfiles in this PR. |

## Deviations from spec (and rationale)

- **No auto-deploy on push to `main`.** The deploy workflow is `workflow_dispatch` only. Auto-deploy would surprise the developer with cloud charges; opt-in is the right default for a portfolio project.
- **Redis as a sidecar Container App, not Azure Cache for Redis.** The smallest Azure Cache for Redis (Basic C0) starts at ~16 EUR/mo; doubling the idle floor for a feature most demos do not need is the wrong trade-off. Documented in `docs/deployment.md` with the upgrade path called out.
- **Ollama is not deployed to the cloud.** Container Apps does not support GPU-backed workload profiles by default; a CPU-only Ollama on a Container App would be unbearably slow for a demo. The cloud profile defaults to `LLM_PROVIDER=azure_openai`.
- **No Bicep `--mode Complete`.** Complete mode deletes resources outside the deployment scope; we want predictable diffs without the foot-gun. Default `Incremental` mode is used.
- **No multi-region.** Single Norway East region. Phase 7 polish would add a Front Door and a second region.
- **No CD on PR merges.** The deploy workflow is on `workflow_dispatch` to avoid drift between PR review and apply. The PR-time Bicep validation is in the `bicep-validate` job which runs the same `what-if` command but does not require dispatch (could be promoted to PR-trigger in a polish pass).

## What was deferred

- Private endpoints for Postgres and Storage.
- Front Door, custom hostname, TLS certs for the public-facing web app.
- A `staging` parameters file.
- Cost dashboard / budget alerts.
- Image signing (Notary, OCI signatures).
- Disaster-recovery runbook.

## Manual steps for the developer

1. **One-time: provision the federated identity and GitHub secrets** (full commands in `docs/deployment.md`).
2. **Validate Bicep with `make deploy DRY_RUN=1`** in your subscription. The output should show every resource as a planned `Create`.
3. **Apply with `make deploy`** to bring the stack up. First apply takes ~12 minutes (Postgres provisioning is the long pole).
4. **Run migrations against the cloud Postgres:**
   ```bash
   export DATABASE_URL=postgres://fjordwatch_admin:<pwd>@<server>.postgres.database.azure.com:5432/fjordwatch?sslmode=require
   docker compose run --rm db-migrate -url=jdbc:postgresql://<server>.postgres.database.azure.com:5432/fjordwatch \
       -user=fjordwatch_admin -password="<pwd>" -baselineOnMigrate=true migrate
   ```
5. **Verify** by hitting the web URL Bicep outputs: `https://fjordwatch-dev-web.<env>.<region>.azurecontainerapps.io`.

## Risks remaining

- **Postgres Flexible Server extension allowlist drift.** `POSTGIS,VECTOR,PG_STAT_STATEMENTS` are accepted today; Microsoft can rotate the list. The runbook links the Microsoft Learn page that lists supported extensions.
- **OIDC federated credential setup is the most common first-run failure.** Documented step-by-step in `docs/deployment.md`.
- **Egress costs.** Sentinel-1 tiles can run to several GB/day; first 5 GB/month from Storage is free, beyond that ~0.08 EUR/GB. Phase 7 polish adds a retention policy on the SAR pipeline.
- **Cold starts.** With `minReplicas=0` on `core-api` and `web`, the first request after an idle period takes 4–8 seconds. Acceptable for a demo; the runbook documents pinning `minReplicas=1` for live presentations.

## What's next

The build phases are complete. Phase 8 is the README finalize, GitHub repo
configuration (topics, About, social preview), and the manual demo recording
session. None of these require code changes; the developer drives them
once the project is ready to share.
