# Bot AI System

> Reverse-engineered from existing Unity C# source. Every claim is grounded in code under
> `Assets/Scripts/Bots/`, `Assets/Scripts/Game/BotDifficulty.cs`, `Assets/Scripts/Core/`,
> and `Assets/Scripts/Net/NetworkGame.cs`. Paths are relative to the project root
> `/mnt/c/Users/Mohammed Awad/Desktop/games/unity-projects/StumbleClone/`.
>
> System slug: **bot-ai-system**. MVP field = player + 7 bots.

## 1. Overview

The Bot AI System fills every level with 7 CPU racers (`GameConstants.DefaultBotsPerLevel = 7`)
that race, survive, and brawl alongside the human player. Each bot is a `BotController`
(`Assets/Scripts/Bots/BotController.cs`) that implements the shared `IRacer` contract
(`Assets/Scripts/Core/IRacer.cs`) and is driven by a `NavMeshAgent` while its `Rigidbody` is held
**kinematic** in normal locomotion; the body goes **dynamic** only during knockback, jumps, and
off-platform recovery, then re-snaps to the NavMesh. `BotSpawner`
(`Assets/Scripts/Bots/BotSpawner.cs`) instantiates the field, reserves the player's spawn point,
samples each spawn onto the baked NavMesh, assigns a per-bot skill roll and a per-mode `BotBehavior`
strategy, and applies difficulty-based combat tuning. Three behavior strategies
(`RaceBotBehavior`, `SurvivalBotBehavior`, `LastStandBotBehavior`) implement per-mode goals.
`BotAnimator` mirrors the player's animation parameter contract; `BotNameGenerator` assigns unique
goofy names; `BotDifficulty` (`Assets/Scripts/Game/BotDifficulty.cs`) maps a player-chosen
Easy/Normal/Hard setting to skill ranges and an aggression scalar that scales the whole field.

## 2. Player Fantasy

The bots exist so the human never races an empty track. They should read as *distinct rivals* with
earned finishing order, not arbitrary filler. Source comments state the intent directly: skill
"drives both move speed and behavior aggression/reaction so the 7 bots feel distinct and finishing
order is earned, not arbitrary" (`BotSpawner.cs` line ~192). On Hard, bots "gang the player" —
aggression "widens player-lock range, pursuit past the safe ring, and edge-directed pushes"
(`BotDifficulty.cs`). The recovery logic exists so a shoved bot scrambles back "instead of just
falling to its death" (`BotController.cs` line ~125), preserving the comedic stumble-and-recover
feel rather than letting rivals silently vanish.

## 3. Detailed Rules

### 3.1 Lifecycle & registration
- `BotController` requires `Rigidbody`, `CapsuleCollider`, `NavMeshAgent`
  (`[RequireComponent]`, `BotController.cs` line 9).
- `Awake`: `Rigidbody.isKinematic = true`, `interpolation = Interpolate`,
  `collisionDetectionMode = Continuous`, `Agent.speed = GameConstants.DefaultMoveSpeed` (6).
- `OnEnable`: registers in `RacerRegistry` and calls `behavior.OnAttach`. `OnDisable`:
  detaches behavior and unregisters from `RacerRegistry` — so win/rank logic stops counting a
  despawned bot (`BotSpawner.DespawnExtraBots` comment).
- `DisplayName` falls back to `"Bot_" + racerId` when no name is assigned.

### 3.2 Per-frame `Update` (BotController.cs lines 106–140)
Order, while alive and not finished:
1. `UpdateSpeedBoost()` every frame (restores captured base agent speed when a SPEED buff lapses).
2. If `position.y < GameConstants.WorldKillY (-25)` → `Eliminate()` and return.
3. If `_inKnockback` → return (the knockback routine owns the body).
4. **Off-mesh check:** `offMesh = Agent == null || !Agent.enabled || !Agent.isOnNavMesh`. If
   `offMesh && !_jumping` → enter/continue recovery (`RecoverTick`) and return.
