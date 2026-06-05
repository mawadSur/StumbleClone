# Level Modes System (GDD)

> **Reverse-engineered from code, not authored ahead of it.** Every claim below cites
> the source file it was read from. Where the code is silent, the section says
> "None — <reason>" rather than inventing intent. Paths are relative to project root
> `unity-projects/StumbleClone/`.
>
> **Slug:** `level-modes-system`. This system OWNS the `GameEvents` bus, the three
> win conditions, elimination resolution, and round flow.

---

## 1. Overview

The Level Modes System is the round runtime for StumbleKids. It defines three game
modes — **Race**, **Survival**, and **Last-Standing (a.k.a. "Knockout")** — and the
shared infrastructure that starts a round, tracks racers, decides who wins, and
broadcasts everything else via a global event bus. One human player competes against
7 bots (`GameConstants.DefaultBotsPerLevel = 7`). Each mode lives in its own scene
(`Level_Race`, `Level_Survival`, `Level_LastStanding`) and is driven by exactly one
manager (`RaceManager` / `SurvivalManager` / `LastStandingManager`,
`Assets/Scripts/Game/`). `GameManager` owns cross-scene startup (countdown freeze,
`LevelStarted`), and end-of-round payout/leaderboard recording. `LevelMode`
(`Assets/Scripts/Core/LevelMode.cs`) is the 3-value enum (`Race=0, Survival=1,
LastStanding=2`) keyed throughout.

---

## 2. Player Fantasy

Documented intent appears in code comments, not separate design text:

- **Last-Standing / Knockout** — "Knockout arena mode. The threat is a stream of
  hazards spawned from the platform rim that escalate with time and eliminations …
  the round ends when only one racer remains" (`LastStandingManager.cs`). The
  headline pressure is a floor that **visibly shrinks** — players physically watch
  the safe ground close in and must move inward, dying only by falling off
  (`ArenaShrinker.cs` header). This is the **free default mode** (`LevelProgress.cs`).
- **Race** — implied by `FinishLine` + `Checkpoint`: a footrace to a finish line
  where falling is recoverable (respawn at last checkpoint), not fatal (`KillZone.cs`).
- **Survival** — outlast a countdown without being knocked off; falling is fatal
  (`KillZone.cs`, `SurvivalManager.cs`).
- Cross-mode: the human "gets one life in every mode: falling off eliminates for
  good (no respawn)" — except Race, where the player still respawns (see Edge Cases).

---

## 3. Detailed Rules — the 3 modes

Shared: all racers implement `IRacer` (`RacerId, IsAlive, IsFinished, IsPlayer,
Eliminate(), Finish(), Respawn()`) and self-register in `RacerRegistry`
(`OnEnable`/`OnDisable`). Managers read `RacerRegistry.All` / `.AliveCount` /
`.Player`. A round begins on `GameEvents.LevelStarted`; a round ends when its manager
raises `GameEvents.LevelEnded(winner)`. Each manager guards re-entry with a private
`_ended` flag.

### 3a. Race (`RaceManager.cs`)
- **Goal:** reach the `finishLine`. `FinishLine.OnTriggerEnter` calls `racer.Finish()`
  for any alive, not-yet-finished `IRacer` (`Level/FinishLine.cs`).
- **Finish order:** each `RacerFinished` appends to `_finishers`; placement = list
  index + 1, broadcast via `RacerRankChanged(racer, rank)`.
- **Win / round-end condition (any one triggers `EndLevel`):**
  1. `_finishers.Count >= finishersToEnd` (default **4**, `[SerializeField]`), OR
  2. all registered racers finished (`AllRegisteredFinished()`), OR
  3. player finished AND `finishOnPlayerFinish == true` (default **false**).
- **Winner** = `_finishers[0]` (first across the line), or `null` if none.
- **Live player rank:** every `Update`, rank = (#already-finished) + 1 + (# alive
  non-player racers closer in XZ to the finish line); exposed as `PlayerRank`.
- **Elimination:** none in Race. Falling triggers **respawn**, not elimination
  (`KillZone.cs`, Race branch → `racer.Respawn(checkpoint or default)`).

