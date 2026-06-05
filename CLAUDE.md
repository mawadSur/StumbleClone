# StumbleClone — Claude Code Game Studios

3D Stumble Guys-style party game prototype. Built on the Claude Code Game Studios template
(49 agents, 73 skills, 12 hooks). MVP scope: player + 7 bots across 3 level modes.

## Project Status

- **Phase**: MVP scaffolding complete; awaiting first playable build
- **State files**: `README.md` (project overview), `PROGRESS.md` (subsystem status + applied fixes), `Assets/IMPORT_GUIDE.md` (in-editor setup steps)
- **Bootstrap**: `Assets/Scripts/Editor/MvpBootstrap.cs` exposes `StumbleClone > Bootstrap MVP` — one-click full scene + prefab + NavMesh setup.

## Technology Stack

- **Engine**: Unity 6000.4.8f1 (Unity 6 LTS line)
- **Language**: C# 9 (project default for Unity 6)
- **Rendering**: Universal Render Pipeline (URP) 17.0.4
- **Physics**: Built-in Rigidbody / NavMesh (`com.unity.ai.navigation` 2.0.x)
- **Input**: New Input System (`com.unity.inputsystem`) — `PlayerInputActions.inputactions`
- **UI**: UGUI + TextMeshPro
- **Version Control**: Git, trunk-based
- **Build System**: Unity built-in (PC standalone primary; WebGL viable)
- **Asset Pipeline**: AssetDatabase + Editor builder scripts (primitives only for MVP; Mixamo characters optional)

## Architecture Contract

All gameplay scripts conform to the contract in `Assets/Scripts/Core/`:
- `IRacer` — implemented by `PlayerController` (`StumbleClone.Player`) and `BotController` (`StumbleClone.Bots`)
- `GameEvents` — global event bus (LevelStarted, LevelEnded, RacerFinished, RacerEliminated, RacerRankChanged, SurvivalTimerTick, WaveTelegraphed)
- `RacerRegistry` — racers self-register in OnEnable
- `GameConstants` — layers (Player=8, Bot=9, Obstacle=10, Ground=11, Killzone=12), tags (Player, Bot, Finish, Killzone, PushPad, Respawn), tuning knobs

Namespaces: `StumbleClone.{Core,Player,Bots,Game,Obstacles,Level,UI,CameraRig,EditorTools}`. Note `CameraRig` (not `Camera`) — avoids clash with `UnityEngine.Camera`.

## Project Structure

@.claude/docs/directory-structure.md

## Engine Version Reference

@docs/engine-reference/unity/VERSION.md

## Technical Preferences

@.claude/docs/technical-preferences.md

## Coordination Rules

@.claude/docs/coordination-rules.md

## Collaboration Protocol

**User-driven collaboration, not autonomous execution.**
Every task follows: **Question -> Options -> Decision -> Draft -> Approval**

- Agents MUST ask "May I write this to [filepath]?" before using Write/Edit tools
- Agents MUST show drafts or summaries before requesting approval
- Multi-file changes require explicit approval for the full changeset
- No commits without user instruction

See `docs/COLLABORATIVE-DESIGN-PRINCIPLE.md` for full protocol and examples.

> **Continuing this project?** The scaffold is implemented. Read `PROGRESS.md` and `Assets/IMPORT_GUIDE.md` first. The studio template's `/start` skill is for new projects — skip it. To extend gameplay, route via `unity-specialist` or use `/dev-story` with stories created from the existing scope.

## Coding Standards

@.claude/docs/coding-standards.md

## Context Management

@.claude/docs/context-management.md
