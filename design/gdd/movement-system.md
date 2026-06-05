# Movement System GDD

> Reverse-engineered from shipped MVP code. Every rule below is grounded in source.
> Primary files: `Assets/Scripts/Player/PlayerController.cs`,
> `Assets/Scripts/Player/PlayerInputHandler.cs`, `Assets/Scripts/Player/IPlayerInput.cs`,
> `Assets/Scripts/Player/PlayerAnimator.cs`, `Assets/Scripts/Player/PushInteraction.cs`,
> `Assets/Scripts/Player/HitStop.cs`, `Assets/Scripts/Core/GameConstants.cs`,
> `Assets/Scripts/Camera/ThirdPersonCamera.cs`. Paths relative to project root.

## 1. Overview

The movement system drives the human player as a physics body: a dynamic `Rigidbody` +
`CapsuleCollider` with frozen rotation, steered camera-relative via the New Input System
(WASD / left-stick), with a jump (buffered + coyote-time), a one-per-airborne-stint air
dash (a second jump press while airborne), a punchy fall multiplier, walkable-slope
projection with anti-slide, knockback that physics carries (not stomped by the controller),
and a spawn-grace settle window that re-snaps falls and ignores knockback. Input enters
through the `IPlayerInput` seam (`Assets/Scripts/Player/IPlayerInput.cs`) so the same
controller can be driven locally (`PlayerInputHandler`) or, in future multiplayer, by a
replicated source via `SetInputSource` with no change to the movement code
(`PlayerController.SetInputSource`, lines 141–144).

## 2. Player Fantasy

A bouncy, slightly chaotic Stumble-Guys-style bean that feels light on the rise and decisive
on the fall, can dash to recover or steal a gap mid-air, and shoves rivals off ledges. Game
feel is layered on top without touching the physics: landing dust + camera shake scaled by
fall speed (`PlayerController.HandleLanded`, lines 235–251), a hit-stop freeze on real impacts
(`HitStop.Do`), an FOV "whoosh" on dash and trauma-based screen shake
(`ThirdPersonCamera.PunchFov` / `AddTrauma`), and a procedural dash-lunge / land-squash overlay
(`PlayerAnimator.LateUpdate`, lines 120–178). All juice is ReducedMotion-aware and never alters
gameplay state.

## 3. Detailed Rules

### 3.1 Body setup (`PlayerController.Awake`, lines 120–136)
- `Rigidbody`: `freezeRotation = true`, `interpolation = Interpolate`,
  `collisionDetectionMode = Continuous`. Movement is velocity-based, not `MovePosition`.
- `groundMask` has the Player (8) and Bot (9) layer bits stripped at runtime so the ground
  check never self-hits; if stripping empties the mask it resets to Ground (11) | Obstacle (10).

### 3.2 Planar movement (`ApplyMovement`, lines 394–460)
- Read `_input.MoveMasked` (zeroed while input-locked). `ComputeMoveDirection` projects the
  2D input onto the camera's flattened forward/right (lines 462–486); with no camera it falls
  back to world axes so the player can still move.
- Target planar velocity = `desired * ActiveMoveSpeed`. Current planar velocity is moved toward
  the target with `Vector3.MoveTowards` using `groundAcceleration` (grounded) or
  `airAcceleration` (airborne).
- **Knockback / external-velocity guard (lines 410–412):** if `Time.time < _inputLockUntil`
  (post-hit lock) OR planar speed exceeds `ActiveMoveSpeed + 0.5`, `ApplyMovement` early-returns,
  leaving x/z to physics. This is what lets pushes and dashes carry the body instead of being
  stomped back to run speed.
- Facing: when there's input, yaw is `SmoothDampAngle` toward the move direction over
  `TurnSmoothTime = 0.08` (line 54), applied via `_rb.MoveRotation` (interpolation-safe).