### 3b. Survival (`SurvivalManager.cs`)
- **Goal:** be the last racer alive OR survive until the timer expires.
- **Timer:** `totalDuration` (default **60s**, `[SerializeField]`) counts down in
  `Update` using `Time.deltaTime`; emits `SurvivalTimerTick(secondsRemaining)` once
  per accumulated whole second and at 0.
- **Round-end conditions (`EndLevel`):**
  1. `_timeRemaining <= 0` (timer expired), OR
  2. `RacerRegistry.AliveCount <= 1` (checked on every `RacerEliminated` and each
     `Update` via `CheckEnd()`).
- **Winner** = first racer in `RacerRegistry.All` with `IsAlive == true`, else `null`.
  (On a timeout with several alive, this is simply the first alive in registry order —
  no tiebreak by anything else.)
- **Elimination:** falling into the kill zone calls `racer.Eliminate()` for **all**
  racers including the player (`KillZone.cs`, `Survival` branch — one life, no respawn).

### 3c. Last-Standing / Knockout (`LastStandingManager.cs`)
- **Goal:** be the last racer standing. No timer, no finish line.
- **Threat:** a runtime `ObstacleSpawner` (created by `EnsureSubsystems()` on
  `LevelStarted`) streams rim hazards, **plus** a shrinking platform (`ArenaShrinker`,
  self-bootstrapped on the `Level_LastStanding` scene only).
- **Round-end condition (`EndLevel`):** `RacerRegistry.AliveCount <= 1` (checked on
  every `RacerEliminated`). On end, `_spawner.StopSpawning()` then raise `LevelEnded`.
- **Winner** = first racer in `RacerRegistry.All` with `IsAlive == true`, else `null`.
- **Elimination:** fall off the shrinking floor into the kill zone → `racer.Eliminate()`
  for **all** racers including the player (`KillZone.cs`, `LastStanding` branch). There
  is **no invisible-radius elimination** — death is fall-only (`ArenaShrinker.cs` header).

### Mode unlock gating (`LevelProgress.cs`)
- **Knockout (LastStanding):** free (`PriceOf == 0`), always `IsUnlocked == true` — the
  default mode.
- **Race:** **100 tokens** to unlock. **Survival:** **150 tokens**.
- `TryUnlock` spends via `TokenWallet.TrySpend`, persists `PlayerPrefs` key
  `stumbleclone.modeunlocked.<modeInt>`. Display names: Race / Survival / **"Knockout"**
  (LastStanding renames to Knockout for the player-facing string).

---

## 4. Formulas (actual values + file refs)

**Shrink curve** (`ArenaShrinker.cs`, all `private const`, scene-feel knobs, not
`GameConstants`):
- `GracePeriod = 15s` — hold full radius after round start.
- `ShrinkDuration = 60s` — contract from full → min.
- `MinRadiusFraction = 0.3` — final radius = 30% of full.
- `DefaultFullRadius = 20f` — used only if the Arena mesh can't be measured.
- Target radius each frame:
  - `elapsed <= 15` → `fullRadius`
  - else → `Lerp(full, full*0.3, SmoothStep(0,1, clamp01((elapsed-15)/60)))`
- `WarnLookahead = 2.5s` — danger band inner edge sits where the floor WILL be 2.5s
  ahead (same SmoothStep curve, rescaled `currentRadius/targetRadius` to glue to the
  live measured edge), clamped to `[minRadius, currentRadius]`.
- `NavRebuildInterval = 1.5s` — throttled NavMesh rebuild cadence while shrinking,
  plus one guaranteed final rebuild at min radius.
- Live radius is **measured from real mesh geometry** (`meshHalfExtent * lossyScale`,
  min of X/Z), so it is correct regardless of `ArenaResizer`'s 1.6x enlargement.

**Danger-band blink** (`SafeZoneRing.cs`): `PulseSpeed = 4.2`; alpha pulses
`Lerp(0.18, 0.62, 0.5+0.5*sin(t*4.2))`; under `SettingsStore.ReducedMotion` holds a
steady `0.42` (no blink). Band hidden when width `<= 0.05`.

**Race rank** (`RaceManager.cs`): finish rank = finish-list index + 1. Live in-progress
rank (XZ squared distance to finish): `(#finished) + 1 + (# alive non-player racers
with smaller sqr-dist to the finish line)`.