5. Otherwise, every `GameConstants.BotPathRefreshRate (0.5s)`, call `behavior.Tick(this)`.

### 3.3 Movement
- `SetDestination(worldPos)` is the only locomotion entry point; it refuses while not alive,
  finished, in knockback, or off-mesh (agent null/disabled/not-on-navmesh).
- `IsGrounded()`: downward raycast over `collider.height*0.5 + 0.15`, with the Player and Bot
  layers masked out so a bot can't self-report grounded off its own/another racer's capsule
  (which would block the recovery double-jump).

### 3.4 Jumping (BotController.cs lines 226–246)
- `Jump()` requires alive, not finished, not in knockback, and `IsGrounded()`.
- `JumpRoutine`: sets `_jumping`, **disables the agent**, goes dynamic, sets `vel.y = ActiveJumpSpeed`,
  waits `jumpAgentLockSeconds (0.35s)`, then `ResnapToNavMesh()`. If the jump carried the bot off the
  mesh, `RecoverTick` takes over.
- `ActiveJumpSpeed` = `jumpSpeed (8)` × SUPER-JUMP multiplier while that buff is live, else `jumpSpeed`.

### 3.5 Pushing / knockback (combat)
- `TryPush(target)` shoves radially (away from self); `TryPushToward(target, worldDir)` shoves along
  an explicit world direction (used to push toward the rim / off the platform).
- `DoPush`: ignores self; gated by `Time.time < _nextPushTime`; ignores dead/finished targets;
  requires target within `pushRange (1.4)`. On success sets
  `_nextPushTime = now + pushCooldown(0.8) * _pushCooldownMul` and calls
  `target.Knockback(dir * pushForce(12) * _pushForceMul)`.
- `Knockback(force)` on the *receiving* bot: ignores if dead/finished; **re-entrant guard** —
  a bot already `_inKnockback` ignores additional pushes; a live SHIELD buff absorbs exactly one
  hit and is consumed. Otherwise starts `KnockbackRoutine`.
- `KnockbackRoutine`: disables agent, goes dynamic, waits one `FixedUpdate` (frame-rate-independent
  impulse), applies `force + Vector3.up * GameConstants.KnockbackUpward(4)` as an Impulse, waits
  `knockbackRecoverySeconds (1.2s)`, then `ResnapToNavMesh()` and clears `_inKnockback`.

### 3.6 Power-up buffs (additive, neutral when inactive)
- **SPEED** (`ApplySpeedBoost(multiplier, seconds)`): captures base agent speed on a fresh grant,
  scales `Agent.speed` by `max(1, multiplier)`; re-grant refreshes the timer against the original
  base (no compounding); `UpdateSpeedBoost` restores base on expiry.
- **SHIELD** (`GrantShield`): one-use, no timer; consumed by the next `Knockback`.
- **SUPER JUMP** (`GrantJumpBoost(multiplier, seconds)`): scales jump impulse for the window.
- `Respawn(position)` clears all in-flight buffs.

### 3.7 Per-mode behaviors (`BotBehavior` strategy, ticked every 0.5s)
- **Race** (`RaceBotBehavior.cs`): per-bot random jitter offset (`jitterRadius 1.5`) on attach;
  every tick `SetDestination(finishLine.position + jitter)`; forward raycast probe
  (`obstacleProbeDistance 2`) from `+0.6` up — if it hits a collider tagged `"Obstacle"` while
  grounded, `Jump()`.
- **Survival** (`SurvivalBotBehavior.cs`): `OverlapSphereNonAlloc` (buffer size 8) over the Killzone
  layer within `killzoneScanRadius (6)`; if a killzone is near, flee = anchor + away*2; else go to
  `safeAnchor`; else wander to a random NavMesh point within `wanderRadius (8)`. Destination is
  NavMesh-sampled (radius 4) before being committed.