### 3.3 Grounding (`UpdateGrounded`, lines 376–392)
- A `SphereCast` straight down (radius `groundCheckRadius`, distance `groundCheckDistance + 0.05`,
  origin lifted `groundCheckRadius + 0.05`) against `groundMask`, ignoring triggers.
- On hit: `_grounded = true`, cache `_groundNormal`, and `_onWalkableSlope =` angle(normal, up)
  ≤ `maxSlopeAngle`. On miss: not grounded, normal resets to up, not a walkable slope.

### 3.4 Jump (`Update`, lines 273–288)
- A masked jump press stamps `_jumpBufferedTime`. A jump fires when **both**: buffered
  (`Time.time - _jumpBufferedTime ≤ jumpBufferTime`) and within coyote
  (`Time.time - _lastGroundedTime ≤ coyoteTime`).
- On fire: set `vel.y = ActiveJumpSpeed` (overwrite, not additive), play `Sfx.Jump` with random
  pitch 0.95–1.08, then consume **both** timers (set to `-Infinity`) so one press can't double-fire.
- `_lastGroundedTime` is refreshed and `_dashArmed` set true every frame the body is grounded
  (lines 267–271).

### 3.5 Air dash (`Update` line 289 → `Dash`, lines 344–368)
- Conditions to dash: `airDashEnabled` AND a fresh jump press AND not grounded AND **not** within
  coyote AND `_dashArmed`. (The coyote exclusion means an early second tap still jumps; only a
  genuinely airborne second press dashes.) One dash per airborne stint — `_dashArmed` is cleared
  on dash and re-armed only on landing.
- `Dash` direction = current masked move dir; if none, `transform.forward`; flattened to Y=0,
  normalized (final fallback `Vector3.forward`).
- Sets `_rb.linearVelocity = dir * dashSpeed` (drops any falling velocity → flat burst), arms
  `_dashUntil = Time.time + dashDuration`, snaps facing to the dash yaw, plays `Sfx.Dash`, FOV
  punch +7°, and triggers the animator dash overlay.
- During the dash window (`FixedUpdate`, lines 302–327): gravity is cancelled each step
  (`AddForce(-Physics.gravity, Acceleration)`) so the burst stays flat. When the window ends,
  planar speed is clamped back down to `ActiveMoveSpeed` so control returns immediately (the body
  has no drag to bleed it).

### 3.6 Fall multiplier (`FixedUpdate`, lines 337–338)
- While airborne (`!_grounded`) AND falling (`vel.y < 0`) AND **not** dashing, apply extra
  downward gravity: `AddForce(Physics.gravity * (FallMultiplier - 1), Acceleration)`. Grounded
  falling (e.g. on a ramp) uses normal gravity so the slope anti-slide stays correct.

### 3.7 Walkable slopes (`ApplyMovement`, lines 430–447)
- Gated on grounded AND walkable slope AND `vel.y ≤ 0.1` (so a fresh jump's upward velocity
  survives the lingering ground read).
- Horizontal velocity is re-projected onto the slope plane (`ProjectOnPlane` against
  `_groundNormal`) so motion runs *along* the surface (no speed bleed uphill, no launch downhill).
- **Anti-slide:** if idle (no input and near-zero new planar velocity), subtract the slope-tangent
  component of gravity for this step so the body holds its spot instead of creeping down.

### 3.8 Knockback (`Knockback`, lines 488–518)
- No-op if dead. No-op (fully ignored) if `Time.time < _spawnSafeUntil` (spawn grace) — does
  **not** consume the shield.
- SHIELD buff: if armed, consume it and return before any impulse/lock/anim.
- Otherwise: `AddForce(force + Vector3.up * KnockbackUpward, Impulse)`, set
  `_inputLockUntil = Time.time + inputLockOnKnockback`, trigger knocked-down anim, play `Sfx.Hit`,
  `HitStop.Do(0.06)`, and add camera trauma scaled by force magnitude (Lerp 0.4→0.7 over
  `force.magnitude / DefaultPushForce`).

