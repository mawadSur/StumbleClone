# Game Concept — StumbleKids

> Top-level framing doc. The six system GDDs in `design/gdd/` are each checked
> against this and against `game-pillars.md`. Grounded in `README.md`,
> `PROGRESS.md`, `CLAUDE.md`, and `docs/DESIGN_TASKS.md` — describes what the
> game actually is, not aspirational scope.

## 1. Vision

StumbleKids is a 3D physics-knockout party game in the Stumble Guys / Fall Guys
mold. You control a wobbly, bumpable character racing and brawling through short,
chaotic obstacle courses against a field of opponents. The fun is in the
slapstick: getting knocked off a spinning bar, shoving a rival into a pit on the
final platform, scrambling back from a stumble to steal a finish. Rounds are
fast, failure is funny rather than punishing, and you come back to chip away at
cosmetic progression between matches.

The shipped, playable experience is **single-player vs. 7 bots**. Online
multiplayer exists in code (join-by-code sessions) but is **dormant** — see
Section 6.

## 2. Core Loop

```
PLAY a round (Race / Survival / Last-Stand)
        │
        ▼
EARN  tokens + battle-pass XP + quest progress
        │
        ▼
SPEND tokens on skins / perks / mode-unlocks,
      climb the battle pass, claim cosmetics
        │
        ▼
RETURN for the daily reward + refreshed quests
        │
        └──────────────► back to PLAY
```

One-liner: **Play a chaotic knockout round → earn tokens, pass XP, and quest
progress → spend on skins, perks, and mode-unlocks and climb the battle pass →
return for the daily reward and new quests.**

This loop is implemented in code today: `TokenWallet` (earn/spend), `SeasonPass`
(battle-pass XP + tiers), `QuestSystem` (daily/weekly quests), `DailyRewardStore`
(streak reward), `AbilityStore` (perks), `SkinInventory` / `LockerUI`
(cosmetics), and `LevelProgress` (mode unlocks).

## 3. Three Level Modes (the moment-to-moment content)

| Mode | Scene | Win condition | Feel |
|------|-------|---------------|------|
| **Race** | `Level_Race` | First to cross the finish line | Forward momentum, dodge obstacles, beat the pack |
| **Survival** | `Level_Survival` | Outlast a timer / hazard waves | Endure, don't fall, last longer than the field |
| **Last-Stand** | `Level_LastStanding` | Be the last racer not eliminated | Shrinking arena, shove rivals off, king-of-the-hill tension |

All three share one player, one bot roster, one obstacle kit, and one set of
win-condition events (`GameEvents`: `LevelStarted`, `LevelEnded`,
`RacerFinished`, `RacerEliminated`, `RacerRankChanged`, `SurvivalTimerTick`,
`WaveTelegraphed`). (Shrink radius is exposed as the `ArenaShrinker.CurrentSafeRadius`
property, not a broadcast event.)

## 4. MVP Definition

The MVP is **shipped and playtest-confirmed** (single-player). It is:

- One player + **7 bots** in every mode.
- The **three modes** above, each with a NavMesh-baked level built from primitive
  geometry plus the obstacle kit (spinning bar, swinging hammer, moving platform,
  push pad, rising platform, shrinking zone).
- Rigidbody-driven player movement (physics party-game feel, not a
  CharacterController) with knockback, jump, and push.
- NavMesh bot AI with per-mode behavior strategies.
- The full progression loop in Section 2 (tokens, battle pass, quests, daily
  reward, perks, cosmetics, mode unlocks).
- Title / level-select / per-mode HUD / results / victory UI.

Explicitly **out of MVP scope** (per `PROGRESS.md`): mobile touch controls as a
first-class input, online multiplayer as a live mode, and a unified hub /
bottom-nav IA (tracked as design backlog `D7` in `docs/DESIGN_TASKS.md`).

## 5. Target Platforms & Player Count

- **WebGL on Vercel** — the primary live distribution today (browser-playable,
  served from `web/`). Keyboard/Mouse + Gamepad.
- **Android** — targeted store platform; currently a debug-signed sideload APK
  only. A signed release + Play closed-test track is open store work (`D14`).
  Touch input is **not yet** a first-class control scheme (`technical-preferences`
  lists touch as out of scope; mobile-readiness is design backlog).
- **PC standalone (Windows)** — viable build target, used during development.

**Player count:** 8 racers per match = **1 human + 7 bots** (single-player). The
dormant multiplayer path supports human-vs-human join-by-code sessions at the
same 8-racer table.

## 6. Multiplayer Status (important framing)

Online multiplayer is **code-complete but dormant**. The `Net/` scripts
(`BackendService`, `Analytics`) and the join-by-code session layer exist and
compile, but the live experience ships **single-player vs. bots**. Multiplayer
requires external wiring (Unity Cloud services linked, Relay/Lobby/Auth enabled,
a 2-client test) before it can be turned on. Treat single-player as the product;
treat multiplayer as a switched-off feature whose GDD documents intended behavior,
not shipped behavior.

## 7. Constraints & Assumptions

- **Engine:** Unity 6 (project `6000.4.8f1`; reference line 6.3 LTS) + URP, New
  Input System, Unity AI Navigation (NavMesh). No legacy `Input.*`, no
  `Rigidbody.velocity` (use `linearVelocity`).
- **Art:** placeholder-grade — primitive geometry, shared character rig, night-sky
  atmosphere (a deliberate signature, not a bug; `D1` proposes daytime but is an
  owner-decision-only "consider"). Cosmetic skins swap the whole model on a shared
  armature so all characters animate identically.
- **Backend:** local-only state by design (no account, no ads/analytics SDKs
  active) — an honest "no data collected" privacy posture. UGS backend scaffolding
  is dormant.
- **Audience:** broad, casual, all-ages "candy party" players; sessions are
  short and drop-in.