- **Last Stand** (`LastStandBotBehavior.cs`): priority order — (1) dodge nearest `ArenaObstacle`
  hazard (scan radius `dodgeScanRadius 5.5`, react radius scales with reflex, hop if close/imminent);
  (2) compute shrink-aware safe ring (uses `ArenaShrinker.Center/CurrentSafeRadius` when active, else
  the static `arenaRadius * safeRingFraction 0.8`); (3) select a victim (prefers the human player
  within an aggression-scaled lock range, else nearest racer via `RacerRegistry.All`), charge if
  within charge range and allowed past the hunt ring, and on contact `TryPushToward` the victim
  **outward toward the rim** (off the platform); (4) otherwise steer to the ring centre.

## 4. Formulas (skill / aggression → speed, combat, AI)

All `Mathf.Lerp(a, b, t)` below are linear interpolations from `a` (t=0) to `b` (t=1).

### 4.1 Skill roll & aggression source (`BotDifficulty.cs`)
| Difficulty | skill range `[min, max]` | `Aggression` |
|-----------|--------------------------|--------------|
| Easy   | `[0.10, 0.40]` | `0.20` |
| Normal | `[0.35, 0.80]` | `0.55` |
| Hard   | `[0.75, 1.00]` | `1.00` |

Per bot: `skill = Random.Range(skillMin, skillMax)`; `aggression = BotDifficulty.Aggression`
(`BotSpawner.cs` lines ~132, 194–195).

### 4.2 Agent base speed (`BotSpawner.cs` lines ~213–218)
```
Agent.speed   = GameConstants.DefaultMoveSpeed(6)
              * Mathf.Lerp(1.0f, 1.28f, skill)
              * Mathf.Lerp(1.0f, 1.18f, aggression)
Agent.acceleration = 40f
Agent.angularSpeed = 520f
```
Floored at the player's run speed (6) even for low skill; skill/aggression push past it.
Effective range ≈ `6` (skill 0, aggr 0) to `6 * 1.28 * 1.18 ≈ 9.06` (skill 1, aggr 1).

### 4.3 Combat tuning (`BotSpawner.cs` line ~220 → `BotController.SetCombatTuning`)
```
cooldownMul = Mathf.Lerp(1f, 0.5f, aggression)   // <1 = pushes more often
forceMul    = Mathf.Lerp(1f, 1.3f, aggression)   // >1 = hits harder
```
`SetCombatTuning` clamps each to `>= 0.1`. Applied in `DoPush`:
`pushCooldown(0.8) * cooldownMul` and `pushForce(12) * forceMul`. On Hard (aggression 1): half
cooldown (0.4s), +30% force (15.6).

### 4.4 Last-Stand AI math (`LastStandBotBehavior.cs`)
```
reflex        = Mathf.Max(skill, aggression)
reactRadius   = Mathf.Lerp(dodgeScanRadius*0.7, dodgeScanRadius*1.15, reflex)   // 5.5 base
jumpRange     = dodgeScanRadius * 0.5
imminentRange = dodgeScanRadius * 0.28
hop hazard if dist<=jumpRange AND (Random.value < reflex OR dist<=imminentRange)

huntRing      = Mathf.Lerp(safeRing, effectiveRadius*0.96, aggression)
charge        = chargeRange(5) * Mathf.Lerp(0.85, 2.3, Mathf.Max(skill, aggression))
player lockRange = Mathf.Lerp(chargeRange*0.8, arenaRadius*2.2, aggression)
```
`contactRange` defaults to `GameConstants.DefaultPushRange (1.4)`; dodge target is pulled 0.35
toward arena centre (`Vector3.Lerp(dodge, center, 0.35)`).

### 4.5 Edge recovery air-control (`BotController.RecoverTick`)
```
vel.x = MoveTowards(vel.x, dir.x * recoveryMoveSpeed(9), recoveryAirAccel(28) * dt)
vel.z = MoveTowards(vel.z, dir.z * recoveryMoveSpeed(9), recoveryAirAccel(28) * dt)
facing: Quaternion.Slerp(rot, LookRotation(dir), 0.25)
canAirJump if !grounded && vel.y < 2.5; jump sets vel.y = jumpSpeed(8)
MaxRecoveryJumps = 2 (ground hop + one mid-air), recoveryJumpInterval = 0.32s
```