### 3.9 Push (`PushInteraction`, full file)
- On masked push press past `pushCooldown`: `CapsuleCastNonAlloc` forward (capsule from self
  collider, range `pushRange`, buffer size 8) against `hitMask`, ignoring triggers.
- Every hit collider's `IRacer` (via `GetComponentInParent`) that isn't self receives
  `Knockback(transform.forward (+ upwardForceShare on Y), normalized * pushForce)`.
- On any connect: `HitStop.Do(0.06)`, dust scuff at contact point (falls back to target position
  if the cast returns a zero hit point), camera trauma +0.35.

### 3.10 Input masking (`PlayerInputHandler`, lines 41–45)
- `InputLocked` is set each frame by `UpdateInputLock` to `Time.time < _inputLockUntil`. While
  locked, `MoveMasked = Vector2.zero`, `JumpPressedMasked = false`, `PushPressedMasked = false`.

### 3.11 Spawn safety (`Start`/`Update`/`ReSnapToSpawn`/`Eliminate`)
- `Start` records `_spawnPoint = transform.position` and `_spawnSafeUntil = Time.time + spawnGrace`.
- `Update` (lines 257–262): below `WorldKillY`, if still in grace → `ReSnapToSpawn` and return,
  else `Eliminate`. `Eliminate` (lines 560–590) also re-snaps if still in grace.
- `ReSnapToSpawn` (lines 188–203): teleport to spawn, zero linear+angular velocity, and clear
  `_inputLockUntil = 0` (the asymmetry fix that prevented "WASD goes dead" after a grace re-snap).

### 3.12 Camera coupling (`ThirdPersonCamera`, full file)
- Orbit-follow on `LateUpdate`. Look comes from `IPlayerInput.Look`; mouse delta uses
  `mouseSensitivity` (no dt — already frame-independent), gamepad/touch stick uses
  `gamepadLookSpeed * Time.deltaTime`, both multiplied by `SettingsStore.LookSensitivity`.
  `LookFromGamepad` (PlayerInputHandler lines 27–35) selects the path.
- Pitch clamped to `[pitchMin, pitchMax]`. Position `SmoothDamp`, rotation `Slerp`. The camera's
  flattened forward/right is the basis for `ComputeMoveDirection`, so movement is camera-relative.

## 4. Formulas

Let `dt = Time.fixedDeltaTime`, `g = Physics.gravity`, `N = _groundNormal`.

- **Active run cap:** `ActiveMoveSpeed = (Time.time < _speedBoostUntil) ? moveSpeed * _speedMultiplier : moveSpeed`
  (`PlayerController` line 93). Base `moveSpeed = GameConstants.DefaultMoveSpeed = 6`.
- **Active jump velocity:** `ActiveJumpSpeed = (Time.time < _jumpBoostUntil) ? jumpSpeed * _jumpMultiplier : jumpSpeed`
  (line 96). Base `jumpSpeed = GameConstants.DefaultJumpSpeed = 8`. Jump sets `vel.y = ActiveJumpSpeed`.
- **Planar accel toward target:** `newPlanar = MoveTowards(currentPlanar, targetPlanar, accel * dt)`,
  `accel = _grounded ? groundAcceleration (60) : airAcceleration (25)`,
  `targetPlanar = desired * ActiveMoveSpeed (+ _arenaSlide if grounded & past grace)`.
- **Movement skip condition:** skip iff `Time.time < _inputLockUntil` OR `planarSpeed > ActiveMoveSpeed + 0.5`.
- **Jump gate:** fire iff `(Time.time − _jumpBufferedTime ≤ jumpBufferTime=0.1)` AND
  `(Time.time − _lastGroundedTime ≤ coyoteTime=0.1)`.