**Survival timer** (`SurvivalManager.cs`): `_timeRemaining -= Time.deltaTime`; tick
emitted each time `_tickAccumulator >= 1f`.

**Leaderboard score** — `LevelResult.ScoreFor(mode, rank, duration, won)`
(`LevelResult.cs`), higher is better:
- `rankBonus = max(0, 9 - rank) * 10000` (when `rank > 0`).
- Race: `max(0, 100000 - duration*100) + rankBonus`.
- Survival: `duration*100 + (won ? 50000 : 0)`.
- LastStanding: `rankBonus + duration*20 + (won ? 50000 : 0)`.

**Token reward** (`GameManager.HandleLevelEnded`): win → `GameConstants.TokensForWin
= 100`; otherwise consolation `max(10, 60 - (playerRank - 2)*8)` (2nd ≈ 52, floored at
10). A Token Doubler charge (`AbilityStore.ConsumeDoubler()`) doubles a **win** only.
Reward recorded on `lastResult.tokensAwarded` / `.doublerUsed`. **Awarded only when a
human player was in the run (`player != null`).**

---

## 5. Edge Cases

- **Fall-only kill / mode-aware kill zone** (`KillZone.cs`): player ALWAYS calls
  `Eliminate()` on falling (one life, even in Race). Non-player racers: Race → respawn
  at last `Checkpoint` (or `defaultRespawnPoint = (0,2,0)`); Survival & LastStanding →
  `Eliminate()`. Mode resolved robustly: `GameManager.currentMode` → `LevelSelfStart.Active.Mode`
  → serialized `fallbackMode` (Race). Without the `LevelSelfStart` hop, a directly-played
  LastStanding scene would fall back to Race and *respawn* a fallen player instead of
  eliminating them.
- **Danger-band blink suppression** (`SafeZoneRing.cs`): `ReducedMotion` swaps the
  pulsing alpha for a steady 0.42; band auto-hides at near-zero width once the floor
  settles. No auto-kill is tied to the band — it is a pure visual telegraph.
- **NavMesh rebuild during shrink** (`ArenaShrinker.ThrottledNavRebuild`): rebaking
  every frame would hitch, so rebuilds run every 1.5s while shrinking + one final bake
  at min radius, keeping bots on the closing floor. Synchronous `BuildNavMesh()` on all
  `NavMeshSurface`. Ordering: `ArenaResizer` scales the platform 1.6x in its
  `sceneLoaded` callback (plus a deferred one-frame-later rebuild via `ArenaResizerRunner`)
  BEFORE `ArenaShrinker` measures geometry on `LevelStarted`.
- **Play-scene-directly** (`LevelSelfStart.cs`): if no `GameManager` exists, this raises
  `LevelStarted` one frame after scene load so managers stand up subsystems; dormant
  under the normal menu flow (no double-start).
- **Countdown freeze** (`GameManager.RaiseLevelStartedNextFrame`): `Time.timeScale = 0`
  during the `RoundIntro` countdown; `LevelStarted` + `_levelStartTime` pinned to "GO!"
  so durations exclude the countdown. A 6s guard timeout force-releases if the overlay
  dies, preventing a stuck timeScale. On `sceneLoaded` it cancels any stale intro
  coroutine and resets timeScale to 1.
- **Scene reload does NOT Reset() the bus or Clear() the registry** (`GameManager.HandleSceneLoaded`):
  per-scene racers/managers/HUDs register in `OnEnable` (before the `sceneLoaded`
  callback), so clearing here would wipe fresh registrations. Cleanup is left to each
  object's `OnDisable`. Risk: if any object fails to unsubscribe, stale handlers persist
  across scenes (the `LevelEnded` double-react below is the live example).
- **Survival/LastStanding winner on a tie** = first alive in registry order (no skill
  tiebreak). Survival timeout with multiple alive picks that same first-alive racer.

---

## 6. Dependencies

**Owns:** `GameEvents` bus (`Core/GameEvents.cs`), `IRacer` contract, `RacerRegistry`,
the three managers, `LevelResult`, `LevelProgress`, `KillZone`/`FinishLine`/`Checkpoint`,
`ArenaShrinker`/`SafeZoneRing`/`ArenaResizer`.