### 4.6 Animation normalization (`BotAnimator.cs`)
`speed01 = Clamp01(planarAgentVelocity / max(0.01, maxSpeedForNormalization))` where
`maxSpeedForNormalization` defaults to `GameConstants.DefaultMoveSpeed (6)`. Writes Animator floats
`Speed` and bool `Grounded`; falls back to `ProceduralCharacterAnimator` when no real clips exist.

## 5. Edge Cases

- **Knocked off platform / off NavMesh:** `Update` detects `offMesh && !_jumping`, sets
  `_recovering`, refills `MaxRecoveryJumps (2)`, and runs `RecoverTick` each frame: drives the
  dynamic body horizontally toward the nearest sampled NavMesh point (within `recoveryScanRadius 20`)
  or the `RecoveryAnchor`, double-jumps to clear the lip, and re-snaps + rebinds the agent the moment
  it lands on solid NavMesh (`BotController.cs` lines 144–207).
- **Frozen-agent / "bots stand still or die randomly" rescue (stuck timeout):** if a bot is off-mesh
  longer than `GameConstants.BotRecoveryTimeout (4s)` and a `RecoveryAnchor` is set, `RecoverTick`
  hard-warps it: zeroes velocity, makes the body kinematic, sets `transform.position` to a
  `NavMesh.SamplePosition` hit (radius **25**) around the anchor, re-enables the agent and `Warp`s it
  (`BotController.cs` lines 154–168). Comment: prevents wedging against a lip or riding physics off
  the edge.
- **Spawn-point NavMesh snap:** spawn points sit ~0.6m above the surface — too far for the agent to
  auto-connect ("Failed to create agent because it is not close enough to the NavMesh"), which leaves
  bots frozen. `SpawnInternal` `NavMesh.SamplePosition`s each spawn (radius **8**) before instantiating
  (`BotSpawner.cs` lines ~173–178).
- **Spawn overlap with player:** any spawn point within `GameConstants.DefaultSpawnSeparation (1.5)`
  XZ of the player is skipped, so two colliders don't stack and PhysX doesn't launch the player off the
  map at start. If filtering removes every point, it falls back to all originals
  (`BotSpawner.cs` lines ~141–159). Player is found by tag, falling back to `RacerRegistry.Player`.
- **Knockback re-entrancy:** a bot already `_inKnockback` ignores further pushes, preventing chained
  shoves from a 7-bot scrum from stacking `KnockbackRoutine`s (each disabling the agent 1.2s) and
  flinging the bot off frame-rate-dependently (`BotController.cs` lines 286–302).
- **Universal recovery anchor:** every bot in every mode is guaranteed a non-null `RecoveryAnchor`
  (preference: `arenaCenter` → `safeAnchor` → `finishLine` → the spawner transform) so a shoved bot
  never falls to its death for lack of a fallback (`BotSpawner.cs` lines ~199–205).
- **ResnapToNavMesh velocity warning guard:** velocity is zeroed only while the body is still dynamic,
  to avoid Unity's per-call warning on a kinematic body (`BotController.cs` lines 321–343).
- **Empty/degenerate config:** `SpawnInternal` logs an error and spawns nothing if `botPrefab` is null
  or no spawn points are assigned; a prefab missing `BotController` is destroyed and skipped.

## 6. Dependencies

Bidirectional. Slugs: movement-system / bot-ai-system / level-modes-system / token-economy-system /
progression-system / multiplayer-system.

### Depends on
- **Unity NavMesh** (`com.unity.ai.navigation`): `NavMeshAgent`, `NavMesh.SamplePosition`, `Warp`,
  `isOnNavMesh`. Core of locomotion and recovery (`BotController.cs`, `BotSpawner.cs`,
  `SurvivalBotBehavior.cs`).
