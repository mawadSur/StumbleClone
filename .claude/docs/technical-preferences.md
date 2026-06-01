# Technical Preferences

<!-- Configured for StumbleClone — Unity 6 3D party game prototype. -->
<!-- All agents reference this file for project-specific standards. -->

## Engine & Language

- **Engine**: Unity 6000.4.8f1 (Unity 6 LTS line; see `docs/engine-reference/unity/VERSION.md`)
- **Language**: C# 9
- **Rendering**: Universal Render Pipeline (URP) 17.0.4
- **Physics**: Built-in Rigidbody + NavMesh (`com.unity.ai.navigation` 2.0.x with `NavMeshSurface`)

## Input & Platform

- **Target Platforms**: PC (Windows standalone primary; WebGL build viable later)
- **Input Methods**: Keyboard/Mouse, Gamepad
- **Primary Input**: Keyboard/Mouse (WASD + Mouse Look + Space jump + Mouse-1 push)
- **Gamepad Support**: Full (left stick move, right stick look, South jump, West push) — bindings in `Assets/InputActions/PlayerInputActions.inputactions`
- **Touch Support**: None (out of MVP scope)
- **Platform Notes**: Use New Input System (`com.unity.inputsystem`). No legacy `Input.*` calls.

## Naming Conventions

- **Classes**: `PascalCase` (e.g., `PlayerController`, `BotSpawner`)
- **Variables (public)**: `PascalCase`; **(private)**: `_camelCase` or `camelCase` depending on `[SerializeField]` status
- **Signals/Events**: `OnVerbed` for instance events (`OnFinished`, `OnEliminated`); `RaiseVerbed` for bus invocations
- **Files**: One class per file, filename matches class name exactly
- **Scenes/Prefabs**: `Level_Race`, `Level_Survival`, `Level_LastStanding`, `MainMenu`; prefabs in `PascalCase` (`Player.prefab`, `Bot.prefab`)
- **Constants**: `PascalCase` in static classes (`GameConstants.DefaultMoveSpeed`)
- **Tags**: `PascalCase` strings — `Player`, `Bot`, `Finish`, `Killzone`, `PushPad`, `Respawn`
- **Layers**: `Player=8`, `Bot=9`, `Obstacle=10`, `Ground=11`, `Killzone=12` (defined in `GameConstants`)

## Performance Budgets

- **Target Framerate**: 60 fps on mid-range PC
- **Frame Budget**: 16.67 ms (CPU + GPU)
- **Draw Calls**: < 500 per frame (URP batching enabled)
- **Memory Ceiling**: < 1 GB for MVP

## Testing

- **Framework**: Unity Test Framework (`com.unity.test-framework` 1.5.1) — EditMode + PlayMode
- **Minimum Coverage**: No formal target for MVP; add tests when behavior is regression-prone
- **Required Tests**: `IRacer` contract conformance, `GameEvents` subscribe/unsubscribe symmetry, level manager win-condition logic

## Forbidden Patterns

- `Rigidbody.velocity` — use `Rigidbody.linearVelocity` (Unity 6 deprecation)
- `Object.FindObjectOfType<T>()` — use `FindFirstObjectByType<T>()` or `FindAnyObjectByType<T>()`
- `Input.GetKey/GetAxis/etc` — legacy Input Manager is forbidden; use Input Actions
- `GetComponent<T>()` in `Update`/`FixedUpdate` — cache in `Awake`/`Start`
- Allocations in hot paths (`Update`, physics callbacks) — use `NonAlloc` variants and object pools
- `Resources.Load` — use direct prefab references or (later) Addressables
- Magic numbers in gameplay code — promote to `GameConstants` or a ScriptableObject

## Allowed Libraries / Addons

- `com.unity.ai.navigation` (NavMesh)
- `com.unity.cinemachine` 3.1.x (available; not currently used — `ThirdPersonCamera` is plain MonoBehaviour for MVP)
- `com.unity.inputsystem`
- `com.unity.render-pipelines.universal`
- `com.unity.ugui` + TextMeshPro (bundled)
- `com.unity.nuget.newtonsoft-json` (available; not used yet)

## Architecture Decisions Log

- ADR-001 (implicit, in `CLAUDE.md`): Rigidbody-based player movement (not CharacterController) for physics-driven party-game feel
- ADR-002 (implicit): Bot's NavMeshAgent drives a kinematic Rigidbody; the body goes dynamic during knockback then re-snaps to NavMesh
- ADR-003 (implicit): Single default assembly (no per-folder asmdefs) for MVP simplicity; revisit if compile times become an issue
- ADR-004 (implicit): URP over Built-in or HDRP; standard for new Unity 6 3D projects

Use `/architecture-decision` to formalize these into `docs/architecture/`.

## Engine Specialists

- **Primary**: `unity-specialist`
- **Language/Code Specialist**: `unity-specialist` (no separate C# specialist needed; gameplay-programmer handles general patterns)
- **Shader Specialist**: `unity-shaders-vfx-specialist`
- **UI Specialist**: `unity-ui-toolkit-specialist` (UGUI for current MVP; UI Toolkit for future polish)
- **Additional Specialists**: `unity-dots-ecs-specialist` (not needed for MVP — bot count is small), `unity-addressables-specialist` (post-MVP for asset bundles)
- **Routing Notes**: Route gameplay implementation through `gameplay-programmer` first; escalate to `unity-specialist` for engine-specific patterns (DOTS, Addressables, URP shaders, NavMesh tuning). Reference `docs/engine-reference/unity/` before suggesting Unity 6 API calls — the model's training data only reliably covers up to 2022 LTS.

### File Extension Routing

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (`.cs`) | `gameplay-programmer` (general) or `unity-specialist` (engine-specific) |
| Shader / material files (`.shader`, `.shadergraph`, `.mat`) | `unity-shaders-vfx-specialist` |
| UI / screen files (`.uxml`, `.uss`, Canvas prefabs) | `unity-ui-toolkit-specialist` |
| Scene / prefab / level files (`.unity`, `.prefab`) | `level-designer` (layout) → `unity-specialist` (technical wiring) |
| Native extension / plugin files | `unity-specialist` |
| General architecture review | `unity-specialist` |
