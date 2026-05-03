# Contributing to FjordWatch

FjordWatch is a personal portfolio project. External contributions are not actively solicited, but issues and pull requests are welcome.

## Ground rules

1. Read [`FjordWatch-SPEC.md`](./FjordWatch-SPEC.md) before opening a non-trivial PR. The spec is the source of truth for scope and architecture.
2. The project is independent and not affiliated with any organization. See [`DISCLAIMER.md`](./DISCLAIMER.md). Do not propose features that imply operational use.
3. Use only public, properly-licensed data and code. Document every new data source in [`docs/data-sources.md`](./docs/data-sources.md).
4. Never commit secrets. Configuration goes in `.env.example` with placeholder values.

## Workflow

- Branch off `main`: `git checkout -b feat/short-name`.
- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`, `build:`, `ci:`.
- Keep commits small and atomic.
- Open a pull request via `gh pr create`. Describe what changed, why, and how it was tested. Include screenshots for UI changes.

## Local development

Prerequisites: Docker, `make`, plus the toolchains for the services you touch (Rust, .NET 9 SDK, Python 3.12, Node.js for the e2e tests).

```bash
make env       # creates .env from .env.example
make up        # starts the local stack
make test      # runs all language test suites once they exist
make lint      # runs all linters
```

## Coding standards

- **Rust:** `cargo fmt`, `cargo clippy -- -D warnings`, `cargo test`.
- **.NET:** `dotnet format --verify-no-changes`, `dotnet build -warnaserror`, `dotnet test`.
- **Python:** `ruff format --check`, `ruff check`, `pyright` strict, `pytest`.
- **TypeScript (e2e):** `eslint`, `prettier --check`, `tsc --noEmit`, `playwright test`.
- **Containers:** every Dockerfile has a `HEALTHCHECK` and a non-root user.

## Testing

- New code has tests. The coverage gate is 70 percent lines, 60 percent branches.
- Tests must not depend on live external services. Use fixtures or replay files.
- ML training scripts use a fixed random seed.

## Architecture decisions

Non-trivial choices are recorded as Architecture Decision Records in [`docs/adr/`](./docs/adr/), numbered sequentially. Use [`0000-template.md`](./docs/adr/0000-template.md) as a starting point.

## Reporting issues

Use GitHub Issues. Include: what you expected, what happened, the relevant logs (redact anything sensitive), and the steps to reproduce.