- **`GameConstants`** (`Assets/Scripts/Core/GameConstants.cs`): `DefaultBotsPerLevel(7)`,
  `DefaultMoveSpeed(6)`, `DefaultJumpSpeed(8)`, `DefaultPushForce(12)`, `DefaultPushRange(1.4)`,
  `DefaultPushCooldown(0.8)`, `KnockbackUpward(4)`, `BotPathRefreshRate(0.5)`, `WorldKillY(-25)`,
  `BotRecoveryTimeout(4)`, `DefaultSpawnSeparation(1.5)`, layer/tag IDs.
- **level-modes-system**: provides `LevelMode` (`Assets/Scripts/Core/LevelMode.cs`) and the scene
  anchor references the spawner consumes — `finishLine`, `arenaCenter`, `safeAnchor`, `arenaRadius`,
  spawn-point transforms. `LastStandBotBehavior` reads `ArenaShrinker.Active/Center/CurrentSafeRadius`
  and scans `ArenaObstacle` hazards.
- **`IRacer` / `RacerRegistry` / `GameEvents`** (`Assets/Scripts/Core/`): `BotController` implements
  `IRacer`, registers itself, raises `GameEvents.RaiseRacerEliminated/Finished`. `LastStandBotBehavior`
  reads `RacerRegistry.All` and `RacerRegistry.Player` for target selection.
- **movement-system**: shares `GameConstants` racer tuning and the `IRacer.Knockback` contract;
  bot pushes deliver knockback to the player's `IRacer` and vice-versa.
- **Animation** (`StumbleClone.Animation`): `BotAnimator` uses `AnimatorClipUtil` and
  `ProceduralCharacterAnimator`, mirroring `PlayerAnimator`'s `Speed`/`Grounded` parameters.

### Depended on by
- **level-modes-system**: consumes bots as `IRacer` racers for finishing order / win-rank logic
  (bots register/unregister in `RacerRegistry`; despawn unregisters so rank stops counting them).
- **multiplayer-system** (`Assets/Scripts/Net/NetworkGame.cs`): server-side bot backfill drives the
  spawner via `SetNetworkedMode(true)`, `SpawnBots`, `DespawnExtraBots`, and reads `SpawnedBotCount` to
  reconcile humans + bots to `NetworkGame.NetworkedFieldSize (8)`.
- **token-economy-system / progression-system**: indirect — winning/finishing against the bot field
  drives the `TokensForWin(100)` win reward + the rank-scaled consolation `max(10, 60−(rank−2)×8)`
  (awarded by level/economy systems via `GameManager`, not this one; `TokensForFinish=25` is a dead
  constant, not the live place reward). No direct code reference from bot scripts.

## 7. Tuning Knobs

### `GameConstants` (`Assets/Scripts/Core/GameConstants.cs`)
| Constant | Value | Effect |
|---|---|---|
| `DefaultBotsPerLevel` | 7 | offline field size |
| `DefaultMoveSpeed` | 6 | agent base speed floor + anim normalization |
| `DefaultJumpSpeed` | 8 | jump & recovery-jump impulse |
| `DefaultPushForce` | 12 | base shove force |
| `DefaultPushRange` | 1.4 | shove gate distance / contact range |
| `DefaultPushCooldown` | 0.8 | base shove cooldown |
| `KnockbackUpward` | 4 | upward component of every knockback impulse |
| `BotPathRefreshRate` | 0.5 | seconds between behavior ticks |
| `WorldKillY` | -25 | elimination floor |
| `BotRecoveryTimeout` | 4 | off-mesh stuck-rescue warp delay |
| `DefaultSpawnSeparation` | 1.5 | min XZ gap from player spawn |

### `BotController` `[SerializeField]` (per-prefab)
`knockbackRecoverySeconds 1.2`, `navResamplingRadius 3`, `jumpAgentLockSeconds 0.35`,
`recoveryScanRadius 20`, `recoveryMoveSpeed 9`, `recoveryAirAccel 28`, `recoveryJumpInterval 0.32`
(+ `MaxRecoveryJumps` const 2). `jumpSpeed`, `pushForce`, `pushCooldown`, `pushRange` default from
`GameConstants` but are serialized (overridable per prefab).