**Depends on (upstream):**
- **`movement-system`** + **`bot-ai-system`** — the racers. `PlayerController` and
  `BotController` implement `IRacer`; this system calls `Eliminate/Finish/Respawn` and
  reads `IsAlive/IsFinished/IsPlayer/Transform`. Bots also read `ArenaShrinker.CurrentSafeRadius`
  / `.Center` to retreat (per `ArenaShrinker.cs` comment).
- `ObstacleSpawner` (Knockout hazards), `NavMeshSurface` (`com.unity.ai.navigation`),
  `RoundIntro`/`SpectateController` (UI), `TokenWallet`/`AbilityStore`/`LeaderboardStore`
  (read by `GameManager` at round end).

**Depended on by (downstream — all subscribe to the bus):**
- **`token-economy-system`** — `GameManager.HandleLevelEnded` awards tokens + records
  the leaderboard (`token-economy-system`/leaderboard).
- **`progression-system`** — `QuestSystem` (`LevelEnded`, `RacerEliminated`) and
  `SeasonPass` (`LevelEnded`) advance quests / pass XP.
- HUDs/feedback: `RaceHUD`, `SurvivalHUD`, `LastStandHUD`, `EndScreenUI`, `VictoryScreen`,
  `EliminationFeed`, `EliminationFx`, `SpectateController`, `GameAudioHooks`, `Analytics`,
  `PowerupSpawner` (all subscribe per grep of `GameEvents.*`).

### GameEvents signatures & fire order (CRITICAL for downstream GDDs)

Definitions (`Core/GameEvents.cs`):
```
event Action<LevelMode>      LevelStarted;        // round begins
event Action<IRacer>         LevelEnded;          // winner, or null
event Action<IRacer>         RacerFinished;       // crossed finish (Race)
event Action<IRacer>         RacerEliminated;     // knocked out (Survival/LastStanding/player)
event Action<IRacer,int>     RacerRankChanged;    // (racer, rank — 1-based)
event Action<float>          SurvivalTimerTick;   // seconds remaining
event Action<string,SpawnDirection> WaveTelegraphed; // (patternName, rim direction)
```
**Fire order on a win/finish:**
- **Race finish:** `FinishLine` → `racer.Finish()` → racer raises `RacerFinished` →
  `RaceManager.HandleRacerFinished` raises `RacerRankChanged(racer, rank)`, then if a
  win condition is met → `RaceManager.EndLevel` raises `LevelEnded(_finishers[0])`.
- **Survival / LastStanding elimination:** `KillZone` → `racer.Eliminate()` → racer
  raises `RacerEliminated` → manager `HandleRacerEliminated` → if `AliveCount <= 1` →
  `EndLevel` raises `LevelEnded(winner)`. (Survival can also end on timer with a final
  `SurvivalTimerTick(0)` immediately before `LevelEnded`.)
- **`LevelStarted`** is raised once at GO! by `GameManager` (or `LevelSelfStart`),
  AFTER the one-frame registration wait and timeScale unfreeze.

> **⚠️ Double-react / ordering risk on `LevelEnded`** (verified in code): three
> independent listeners all react to the **same** `LevelEnded` event, with **no shared
> ordering guarantee** (C# delegate invocation order = subscription order, which is
> nondeterministic across `RuntimeInitializeOnLoadMethod` bootstraps + a `MonoBehaviour`):
> 1. `GameManager.HandleLevelEnded` — token reward + leaderboard, **gated on
>    `player != null`** (skips bot-only runs).
> 2. `QuestSystem.OnLevelEnded` — `RoundsPlayed +1` always, `RoundsWon +1` if player won.
>    **No `player != null` guard** → a bot-only run still increments `RoundsPlayed`.
> 3. `SeasonPass.OnLevelEnded` — flat `AddXp(XpPerRound)` on **every** round, including
>    bot-only / Play-Again loops. **No guard at all.**
>
> Consequences to flag in cross-review:
> - **Not a token double-award** (only `GameManager` touches `TokenWallet`), but quests
>   and pass XP can advance on rounds where the human earned **zero tokens**, creating an
>   inconsistency between the economy ledger and progression counters.
> - `QuestSystem` independently recomputes `playerWon` from `RacerRegistry.Player` +
>   `winner`, identically to `GameManager` — duplicated win logic that can drift if one
>   changes. Any future second `TokenWallet.Add` from a downstream `LevelEnded` listener
>   WOULD double-pay (and `QuestSystem.TokensEarned` would then mis-credit via
>   `TokenWallet.Changed`).
> - If a manager fails to unsubscribe (see §5 scene-reload note), `LevelEnded` could fire
>   more than once per round, multiplying all three reactions.

