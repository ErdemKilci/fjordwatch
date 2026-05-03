// Dev environment parameters. Used by `make deploy` and the deploy workflow.
//
// `postgresAdminPassword` is intentionally NOT set here; the deploy workflow
// passes it via `--parameters postgresAdminPassword=<value>` from the GitHub
// secret `POSTGRES_ADMIN_PASSWORD`. `make deploy` reads it from the
// `POSTGRES_ADMIN_PASSWORD` env var so neither path commits a secret.

using './main.bicep'

param environment = 'dev'
param postgresAdminLogin = 'fjordwatch_admin'
param imageTag = 'dev'
param llmProvider = 'azure_openai'
