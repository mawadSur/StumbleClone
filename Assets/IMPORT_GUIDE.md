# StumbleClone — Import & Setup Guide

This guide takes the freshly-checked-out project from zero to a runnable Stumble Guys-style prototype.
The MVP runs with placeholder primitive shapes; a Mixamo character is optional but recommended.

Target: Unity 6000.4.8f1 with URP and the New Input System.

---

## 1. First-time Unity setup

Open **Unity Hub** > **Add** > select `unity-projects/StumbleClone/`. The first import is slow because
URP, the New Input System, AI Navigation, and TextMeshPro packages are resolving and shaders are
compiling. Expect 2–5 minutes. Once the editor loads, you should see the `Assets/` tree populated and
no red errors in the Console. If TMP prompts you to import its Essentials, accept.

---

## 2. Tags & Layers setup

Open **Edit > Project Settings > Tags and Layers**. These values are defined in
`Assets/Scripts/Core/GameConstants.cs` and are the source of truth — do not change them, just
mirror them in the project settings.

### Tags (under the `Tags` foldout)
- [ ] `Player`
- [ ] `Bot`
- [ ] `Finish`
- [ ] `Killzone`
- [ ] `PushPad`
- [ ] `Respawn`

### User Layers (slots 8–12)
- [ ] Layer 8: `Player`
- [ ] Layer 9: `Bot`
- [ ] Layer 10: `Obstacle`
- [ ] Layer 11: `Ground`
- [ ] Layer 12: `Killzone`

After saving, the C# `GameConstants` IDs will line up with the editor layer indices automatically.

---

## 3. Building the levels

The project ships three editor menu items that build each scene from primitives. Run each one,
then save the resulting scene under the exact name shown.

### Build each scene
- [ ] **StumbleClone > Build Race Level** — save as `Assets/Scenes/Level_Race.unity`
- [ ] **StumbleClone > Build Survival Level** — save as `Assets/Scenes/Level_Survival.unity`
- [ ] **StumbleClone > Build Last Stand Level** — save as `Assets/Scenes/Level_LastStanding.unity`

The scene names must match exactly — `LevelManager` loads them by string.

### Bake the NavMesh in each scene
For each level scene:
- [ ] Open **Window > AI > Navigation**.
- [ ] In the **Bake** tab, click **Bake**.
- [ ] Save the scene. The NavMesh asset is stored next to the scene.

Bots refuse to move on a scene with no NavMesh, so this step is required.

### Create the MainMenu scene
- [ ] **File > New Scene** > Basic (Built-in).
- [ ] Save as `Assets/Scenes/MainMenu.unity`.
- [ ] Add a `Canvas` with three buttons (Play / Quit / and a panel containing Race / Survival / LastStanding / Back buttons).
- [ ] Add an empty GameObject named `UIBootstrapper` with the `UIBootstrapper` script — it auto-creates the GameManager and LevelManager if they aren't already in the scene.
- [ ] Attach `MainMenuUI` to the canvas root and wire the Play / Quit buttons and Level-Select panel.
- [ ] Attach `LevelSelectUI` to the Level-Select panel and wire the four mode buttons.

---

## 4. Setting up the Player prefab

Create `Assets/Prefabs/Player.prefab`:

1. [ ] Create an empty GameObject named `Player`. Set tag = `Player`, layer = `Player` (8).
2. [ ] Add `CapsuleCollider` (Height 2, Radius 0.5, Center Y = 1).
3. [ ] Add `Rigidbody` (Mass 1, Drag 0, Angular Drag 0.05, Freeze Rotation X/Y/Z = checked, Interpolate = Interpolate, Collision Detection = Continuous). **Do NOT enable Is Kinematic** — the player is a dynamic body.
4. [ ] Add `PlayerInput` (Unity.InputSystem). Drag `Assets/InputActions/PlayerInputActions.inputactions` into the Actions slot. Default Action Map = `Gameplay`. Behavior = `Send Messages` is fine.
5. [ ] Add `PlayerInputHandler` (auto-finds the PlayerInput).
6. [ ] Add `PlayerController` — fields auto-resolve from the same GameObject. Set Display Name = `Player`, Racer Id = `0`.
7. [ ] Add `PushInteraction` — auto-resolves to the same input handler and capsule collider.
8. [ ] Add `PlayerAnimator` — leave the Animator slot empty for now; we'll fill it in section 6.
9. [ ] As a visual placeholder, add a child `Capsule` primitive (remove its collider — the parent has one).
10. [ ] Save as `Assets/Prefabs/Player.prefab`. Drop one instance into each Level scene.

### Camera

Create a separate camera GameObject (or use the Main Camera that ships with new scenes):
- [ ] Add `ThirdPersonCamera` (from `StumbleClone.CameraRig`).
- [ ] Drag the Player into the **Target** field.
- [ ] Tag the camera as `MainCamera` so `PlayerController` finds it via `Camera.main`.

---

## 5. Setting up the Bot prefab

Create `Assets/Prefabs/Bot.prefab`:

