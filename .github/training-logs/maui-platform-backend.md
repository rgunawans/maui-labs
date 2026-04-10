# Training Log: maui-platform-backend

Skill location:
- Local: `~/.copilot/skills/maui-platform-backend/`
- Repo: `.github/skills/maui-platform-backend/`

---

## Session: 2026-04-06 — Initial assessment and Phase 1 stop signal fix

**Trainer:** SkillTrainer | **Skill:** maui-platform-backend | **Trigger:** First training session after skill creation

### Assessment

**Multi-model eval (3 models, 2 families):**
- Claude Sonnet 4, GPT-5.2, Claude Haiku 4.5
- Task: "I want to create a new MAUI backend for Avalonia. Where do I start? Go ahead and scaffold the initial project structure."

**Pre-training scores:**

| Dimension | Sonnet 4 | GPT-5.2 | Haiku 4.5 | Avg |
|-----------|----------|---------|-----------|-----|
| Accuracy | 5 | 4 | 5 | 4.7 |
| Completeness | 4 | 3 | 4 | 3.7 |
| Clarity | 5 | 4 | 4.5 | 4.5 |
| Token efficiency | 4 | 4 | 5 | 4.3 |
| Stop signals | 5 | 5 | 3.5 | 4.5 |

**Issues found (ranked):**
1. ⚠️ **Missing Phase 1 stop checklist** — All 3 models flagged uncertainty about when scaffolding is complete. No explicit "done" criteria, no build verification step.
2. ⚠️ **Canonical reference access not guided** — SKILL.md says "Study platforms/Linux.Gtk4/ (branch: platforms/linux-gtk4-import)" but doesn't explain HOW to access it without switching branches. GPT and Haiku specifically flagged this.
3. ❌ **Session count factual error** — SKILL.md said "28 sessions across 7 backend categories" but prior-sessions.md counts "26 sessions (25 unique) across 7 platform backend categories."
4. 💡 **Naming convention gap** — GPT uniquely flagged that the naming convention (Linux.Gtk4, MacOS.AppKit) doesn't address cross-platform toolkits like Avalonia. Not broken but worth noting.
5. 💡 **DevFlow agent code location ambiguous** — devflow-integration.md says "lives in existing DevFlow product" but doesn't clarify the module structure.

### Cycle 1: Phase 1 stop checklist

**Hypothesis:** Adding an explicit Phase 1 completion checklist with build verification will reduce model uncertainty about when scaffolding is complete, leading to more consistent outputs.

**Edit:** SKILL.md Phase 1 section — added 4-item "✅ Phase 1 is complete when:" checklist including `dotnet build` verification and explicit "don't implement handler logic yet" guidance.

### Cycle 2: Canonical reference access + session count fix

**Hypothesis:** Showing exact commands to browse Linux.Gtk4 without switching branches will eliminate the access confusion flagged by GPT and Haiku.

**Edits:**
- SKILL.md Phase 0 — rewrote canonical reference paragraph with explicit `github-mcp-server-get_file_contents` and `git show` commands
- SKILL.md References section — corrected "28 sessions" → "26 sessions"

### Validation

**Post-training re-eval (Haiku 4.5, GPT-5.2):**

| Dimension | Haiku (before) | Haiku (after) | GPT (after) |
|-----------|---------------|---------------|-------------|
| Stop signals | 3.5 | **5** | **5** |
| Clarity | 4.5 | **5** | **5** |
| Overall | 4.4 | **5** | **5** |

**Regressions:** None detected by either model. All changes were additive/clarifying.

**Outcome:** ✅ All changes kept. Stop signal fix had the highest impact (Haiku: 3.5 → 5/5).

### Patterns Learned

- **Phase-specific stop checklists are high-leverage for multi-phase skills** — the skill already had good stop signals for Research and Handler Implementation phases, but the Scaffolding phase was missing one. Models need to know when each phase is "done."
- **"How to access" > "where it is"** — saying "it's on branch X" is less useful than providing the exact command to browse it. Agents operate within tool constraints; give them the tool invocation.
- **All 3 models converged on the same top issue** — high confidence the Phase 1 stop checklist was the right fix. Consensus across model families is a strong signal.

### Open Items

- 💡 Naming convention for cross-platform toolkits (Avalonia) — not addressed, low priority
- 💡 DevFlow agent code location clarification — could be addressed in devflow-integration.md reference
- The skill has not been tested against a REAL backend creation task yet (only simulated eval). A real-world test would be the gold standard validation.

---

## Session: 2026-04-07 — Session history mining and empirical pattern extraction

**Trainer:** SkillTrainer | **Skill:** maui-platform-backend | **Trigger:** User request to mine local Copilot CLI session histories

### Data Source

Mined **95 checkpoint files** from local Copilot CLI session histories across 7 backend categories:

