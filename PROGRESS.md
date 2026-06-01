# StumbleClone — MVP Scaffolding Progress

Status as of the integration-review pass.

## Subsystem file counts

| Subsystem | Files | Notes |
|-----------|------:|-------|
| Core      | 5 | `IRacer`, `GameEvents`, `RacerRegistry`, `GameConstants`, `LevelMode` |
| Player    | 4 | Controller, InputHandler, Animator, PushInteraction |
| Camera    | 1 | `ThirdPersonCamera` in `StumbleClone.CameraRig` |
| Bots      | 7 | Controller, Spawner, NameGenerator, base `BotBehavior` + 3 mode strategies |
| Game      | 6 | GameManager, LevelManager, RaceManager, SurvivalManager, LastStandingManager, LevelResult |
| Obstacles | 6 | SpinningBar, SwingingHammer, MovingPlatform, PushPad, RisingPlatform, ShrinkingZoneVisualizer |
| Level     | 3 | Checkpoint, FinishLine, KillZone |
| UI        | 8 | MainMenu, LevelSelect, RaceHUD, SurvivalHUD, LastStandHUD, EndScreen, PauseMenu, UIBootstrapper |
| Editor    | 3 | RaceLevelBuilder (+ BuilderUtils), SurvivalLevelBuilder, LastStandLevelBuilder |
| **Total** | **43** | |

## Contract verified

- [x] `IRacer` implemented by both `PlayerController` and `BotController` (all 12 members).
- [x] Both register/unregister with `RacerRegistry` in `OnEnable`/`OnDisable`.
- [x] All `GameEvents` subscribers unsubscribe symmetrically.
- [x] `GameManager` re-subscribes to `LevelEnded` after `GameEvents.Reset()` on scene load.
- [x] Unity 6 APIs: `Rigidbody.linearVelocity`, `FindFirstObjectByType` everywhere — no deprecated calls.
- [x] No legacy `Input.GetKey/GetAxis` — input goes through `PlayerInput` + the `Gameplay` action map.
- [x] Namespace consistency: gameplay code in `StumbleClone.{Subsystem}`; camera lives in `StumbleClone.CameraRig` to avoid clashing with `UnityEngine.Camera`.
- [x] All level builders gated by `#if UNITY_EDITOR`.

## Fixes applied during review

1. `UI/PauseMenuUI.cs` — missing `using StumbleClone.Player;` (referenced `PlayerInputHandler`). Added.
2. `Editor/LastStandLevelBuilder.cs` — log message said `Level_LastStand.unity` but `LevelManager` loads `Level_LastStanding`. Aligned to the canonical name.
3. `Obstacles/MovingPlatform.cs` — added scene-unload guards in `OnTriggerExit` / relay so destroyed colliders don't throw `MissingReferenceException` when reparenting on scene change.
4. `Level/KillZone.cs` — replaced reflection-based `GameManager` lookup with a direct reference (`GameManager` is now a real type in this project; reflection was a hedge against the parallel coder pass).
5. `UI/UIBootstrapper.cs` — also creates a fallback `LevelManager` (previously only `GameManager`), so launching directly into `MainMenu` during development doesn't NRE when clicking a mode button.

## Manual TODOs for the user

These can't be done from script — they need the Unity editor:

- [ ] Add the tags and layers listed in `Assets/IMPORT_GUIDE.md` section 2.
- [ ] Run the three `StumbleClone > Build *` menu items, save scenes, bake NavMesh.
- [ ] Author the `MainMenu` scene with a Canvas, buttons, and a `UIBootstrapper`.
- [ ] Build the `Player.prefab` and `Bot.prefab` per the guide (sections 4, 5).
- [ ] Wire the `BotSpawner` `botPrefab` and mode-reference transforms in each level scene.
- [ ] Author `Assets/InputActions/PlayerInputActions.inputactions` so the `Gameplay` action map exists with `Move` (Vector2), `Look` (Vector2), `Jump` (Button), `Push` (Button), `Pause` (Button). The asset file is already at that path — open it and ensure the actions/bindings match those names exactly.
- [ ] Add all four scenes to Build Settings in the order listed in the guide.
- [ ] (Optional) Import a Mixamo character and authoring the Animator Controller per guide section 6.

## Architecture intentionally unfinished (out of MVP scope)

- No audio system, particles, online multiplayer, or mobile touch.
- `RisingPlatform.Begin()` is exposed but no level builder calls it yet — useful as a level-puzzle hook.
- `BotNameGenerator` is static — fine for a single-machine prototype, would need per-session scoping for online play.
- `RaceManager.PlayerRank` is computed but `RaceManager` does not raise `RacerRankChanged` for the player every frame (only for finishers). The `RaceHUD` still shows the player rank because it tracks the event for finishers; live-rank ticker is a follow-up.

See `Assets/IMPORT_GUIDE.md` for the step-by-step bring-up. See the per-script doc comments for behavior details.