- **Fall multiplier:** while airborne & `vel.y < 0` & not dashing: add
  `g * (FallMultiplier − 1)` as acceleration. `FallMultiplier = 2.2` (`GameConstants`, line 28).
- **Dash burst:** `vel = dir * dashSpeed`, hold `dashDuration` with `AddForce(−g, Acceleration)`
  each step; on end clamp planar to `ActiveMoveSpeed`. `dashSpeed = DefaultDashSpeed = 18`,
  `dashDuration = DefaultDashDuration = 0.18` (`GameConstants`, lines 35–36).
- **Slope walkable test:** `Angle(N, up) ≤ maxSlopeAngle (50°)`.
- **Slope projection:** `alongVel = ProjectOnPlane((vx,0,vz), N)`.
- **Slope anti-slide (idle):** `vel −= ProjectOnPlane(g, N) * dt`.
- **Turn smoothing:** `yaw = SmoothDampAngle(currentYaw, targetYaw, ref _turnVel, TurnSmoothTime=0.08)`,
  `targetYaw = Atan2(desired.x, desired.z) * Rad2Deg`.
- **Knockback impulse:** `AddForce(force + up * KnockbackUpward, Impulse)`,
  `_inputLockUntil = Time.time + inputLockOnKnockback (0.3)`. `KnockbackUpward = 4` (`GameConstants` line 32).
- **Push force:** `Knockback((forward + up*upwardForceShare).normalized * pushForce)`,
  `pushForce = DefaultPushForce = 12`, `pushRange = DefaultPushRange = 1.4`,
  `pushCooldown = DefaultPushCooldown = 0.8`, `upwardForceShare = 0` default.
- **Knockback camera trauma:** `Lerp(0.4, 0.7, clamp01(force.magnitude / max(1, DefaultPushForce)))`.
- **Landing impact (PlayerAnimator):** `impact = clamp01(InverseLerp(SoftLandSpeed=4, HardLandSpeed=12, |vel.y at contact|))`;
  camera shake on landing only if `impact > 0.25`, trauma `= 0.25 * clamp01(impact)`.
- **Hit-stop:** `Time.timeScale = 0.05` for `seconds` (unscaled), restore to captured scale;
  typical `0.06`s (`HitStop.Do`).
- **Kill plane:** eliminate/respawn when `transform.position.y < WorldKillY = −25` (`GameConstants` line 44).

## 5. Edge Cases

- **WASD goes dead after grace re-snap (FIXED):** `ReSnapToSpawn` clears `_inputLockUntil`; without
  it a knockback landing just before a re-snap left the lock in the future forever
  (`PlayerController` lines 193–197).
- **Jump killed by lingering ground read (HANDLED):** slope handling is gated on `vel.y ≤ 0.1` so a
  fresh jump's upward velocity isn't re-projected away the frame after takeoff (lines 427–430).
- **Dash with no input:** falls back to `transform.forward`, then `Vector3.forward` if degenerate
  (`Dash`, lines 349–351).
- **No camera at movement time:** `ComputeMoveDirection` uses world axes so the player can still
  move; `RefreshCamera` re-fetches lazily (lines 466–479, 207–216).
- **Self-hit on ground check / camera collision:** Player+Bot bits stripped from `groundMask`
  (`Awake`) and `collisionMask` (`ThirdPersonCamera.Awake`).
- **Spawn-time gang-up / overlap shove:** during `spawnGrace` (3s) knockback is fully ignored and
  falls/eliminations re-snap instead of killing; arena slide also suppressed until past grace
  (lines 419, 494, 565).
- **Push at zero distance:** a zero-distance `CapsuleCast` returns a (0,0,0) hit point; push uses the
  target's position for the dust scuff instead (`PushInteraction`, lines 69–73).
- **Stale renderer cache after skin swap (HANDLED):** `SetRenderersEnabled` fetches renderers live so
  a destroyed cached renderer can't abort `Eliminate` (lines 595–600).