| Platform | Checkpoints | Key Theme |
|----------|-------------|-----------|
| macOS/AppKit | 68 | Full backend implementation + visual audit |
| macOS | 5 | PushModalAsync native sheet modals |
| macOS | 1 | Shell/SplitView/Sidebar branch |
| macOS | 1 | Submit Pull Request |
| GTK4/Linux | 2 | Import MAUI Platforms to maui-labs |
| TUI | 6 | Terminal UI with steering support |
| Extensibility | 1 | Custom backends & multitargeting |

### Assessment

Compared mined insights against existing SKILL.md and references. Identified 10 gaps ranked by severity:

**❌ Critical (caused crashes/hangs/hours of wasted time):**
1. No guidance on try-catch in native layout overrides (caused infinite retries, SIGSEGV)
2. No warning about Shell navigation from background threads (caused SIGSEGV)
3. No mention of killing stale processes before retesting (#1 source of false negatives)

**⚠️ Incomplete (partially covered but missing key details):**
4. No guidance on unified attributed text rendering (label property ordering bugs)
5. No mention of verifying .NET binding existence for native APIs
6. No preparation for visual audit scale (32% of macOS session)
7. No per-window unique identifier warning
8. No toolbar/MenuBar change notification workarounds

**💡 Nice-to-have (edge cases with clear workarounds):**
9. Editor placeholder custom subclass pattern
10. Button padding subclass explanation

### Cycle 1: Anti-patterns and Debugging Techniques (batch)

**Hypothesis:** Adding 3 new anti-patterns (#9-#11) and 5 new debugging techniques to SKILL.md will eliminate the critical gaps found across sessions.

**Edits to SKILL.md:**
- Added anti-pattern #9: Don't throw from native layout/measure overrides
- Added anti-pattern #10: Don't call Shell navigation from background threads  
- Added anti-pattern #11: Don't mix separate text property setters on labels
- Added debugging: Kill stale processes (prioritized as FIRST technique)
- Added debugging: Use `maui-devflow wait` before inspecting
- Added debugging: Verify .NET binding existence
- Added debugging: Use file logging for native debugging
- Added debugging: Prepare for visual audit scale (multi-pass)

**Edits to prior-sessions.md:**
- Added 8 new Key Lessons (#13-#20) covering visual audit scale, stale processes, binding verification, per-window IDs, toolbar change detection, unified text rendering, button padding subclass, editor placeholder subclass
- Added 8 new Pitfalls & Workarounds entries from mined sessions

### Validation

**Post-training multi-model eval (3 models, 2 families):**

Task: "Building a Godot MAUI backend. Phase 2 done. Getting intermittent SIGSEGV during Shell navigation. Label text partially applying properties."

| Dimension | Sonnet 4 | GPT-5.1 | Haiku 4.5 |
|-----------|----------|---------|-----------|
| Accuracy | 5 | 5 | 5 |
| Completeness | 4 | 4 | 4 |
| Actionability | 5 | 5 | 5 |
| Anti-pattern coverage | 5 | 5 | 5 |
| Debugging workflow | 4 | 4 | 5 |

**Key validation results:**
- All 3 models correctly identified SIGSEGV causes (background thread + layout exceptions) using new anti-patterns #9 and #10
- All 3 models recommended killing stale processes FIRST, citing the new debugging technique
- All 3 models recommended unified `UpdateAttributedText()` using new anti-pattern #11
- All 3 models correctly applied stop signals for platform differences
- No regressions detected — all pre-existing guidance still referenced correctly

**Completeness rated 4/5 because:** All models flagged that game-engine backends (Godot) have different rendering models than traditional widget toolkits. The skill's GDUI section partially addresses this but doesn't cover Godot-specific patterns. This is expected — the skill can't pre-document every platform.

**Regressions:** None. All changes were additive.

### Patterns Learned

1. **Session mining is high-yield for empirical patterns** — 95 checkpoints across 9 sessions yielded 10 actionable insights. The most valuable patterns were failure modes that appear repeatedly (stale processes, layout exceptions, threading crashes).

2. **Anti-patterns from real sessions carry more weight than theoretical ones** — All 3 eval models rated anti-pattern coverage 5/5 specifically because the new items cite empirical evidence ("caused hangs", "was the #1 source of false negatives").

3. **Visual audit scale is an under-documented reality** — 22 of 68 macOS checkpoints (32%) were visual comparison work. Future backends should plan for this as the dominant late-stage activity.

4. **Debugging priority matters more than debugging techniques** — Adding "kill stale processes FIRST" had more impact than adding new debugging tools. The ordering of debugging steps is itself the most valuable guidance.

5. **Pitfalls table is the most scannable reference** — The expanded 20-row pitfalls table in prior-sessions.md is the quickest reference for "has anyone hit this before?" queries. Keep it growing.

### Open Items

- 💡 Game-engine-specific rendering guidance (Godot/GDUI patterns) — could be a separate reference file when more game engine backends are implemented
- 💡 Editor placeholder and button padding could get their own "Common Subclass Patterns" reference with code templates
- The skill has still not been tested against a REAL backend creation task (only simulated eval)