---

## 7. Tuning Knobs

| Knob | Default | Where | Notes |
|---|---|---|---|
| `finishersToEnd` | 4 | `RaceManager` `[SerializeField]` | finishers needed to end a Race |
| `finishOnPlayerFinish` | false | `RaceManager` `[SerializeField]` | end Race when player finishes |
| `totalDuration` | 60s | `SurvivalManager` `[SerializeField]` | Survival timer |
| `arenaRadius` | 18f | `LastStandingManager` `[SerializeField]` | fallback rim radius (overridden by ArenaShrinker live edge) |
| `GracePeriod` | 15s | `ArenaShrinker` const | hold full size |
| `ShrinkDuration` | 60s | `ArenaShrinker` const | full→min contract time |
| `MinRadiusFraction` | 0.3 | `ArenaShrinker` const | final radius fraction |
| `WarnLookahead` | 2.5s | `ArenaShrinker` const | danger-band predictive lead |
| `NavRebuildInterval` | 1.5s | `ArenaShrinker` const | NavMesh rebuild cadence |
| `PulseSpeed` / band alphas | 4.2 / 0.18–0.62 / 0.42 | `SafeZoneRing` const | blink rate, blink range, ReducedMotion steady |
| `WidenFactor` / `SpawnSpreadFactor` | 1.6 / 1.25 | `ArenaResizer` const | platform / spawn-spread enlargement |
| `DefaultBotsPerLevel` | 7 | `GameConstants` | racers besides the player |
| `TokensForWin` | 100 | `GameConstants` | win payout (economy-owned constant) |
| Race / Survival unlock price | 100 / 150 | `LevelProgress.PriceOf` | **defined HERE**, spent via TokenWallet |
| Leaderboard score weights | RaceParTime 100000, RaceTimeWeight 100, SurvivalTimeWeight 100, LastStandTimeWeight 20, RankWeight 10000, WinBonus 50000 | `LevelResult` const | per-mode scoring |

---

## 8. Acceptance Criteria

1. Race ends and raises `LevelEnded(_finishers[0])` when any of: 4 finishers, all
   racers finished, or (player finished AND `finishOnPlayerFinish`). Each finish raises
   `RacerFinished` then `RacerRankChanged`.
2. Survival ends on `_timeRemaining<=0` OR `AliveCount<=1`; emits `SurvivalTimerTick`
   each whole second and a final `0` tick before ending; winner = first alive in registry.
3. LastStanding ends only when `AliveCount<=1`; on `LevelStarted` it stands up an
   `ObstacleSpawner`; `StopSpawning()` is called before `LevelEnded`.
4. Player falling calls `Eliminate()` in every mode; non-player falling respawns in Race
   (last checkpoint or `(0,2,0)`) and eliminates in Survival/LastStanding.
5. Knockout platform holds full radius for 15s, then SmoothStep-contracts to 30% over
   60s; NavMesh rebuilds every ≤1.5s while shrinking + one final bake at min; no
   invisible-radius kill occurs.
6. Danger band blinks at 4.2 rate within alpha 0.18–0.62, holds steady 0.42 under
   ReducedMotion, and hides when settled.
7. Knockout is always unlocked; Race costs 100 and Survival 150 tokens via
   `LevelProgress.TryUnlock`, persisted in PlayerPrefs.
8. Tokens/leaderboard are recorded only when a human player was in the run; win pays 100
   (×2 with a Token Doubler), non-win pays `max(10, 60-(rank-2)*8)`.
9. `Time.timeScale` returns to 1 at GO! and `_levelStartTime` excludes the countdown;
   a stuck intro is force-released within 6s.
10. **Each downstream `LevelEnded` listener behaves per §6:** economy/leaderboard gated on
    a player; `QuestSystem` advances RoundsPlayed always + RoundsWon on a player win;
    `SeasonPass` drips XP every round — and no listener double-awards tokens.