- **Overlapping hit-stops:** re-entrant `HitStop.Do` restarts against the originally-captured
  timeScale so stacked hits never strand the game at low scale (`HitStop.Runner.Begin`).
- **ReducedMotion:** hit-stop, FOV punch, and screen shake are no-ops (visually) when the toggle is on.
- **Arena slide:** only applied grounded and past grace; cleared with `Vector3.zero` (`SetArenaSlide`).
- **Shield vs spawn grace:** a hit during grace is ignored WITHOUT consuming the shield; the shield
  only absorbs a real post-grace hit (lines 494–501).

## 6. Dependencies

**Depends on:**
- **`GameConstants` (Core)** — `Assets/Scripts/Core/GameConstants.cs`. Layers (Player=8, Bot=9,
  Obstacle=10, Ground=11, Killzone=12), tags, and all tuning defaults (move/jump/dash/push/
  knockback/fall/kill-Y/spawn-separation).
- **Unity New Input System** — via `PlayerInputHandler` (`UnityEngine.InputSystem.PlayerInput`,
  "Gameplay" action map; actions Move/Look/Jump/Push/Pause). Bindings in
  `Assets/InputActions/PlayerInputActions.inputactions` (WASD+mouse, gamepad, touch→rightStick).
- **NavMesh** — used for spawn placement/re-snap context (player honors `DefaultSpawnSeparation`
  min XZ gap from bots; bot side warps to nearest mesh). Player re-snap itself uses `_spawnPoint`.
- **`IRacer` / `RacerRegistry` / `GameEvents` (Core)** — self-registers in `OnEnable`; raises
  `RacerEliminated` / `RacerFinished`.
- **Audio / Visuals / CameraRig** — `AudioManager`, `ImpactPuff`, `ThirdPersonCamera`,
  `SettingsStore` (ReducedMotion, LookSensitivity). Purely additive feel.

**Depended on by:**
- **level-modes-system** — consumes `IRacer.IsAlive/IsFinished/OnEliminated/OnFinished`; KillZone
  triggers and shrink/safe-zone managers call `Eliminate`; below-`WorldKillY` self-eliminates.
- **camera** (`ThirdPersonCamera`) — follows the player transform; movement is camera-relative, so
  the two are mutually coupled (camera reads `IPlayerInput.Look`; movement reads camera basis).
- **multiplayer-system** — a future `NetworkInputProvider` implements `IPlayerInput` and is injected
  via `SetInputSource`; the controller is input-source-agnostic (Phase-0 seam, IPlayerInput.cs).
- **token-economy-system** / **progression-system** — indirect: win/place outcomes are gated on the
  movement-driven elimination/finish. A win pays `TokensForWin=100`; placing pays a rank-scaled
  consolation `max(10, 60−(rank−2)×8)` (`GameManager.HandleLevelEnded`). Note: `TokensForFinish=25`
  is a **dead constant** — it is defined but unused; the live place reward is the consolation formula.
- **bot-ai-system** — shares the `IRacer.Knockback` contract; player push and bot pushes both route
  through it (bots use a NavMeshAgent-driven body, not this controller).

## 7. Tuning Knobs

