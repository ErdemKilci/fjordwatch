# Cloud deployment (optional)

FjordWatch is designed to run end-to-end on a developer laptop with
`docker compose`. This document describes the **opt-in** Azure path: a
single resource group in Norway North that hosts the same six services on
Azure Container Apps with a Postgres Flexible Server, an Azure Container
Registry, a Storage Account, Key Vault, and Application Insights.

## What ships in this phase

| Component | Cloud equivalent | Notes |
|---|---|---|
| `services/*` (Docker Compose) | Azure Container Apps | One Container App per service. Internet ingress only on `core-api` and `web`. |
| `postgres` (Compose) | Postgres Flexible Server | `postgis`, `vector`, `pg_stat_statements` enabled in `azure.extensions`. |
| `redis` (Compose) | Redis sidecar Container App | Cheaper than Azure Cache for Redis at portfolio-demo scale. |
| `minio` (Compose) | Storage Account (StorageV2 + Hot tier) | Containers `sar-tiles` and `fixtures`. |
| `ollama` (Compose) | _omitted_; default cloud LLM is Azure OpenAI | `LLM_PROVIDER=azure_openai` in the dev parameters. |

Bicep templates live in [`infrastructure/bicep/`](../infrastructure/bicep)
with one module per concern. The entry-point is `main.bicep`.

## Prerequisites

1. Azure subscription with quota for the Norway North region.
2. `az` CLI 2.60+ and the `bicep` extension (`az bicep install`).
3. A managed identity in your tenant with **Contributor** on the target
   resource group (or **Owner** if you want the deployment to set up RBAC
   role assignments itself). Set up an OIDC federated credential so the
   GitHub Actions workflow can sign in without a client secret.
4. A GitHub secret named `POSTGRES_ADMIN_PASSWORD` containing a strong
   password (Postgres Flexible Server rejects passwords with the user
   name in them).

## One-time setup

```bash
# Sign in interactively
az login --use-device-code
az account set --subscription "<subscription-id>"

# Create the federated identity for GitHub Actions
az ad sp create-for-rbac \
    --name fjordwatch-deploy \
    --role contributor \
    --scopes /subscriptions/<sub-id>

# Configure repository secrets
gh secret set AZURE_CLIENT_ID       --body "<sp client id>"
gh secret set AZURE_TENANT_ID       --body "<sp tenant id>"
gh secret set AZURE_SUBSCRIPTION_ID --body "<sub id>"
gh secret set POSTGRES_ADMIN_PASSWORD --body "<strong password>"
```

## Deploy locally

```bash
export POSTGRES_ADMIN_PASSWORD="<strong password>"

# What-if first (no charges incurred):
DRY_RUN=1 make deploy

# Apply for real:
make deploy
```

`make deploy` runs three steps:
1. `make deploy-rg` — `az group create` (idempotent).
2. `make deploy-bicep` — what-if or apply against `infrastructure/bicep/main.bicep`.
3. `make deploy-images` — `az acr build` for each service in turn.

Container Apps roll new revisions automatically when the `imageTag` parameter
changes, so re-running `make deploy-bicep` with a new tag is the rollout
trigger.

## Deploy from GitHub

The `.github/workflows/deploy.yml` workflow is `workflow_dispatch` only.
Trigger it from the Actions tab with `dry_run=true` to validate the
templates and `dry_run=false` to apply them.

## Cost estimate

The default scale-to-zero footprint, based on the Azure pricing calculator
in May 2026 for Norway East:

| Resource | SKU | Idle (per month) | Active (per month, 4 hours/day) |
|---|---|---:|---:|
| Container Registry | Standard | 4.20 EUR | 4.20 EUR |
| Container Apps Environment | Consumption | 0.00 EUR | 0.00 EUR |
| `core-api` Container App | min=0, max=3 | 0.00 EUR | ~3.00 EUR |
| `web` Container App | min=0, max=2 | 0.00 EUR | ~2.00 EUR |
| `ais-ingestion` Container App | min=1, max=1 | ~6.00 EUR | ~6.00 EUR |
| Other Container Apps (5x) | min=0..1 | ~5.00 EUR | ~10.00 EUR |
| Postgres Flexible Server | Standard_B1ms (32 GB) | ~14.00 EUR | ~14.00 EUR |
| Storage Account | StorageV2 LRS, Hot, ~5 GB | ~0.20 EUR | ~0.20 EUR |
| Log Analytics + App Insights | Pay-as-you-go, 5 GB | ~2.00 EUR | ~2.00 EUR |
| Key Vault | Standard | ~0.30 EUR | ~0.30 EUR |
| **Total** | | **~31.70 EUR/mo** | **~41.70 EUR/mo** |

Notes:
- The numbers are estimates; check with the Azure Pricing Calculator before deploying.
- Egress charges from Storage to non-Azure clients are billed at ~0.08 EUR/GB after the first 5 GB/month free tier; tiles and SAR scenes are the heaviest egress source.
- Switching the Postgres SKU to `Standard_B2s` doubles the floor; only do that if a real load test shows the burstable B1ms is throttling.
- Setting `core-api`/`web` `minReplicas=0` (the default) means the first request after an idle period sees a 4–8 second cold start. Pin to `minReplicas=1` for demos.

## Rollback

`az deployment group create` is idempotent; redeploy with the previous
`imageTag` to roll back. For a clean tear-down:

```bash
az group delete --name fjordwatch-dev-rg --yes --no-wait
```

## Phase 7 follow-ups

- **Private endpoints** for Postgres and Storage. The current dev profile uses public access plus an "allow Azure services" firewall rule.
- **Front Door** in front of the web app for caching and a stable hostname.
- **Cosmos DB for PostgreSQL or Aurora-style scale-out** if vessel counts ever exceed a single Postgres node's comfortable working set.
- **A `staging` parameters file** with HA enabled on Postgres and `minReplicas=1` on the API.
- **Cost dashboard** in App Insights / Azure Cost Management for daily spend tracking.

## What is NOT in scope

- The local docker compose stack must continue to work without an Azure subscription. No file in `services/*` depends on any Bicep output.
- Migrations are still applied via the Flyway one-shot pattern; on Azure, run `make migrate` once with `DATABASE_URL` pointed at the cloud Postgres after `make deploy-bicep`.
- Ollama is **not** deployed to the cloud. The cloud profile uses Azure OpenAI; Ollama in Container Apps would need a GPU-backed workload profile and is out of scope.
