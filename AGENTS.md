# AGENTS.md: PARLOR (Unity, Meta Quest, mixed reality)

Any coding agent working here must first read
`/Users/mac/local/internal/meta/XR/submission/SUPERPROMPT.md`. It holds the spec, the plan, the quality
gates and the iteration loop. This file only lists the rules that always apply.

## Ground rules
- **There is no physical headset.** All runtime testing happens in **Meta XR Simulator** on this Mac
  (Apple Silicon, Metal). Don't ask anyone to test on a device.
- **Work autonomously and never block on the human.** Anything only a human can do goes in
  `submission/HUMAN_TODO.md`. Then continue with other work.
- Load secrets and config with `set -a; source repo/.env; set +a`. **Never** print secrets in
  logs or commit them. Never commit `.env` or `keystore/`.
- Verify Meta/Quest APIs against current docs (`metavr docs search "<topic>"`, `metavr docs fetch <path>`)
  before relying on memory. Meta's agent skills are at https://github.com/meta-quest/agentic-tools
  (`metavr init --all-agents` installs them for your agent).
- Log every iteration in `submission/BUILD_LOG.md` (what changed, gate table, next fix).
- Commit at every green checkpoint, with small and descriptive commits.

## Layout
- Unity project: `repo/PARLOR/`. Builds: `repo/Builds/`. Simulator captures: `repo/Captures/` (both git-ignored).
- Editor-driving scripts: `repo/PARLOR/AgentScripts/` (outside `Assets/`, run via `unity command run_script`).
- Art reference: `submission/design/` (concept art is the source of truth for look and staging).
- Judge-facing files: `submission/` (see SUPERPROMPT §11).

## Tools (installed)
- Unity Editor 6000.6.9f1 + Android Build Support, at `/Applications/Unity/Hub/Editor/6000.6.9f1`
- Unity CLI: `/Users/mac/.unity/bin/unity` (`unity status`, `unity command`, `unity build`, `unity test`)
- Meta VR CLI: `/Users/mac/.metavr/bin/metavr` (authenticated). Simulator: `metavr xrsim ...`
- Meta XR Simulator: installed via `metavr tools install xrsim`

## Deadline
Devpost closes **2026-11-18 12:00 PM PST**. Internal freeze is **06:00 AM PST** the same day: after that, no
features and only crash fixes. Ship what passes.