| Knob | Default | Source | Notes |
|------|---------|--------|-------|
| `moveSpeed` | 6 | `GameConstants.DefaultMoveSpeed` | planar run cap |
| `jumpSpeed` | 8 | `GameConstants.DefaultJumpSpeed` | jump launch `vel.y` |
| `groundAcceleration` | 60 | inspector | snappier when higher |
| `airAcceleration` | 25 | inspector | floaty air control |
| `groundCheckDistance` | 0.2 | inspector | SphereCast reach |
| `groundCheckRadius` | 0.45 | inspector | SphereCast radius |
| `maxSlopeAngle` | 50° | inspector | walkable threshold |
| `coyoteTime` | 0.1 | inspector | post-ledge jump grace |
| `jumpBufferTime` | 0.1 | inspector | early-press memory |
| `airDashEnabled` | true | inspector | toggle dash |
| `dashSpeed` | 18 | `GameConstants.DefaultDashSpeed` | burst speed |
| `dashDuration` | 0.18 | `GameConstants.DefaultDashDuration` | gravity-cancel window |
| `FallMultiplier` | 2.2 | `GameConstants.FallMultiplier` | descent gravity scale |
| `inputLockOnKnockback` | 0.3 | inspector | stun length |
| `spawnGrace` | 3 | inspector | settle/no-kill window |
| `KnockbackUpward` | 4 | `GameConstants.KnockbackUpward` | up component of pushes |
| `pushForce` | 12 | `GameConstants.DefaultPushForce` | shove impulse magnitude |
| `pushRange` | 1.4 | `GameConstants.DefaultPushRange` | capsule-cast reach |
| `pushCooldown` | 0.8 | `GameConstants.DefaultPushCooldown` | shove cadence |
| `upwardForceShare` | 0 | inspector | added to push Y dir |
| `WorldKillY` | −25 | `GameConstants.WorldKillY` | kill plane |
| `TurnSmoothTime` | 0.08 | const | facing smoothing |
| `mouseSensitivity` | 2 | inspector (camera) | deg/mouse-unit |
| `gamepadLookSpeed` | 180 | inspector (camera) | deg/s at full stick |
| HitStop duration | 0.06 | call site | freeze length |
| `SoftLandSpeed` / `HardLandSpeed` | 4 / 12 | const (animator) | landing impact ramp |

Power-up buffs (set by pickups, not movement defaults): `ApplySpeedBoost(multiplier, seconds)`,
`GrantJumpBoost(multiplier, seconds)`, `GrantShield()`. All clamp multiplier ≥ 1 and revert on timer.

## 8. Acceptance Criteria

1. Holding W moves the player along camera-forward; planar speed settles at ~`moveSpeed` (6) and
   never exceeds `moveSpeed + 0.5` under pure input.
2. A jump press up to `jumpBufferTime` (0.1s) before landing fires on touchdown; a press up to
   `coyoteTime` (0.1s) after leaving a ledge still fires. A single press never double-fires.
3. Jump sets `vel.y` to `jumpSpeed` (8); peak rise height is consistent with `8²/(2·9.81)` under
   normal gravity before the fall multiplier kicks in.
4. Falling (after apex) descends faster than the rise due to `FallMultiplier` (2.2); on a walkable
   ramp, the fall multiplier is NOT applied.
5. A second jump press while genuinely airborne (past coyote) and armed fires exactly one dash;
   no further dash until the player lands and re-arms.
6. During the dash window the body moves flat (gravity cancelled); at window end planar speed is
   clamped to `moveSpeed`.
7. On a slope ≤ `maxSlopeAngle` (50°), standing idle the player does not slide; walking, motion
   follows the surface. On a steeper face the player slides down.
8. A push within `pushRange` (1.4) applies knockback to a target `IRacer`; the pushed body's planar
   speed exceeds `moveSpeed + 0.5` and the controller does NOT stomp it back for the carry duration.
9. After taking knockback, input is locked for `inputLockOnKnockback` (0.3s), then WASD responds again.
10. During `spawnGrace` (3s) the player cannot be eliminated (falls/zone hits re-snap to spawn) and
    all knockback is ignored without consuming the shield; after grace, normal rules apply.
11. Falling below `WorldKillY` (−25) eliminates the player (or re-snaps if still in grace).
12. Replacing input via `SetInputSource(IPlayerInput)` drives movement identically with no code change
    to `PlayerController` (verifiable with a stub `IPlayerInput`).
13. A `ReSnapToSpawn` during the grace window leaves WASD responsive (no permanent input lock).
14. Movement is camera-relative: rotating the camera changes the world direction of a fixed input.
```
