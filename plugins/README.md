# Agent Skills

Distributable agent skills for .NET MAUI development. Installable via the Copilot CLI, Claude Code, or VS Code plugin system.

DevFlow runtime skills (`devflow-onboard`, `devflow-connect`, `devflow-debug`) are bundled with the `maui` CLI from `.claude/skills/` and installed with `maui devflow init`.

## Plugin

| Plugin | Skill | Description |
|--------|-------|-------------|
| [dotnet-maui](dotnet-maui/) | [android-slim-bindings](dotnet-maui/skills/android-slim-bindings/) | Create Android slim bindings using the Native Library Interop approach. |
| | [ios-slim-bindings](dotnet-maui/skills/ios-slim-bindings/) | Create iOS slim bindings using the Native Library Interop approach. |
| | [dotnet-workload-info](dotnet-maui/skills/dotnet-workload-info/) | Discover installed .NET workloads, SDK versions, and dependency requirements. |

## Installation

```bash
# Add this repo as a marketplace
/plugin marketplace add dotnet/maui-labs

# Install the plugin
/plugin install dotnet-maui@dotnet-maui-labs
```

## Adding Skills

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full guide. Quick summary:

1. Create `plugins/<plugin>/skills/<skill-name>/SKILL.md` with YAML frontmatter
2. Create `tests/<plugin>/<skill-name>/eval.yaml` with evaluation scenarios
3. Submit a PR — the `skill-check` workflow validates automatically
4. A maintainer posts `/evaluate` to run LLM-based evaluation