1. [ ] Empty GameObject named `Bot`. Tag = `Bot`, layer = `Bot` (9).
2. [ ] Add `CapsuleCollider` (Height 2, Radius 0.5, Center Y = 1).
3. [ ] Add `Rigidbody` with **Is Kinematic = ON** initially. (`BotController.Awake` also forces this, but having the prefab default match avoids a one-frame physics blip on spawn.) Freeze Rotation X/Y/Z = checked, Interpolate = Interpolate.
4. [ ] Add `NavMeshAgent` (Radius 0.5, Height 2, Speed 6, Acceleration 12, Angular Speed 240).
5. [ ] Add `BotController` — fields auto-resolve.
6. [ ] As a visual placeholder, add a child `Capsule` primitive in a contrasting color (delete its collider).
7. [ ] Save as `Assets/Prefabs/Bot.prefab`.

Wire the prefab into each level scene's `BotSpawner` (created by the level builders) by dragging it into the `botPrefab` slot. Also drag the relevant transforms into:
- Race: `finishLine` = the `FinishVisual` GameObject's transform.
- Survival: `safeAnchor` = the `SafeZoneCenter` GameObject.
- LastStanding: `arenaCenter` = the `Arena` GameObject; set `arenaRadius` ≈ 18.

---

## 6. Importing a Mixamo character (optional but recommended)

Skip this section if placeholder capsules are fine for now.

### Download the model
- [ ] Go to https://mixamo.com and sign in (free Adobe account).
- [ ] Pick a character such as **Y Bot**. Click **Download**. Format: `FBX for Unity (.fbx)`, **with skin**, **T-Pose**.
- [ ] Save into `Assets/Models/`.

### Download the animations
For each clip below, search Mixamo, select **Without Skin**, Format **FBX for Unity**:
- [ ] `Idle`
- [ ] `Running` — enable the **In Place** checkbox.
- [ ] `Jumping`
- [ ] `Falling Idle`
- [ ] `Getting Up`

Drop the FBX files into `Assets/Models/`.

### Configure the rig
For each FBX:
- [ ] Select the asset. In the **Rig** tab, set Animation Type = `Humanoid`, Avatar Definition = `Create From This Model` (only for the character FBX). Click **Apply**.
- [ ] For the animation FBX files, set Avatar Definition = `Copy From Other Avatar` and point to the character's avatar.
- [ ] On the **Animation** tab of each clip, set **Loop Time** = ON for Idle/Running/Falling Idle.

### Create the Animator Controller
- [ ] In `Assets/Animations/`, right-click > Create > Animator Controller, name it `PlayerAnimator.controller`.
- [ ] Open it in the Animator window.
- [ ] Add parameters (exact spelling and case — these are looked up by name in `PlayerAnimator.cs`):
  - [ ] `Speed` (Float)
  - [ ] `Grounded` (Bool)
  - [ ] `Jump` (Trigger)
  - [ ] `Fall` (Bool)
  - [ ] `KnockedDown` (Trigger)
- [ ] Create states:
  - `Idle` (default) — use the Idle clip.
  - `Run` — use the Running clip.
  - `Jump` — use the Jumping clip.
  - `Fall` — use the Falling Idle clip.
  - `KnockedDown` — use the Getting Up clip.
- [ ] Transitions:
  - `Idle → Run` when `Speed > 0.1`.
  - `Run → Idle` when `Speed < 0.05`.
  - `Any State → Jump` on `Jump` trigger.
  - `Any State → Fall` when `Fall == true`.
  - `Any State → KnockedDown` on `KnockedDown` trigger.
- [ ] Disable **Has Exit Time** on the Any-State transitions so they fire immediately.

### Hook it up
- [ ] Open the `Player.prefab`. Drag the Mixamo character FBX into the prefab as a child of the root.
- [ ] Position the child so its feet sit at the root's pivot (Y = 0).
- [ ] On the child, set the `Animator` component's Controller = `PlayerAnimator.controller`.
- [ ] On the root, assign the child's `Animator` into the `PlayerAnimator` script's `animator` field.
- [ ] Delete the placeholder capsule visual.

---

## 7. Build & Run

- [ ] **File > Build Profiles** (or Build Settings) — add all four scenes in this order:
  1. `Assets/Scenes/MainMenu.unity`
  2. `Assets/Scenes/Level_Race.unity`
  3. `Assets/Scenes/Level_Survival.unity`
  4. `Assets/Scenes/Level_LastStanding.unity`
- [ ] Open `MainMenu.unity` and press **Play**.
- [ ] Click Play > Race. The Race scene loads, 7 bots spawn at the spawn points, and the HUD shows your rank.
- [ ] Controls: WASD = move, Space = jump, Left-Click / E = push, Esc = pause.

---

## 8. Known gaps

The MVP intentionally does not include:
- Online multiplayer / netcode.
- Character customization / skins.
- Audio (sound effects, music).
- Particle effects (dust, hit flashes).
- Mobile touch controls — keyboard/mouse and gamepad only.
- Polished art / final-quality models.
- Difficulty scaling, ranked play, persistent stats.

The architecture (`IRacer`, `GameEvents`, `RacerRegistry`, `BotBehavior` strategies, mode-specific
managers) is designed so these can be layered on without rewriting gameplay code:
- Audio: subscribe a sound manager to `GameEvents.RacerEliminated` etc.
- Multiplayer: spawn proxies that also implement `IRacer` and register themselves.
- Customization: swap the prefab's visual child; nothing in the controller scripts cares.