### `BotSpawner` `[SerializeField]`
`botCount` (default `DefaultBotsPerLevel`), `mode` (LevelMode), `finishLine`, `arenaCenter`,
`safeAnchor`, `arenaRadius 15`, `spawnOnStart true`, `firstRacerId 1`, `spawnPointOffset 0`.
Hard-coded in `SpawnInternal`: agent `acceleration 40`, `angularSpeed 520`, speed Lerp factors
`1.0→1.28` (skill) and `1.0→1.18` (aggression), combat Lerps `1→0.5` (cooldown) and `1→1.3` (force),
NavMesh spawn sample radius 8.

### Behavior constructor params
- Race: `jitterRadius 1.5`, `obstacleProbeDistance 2`.
- Survival: `killzoneScanRadius 6`, `wanderRadius 8`.
- Last Stand: `safeRingFraction 0.8`, `chargeRange 5`, `contactRange = DefaultPushRange`,
  `skill` (rolled), `aggression` (difficulty), `dodgeScanRadius 5.5`.

### Difficulty (`BotDifficulty.cs`)
Skill ranges and aggression per Easy/Normal/Hard (see §4.1). Persisted in `PlayerPrefs`
(`"stumbleclone.botdifficulty"`), default Normal; set from the title screen.

### Networked (`NetworkGame.cs`)
`NetworkedFieldSize 8` — server-authoritative target (humans + backfill bots).

### `BotNameGenerator.cs`
20-name `Pool` (Wobble, Tumble, Bumper, …); unique-per-field, resets on full field build or when
the pool is exhausted; `"Bot_####"` fallback.

## 8. Acceptance Criteria

1. **Field count:** Offline, a level with `spawnOnStart` true and `_networkedMode` false spawns
   exactly `botCount` (default 7) bots, ids `firstRacerId..firstRacerId+6`, each with a unique name.
2. **No player launch at start:** No bot spawns within `DefaultSpawnSeparation (1.5)` XZ of the
   player's spawn point; the player is not flung off the map by spawn-overlap depenetration.
3. **NavMesh binding:** Every spawned bot lands on the baked NavMesh (sampled, radius 8) so its agent
   binds and it moves — no frozen "not close enough to the NavMesh" bots.
4. **Difficulty scales the field:** Switching Easy→Normal→Hard measurably raises average agent speed
   (per §4.2) and shove frequency/force (per §4.3); Hard bots lock onto and herd the player toward the
   edge (lock range and hunt ring widen with aggression).
5. **Off-platform recovery:** A bot shoved off but with NavMesh/anchor in reach scrambles back
   (air-control + ≤2 jumps) and resumes normal AI; it does not silently fall to its death when an
   anchor exists.
6. **Stuck rescue:** A bot wedged off-mesh longer than `BotRecoveryTimeout (4s)` with a
   `RecoveryAnchor` is hard-warped onto the nearest mesh (radius 25 around the anchor) and resumes —
   no permanently frozen or randomly-dying bots.
7. **Knockback is single-shot & frame-rate independent:** A bot already in knockback ignores further
   pushes; SHIELD absorbs exactly one hit; the impulse is applied after one FixedUpdate.
8. **Kill floor:** A bot below `WorldKillY (-25)` is eliminated and raises
   `GameEvents.RaiseRacerEliminated`.
9. **Mode goals:** Race bots head for the finish line (jumping tagged obstacles); Survival bots flee
   killzones toward the safe anchor; Last-Stand bots dodge hazards, retreat with the shrinking ring,
   and shove victims outward.
10. **Networked backfill:** With a live server, `BotSpawner` is put in networked mode (offline
    auto-fill suppressed) and the bot count is reconciled so humans + bots == `NetworkedFieldSize (8)`;
    despawned bots unregister from `RacerRegistry`.
