# Claude Code Kickoff Prompt — FjordWatch (with GitHub workflow)

> Copy everything below the divider into Claude Code as your first message. Make sure `FjordWatch-SPEC.md` is in the same working directory and that `gh` (GitHub CLI) is authenticated (`gh auth status` shows logged in).

---

You are building **FjordWatch**, a maritime intelligence platform for the Norwegian coast. The complete specification is in `FjordWatch-SPEC.md` in this directory. Read it fully before you do anything else.

## Your operating mode

You are working autonomously on a long-running project. The developer has limited time to babysit you. Optimize for **steady forward progress** with high code quality. Push commits to GitHub regularly so progress is visible.

### GitHub workflow

The repo should already be initialized and connected to GitHub via `gh` CLI before you start. Verify with `gh auth status` and `git remote -v`. If either is missing, stop and ask the developer to set them up.

For each phase:
- Work directly on `main` for phases 0 through 2 (early scaffolding moves fast).
- From phase 3 onward, create a feature branch per phase: `git checkout -b phase-{N}-{shortname}`. When the phase is done and verified, open a PR with `gh pr create`, write a meaningful PR body summarizing the phase, then merge it yourself with `gh pr merge --squash --delete-branch`. This produces clean commit history and visible PR activity, which is what hiring managers look at.
- Push intermediate work-in-progress commits to the branch as you go (`git push -u origin HEAD` on first push, `git push` after that). Do not wait until phase end.
- After each phase merge, tag the commit: `git tag -a phase-{N} -m "Phase {N} complete: {one-line summary}" && git push origin phase-{N}`.
- Use Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`, `build:`, `ci:`.

### Workflow loop (repeat for every phase 0 through 7)

For each phase, do exactly this in order:

1. **Read** the relevant phase section in `FjordWatch-SPEC.md`.
2. **Branch** (phase 3+) - `git checkout -b phase-{N}-{shortname}`.
3. **Plan** - write a 5-to-10 line plan listing the files you will create or modify and any deviation from the spec with one-sentence justification. Save this to `docs/plans/phase-{N}-plan.md`. Commit and push.
4. **Build** - implement the phase end to end. Commit at meaningful units of work using Conventional Commits. Push at least every 2-3 commits.
5. **Test** - run all linters, formatters, and tests for the languages touched in this phase. Fix everything red. Paste the final green output into the phase summary.
6. **Verify** - go through the phase's "Definition of done" checklist in the spec. Each item must be demonstrably true. If you cannot verify an item (for example, no GPU available for training), document it explicitly in `docs/plans/phase-{N}-summary.md` with a fallback that does not block the project (synthetic data, smaller model, mocked endpoint).
7. **Summarize** - write `docs/plans/phase-{N}-summary.md` with: what you built, what you skipped and why, what risks remain, what to verify manually, and what's next.
8. **Merge** (phase 3+) - `gh pr create --fill --base main` with a descriptive title and body, then `gh pr merge --squash --delete-branch`. For phases 0-2, just push to main directly.
9. **Tag** - `git tag -a phase-{N} -m "..."` and `git push origin phase-{N}`.
10. **Continue** - move directly to phase N+1 without waiting for confirmation, unless one of the stop conditions below is met.

### Stop conditions (ask the developer before continuing)

Stop and ask only if:
- A required external resource is unreachable and there is no documented fallback (for example, Kystverket TCP socket is down and you have no NMEA fixture file to replay).
- You hit a credential or secret you do not have (Azure subscription, Copernicus credentials, paid API key). Use environment variable placeholders and skip cloud-only steps; do not stop for this unless the entire phase becomes impossible.
- You discover a fundamental contradiction in the spec.
- A phase verification fails after three honest fix attempts.
- You would need to install something requiring sudo or system-level changes the developer hasn't approved.
- `gh` or `git` operations fail with auth errors that you cannot resolve.

For everything else, decide and document. Write an ADR in `docs/adr/` numbered sequentially.

### Things to do without asking

- Choose specific library versions, pin them, write down why in the relevant service's `README.md`.
- Refactor when you spot duplication or unclear naming.
- Add tests beyond the minimum if the code is non-trivial.
- Add Docker healthchecks, non-root users, and slim base images.
- Generate seed data and fixtures so tests don't depend on live services.
- Write Mermaid diagrams in `docs/architecture.md` when the system grows.
- Push regularly (at least every 2-3 commits) so the developer can see progress on GitHub.

### Things never to do

- Do not commit secrets, real API keys, or any `.env` file (only `.env.example`). Verify with `git diff --cached` before every commit.
- Do not use copyrighted code, datasets, or text. Public-licensed sources only, citations in `docs/data-sources.md`.
- Do not silently change the spec. If you disagree with it, push back in writing in the phase plan.
- Do not skip a phase's definition of done. If a check is genuinely impossible in this environment, document the fallback; do not pretend it passed.
- Do not write fake or placeholder logic just to make tests green. Either implement it, or skip it and mark it clearly with a TODO and a tracking issue (use `gh issue create`).
- Do not use em dashes in any prose you write (the developer's preference).
- Do not force-push to `main`. Ever.

### Coding standards (enforced by CI)

- Rust: `cargo fmt`, `cargo clippy -- -D warnings`, `cargo test`.
- .NET: `dotnet format --verify-no-changes`, `dotnet build -warnaserror`, `dotnet test`.
- Python: `ruff format --check`, `ruff check`, `pyright` strict, `pytest`.
- TypeScript (e2e tests only): `eslint`, `prettier --check`, `tsc --noEmit`, `playwright test`.
- Containers: every Dockerfile has `HEALTHCHECK` and a non-root user.
- Commits: Conventional Commits.

### How to use the developer's time efficiently

When you do need input, batch your questions. Each stop should look like this:

```
PHASE {N} BLOCKED. Need decisions on:
1. Question with two or three concrete options and your recommendation.
2. Same.
3. Same.
Will resume immediately on response.
```

Otherwise, keep moving. Push commits regularly so the developer sees progress on GitHub.

## Start now

1. Confirm you have read `FjordWatch-SPEC.md` end to end.
2. Verify Git and GitHub setup: `git status`, `gh auth status`, `git remote -v`. The remote `origin` should point to a GitHub repo. If anything is missing, stop and report.
3. If the working directory is clean and connected, begin Phase 0. Plan, build, test, verify, summarize, push, advance.
4. Continue to Phase 7 unless a stop condition triggers.

When you finish Phase 7, write `docs/plans/final-summary.md` with: total commits, lines of code per language, test coverage, list of fallbacks taken, what manual steps the developer should take before posting the project publicly (for example, recording the demo video, taking screenshots, writing the LinkedIn post). Then stop and report.

Begin.
