# Cross-GDD Review Report

**Date:** 2026-06-04
**GDDs Reviewed:** 9 (6 system + 3 framing)
**Systems Covered:** movement, bot-AI, level-modes, token-economy, progression, multiplayer
**Method:** 3 parallel passes (consistency / design-holism / scenario walkthrough), each reading all 9 GDDs, spot-verified against `Assets/Scripts/`. Entity registry was an empty template — findings rely on full GDD reads + code checks.

> The GDDs were reverse-engineered from **working, playtest-confirmed shipped code**. "Blocking" therefore splits into **documentation defects** (the new GDD is wrong; the code is correct) and **real code findings** (the review surfaced an actual gameplay bug). Both are labelled.

---

## Consistency Issues (Phase 2)

### 🔴 Blocking — documentation defects (code is correct; fix the GDDs)
- **C‑B1 — `ShrinkRadiusChanged` doesn't exist.** `game-concept.md §3` lists it in `GameEvents`, but `Core/GameEvents.cs:17` defines `WaveTelegraphed` and no shrink event (shrink radius is the `ArenaShrinker.CurrentSafeRadius` property, not a broadcast). The same stale name appears in `CLAUDE.md` (Architecture Contract) and `ROADMAP.md`. → replace with `WaveTelegraphed` in all three.
- **C‑B2 — `TokensForFinish=25` is a dead constant cited as live.** `movement-system.md §6` and `bot-ai-system.md §6` describe the place reward as `TokensForFinish=25`; the constant is referenced nowhere but its own definition (`GameConstants.cs:48`). The live payout (`GameManager.cs:88‑90`) is `max(10, 60−(rank−2)×8)`. → repoint both GDDs; decide delete-vs-wire for the constant.

### ⚠️ Warnings
- **C‑W1** — economy §6 labels its level-modes dependency "inbound," inverting the usual arrow reading; reciprocity is actually present. Wording only.
- **C‑W2** — `TokenWallet` co-ownership phrasing differs ("owns" vs "no sole authority") across economy/progression/systems-index. Operationally identical; align wording.
- **C‑W3** — bot count 7 (offline) vs networked field 8 is consistent and reconciled in game-concept §5; optional clarity note.
- **C‑W4** — bot-only rounds advance quests/XP but pay no tokens (docs agree); this is a design decision (see S‑W2).

### ℹ️ Info (benign, code-confirmed)
`skin.premium_gold` not in `SkinCatalog` (intentional, on the inert premium track); `Casual_Male` priced-by-index but un-narrated; skin-price overflow (→200) vs unknown-id (→free) nuance in economy §5.

**Consistency verdict: CONCERNS** — 2 doc defects to fix; load-bearing seams coherent.

---

## Game-Design Issues (Phase 3)

### 🔴 Blocking: None
Nothing breaks the shipped game or actively violates a hard anti-pillar (the perks-vs-A1 and analytics-vs-A5 tensions are earn-only / dormant today — see warnings).

### ⚠️ Warnings
- **D‑W1 — Competing meta-loops.** Tokens + pass-XP + quests all pay off one round with no declared hierarchy → three reward bars per 2-min round for a "fast, low-stakes" game. Declare an intent hierarchy in `game-concept §2`.
- **D‑W2 — Economy runs out of sinks (top economy risk).** ~**2,190 tokens** clears every one-time purchase (skins 1,500 + perks 440 + mode unlocks 250); afterward only consumables remain against an infinite faucet (≈190 tokens/day quests + 870/week + win/consolation/daily). Currency trends toward meaningless within weeks → add an ongoing sink (rotating shop / token→pass-XP converter).
- **D‑W3 — A1 "not pay-to-win" is implicit.** Premium pass + IAP stub are inert only by wiring; premium grants cosmetics only — but no written rule says money may buy tiers/cosmetics ONLY, never perks/power-ups/tokens. Add the guardrail to `game-pillars A1`.
- **D‑W6 — A5 "no data" is implicit.** `Analytics.cs` already auto-instruments a full funnel; the HTTP sink is off only because the endpoint string is empty (PlayerPrefs-overridable). Write the disclosure rule into A5.
- **D‑W5 — Hard bots out-scale the player's base kit.** Bots ~9.06 vs player capped 6, with harder/more-frequent pushes + edge-herding; the only parity path is token-bought perks, which makes A1's line load-bearing. Consider a small innate catch-up.

### ℹ️ Info
Attention budget hits 5–6 in Knockout (on-theme chaos); the **air-dash is a 4th verb undocumented in pillar P4**. Player-fantasy coherent; every system serves ≥1 pillar; daily streak is A3-compliant (invite, not punishment).

**Design verdict: CONCERNS** — no blocker; economy sink + the two implicit guardrails are the pre-scale decisions.

---

## Cross-System Scenario Issues (Phase 4) — *the real code findings*

Scenarios walked: win-event fan-out · claim→Changed→quest loop · Last-Stand peak chaos · bot-only round end · dormant online round · fresh-install first round.

### 🔴 BLOCKER
- **S‑B1 — Last-Stand shrink × bot-recovery geometry race.** Bot stuck-rescue (`BotController`, `BotRecoveryTimeout=4s`) samples NavMesh at **radius 25** around the anchor, NOT clamped to the live `ArenaShrinker.CurrentSafeRadius` (as low as ~6m), and the floor rebuilds only every 1.5s → a rescued bot can warp onto stale/vanished mesh or the doomed rim. → clamp recovery radius to the live safe radius + force a rebuild before warping during an active shrink.

### ⚠️ Warnings (several are genuine bugs)
- **S‑W1 — Fresh-install quest under-credit (real bug).** `QuestSystem._lastBalance` captures its baseline on the *first* `Changed` event; on a clean install the first earn IS that event → first-round tokens credit 0 toward `d_earn150`. → initialize `_lastBalance` from persisted `TokenWallet.Balance` at bootstrap.
- **S‑W3 — Double-shield waste (real bug).** Guardian perk (200) and Bubble power-up (60) both call `GrantShield()` on `LevelStarted`; shield is a single boolean → the Bubble charge is consumed for nothing. → don't consume Bubble when a shield is already armed (refund), or stack.
- **S‑W4 — Duplicated `playerWon` logic.** `GameManager` (doubler) and `QuestSystem` (RoundsWon) each compute win independently — identical today, drift-prone. → extract one `RoundOutcome.PlayerWon()` in Core.
- **S‑W5 — Dormant-MP alive-count desync (reclassify).** Backfill bots are server-local (not `NetworkObject.Spawn`'d) but Last-Stand's win condition IS `AliveCount` → remote client's field disagrees with the server's. → reclassify from "cosmetic Phase-2 polish" to a Phase-1 enablement blocker for Last-Stand.

### ℹ️ Info
A banked weekly claim can auto-complete the "earn 150 today" daily (bounded, by design); `LevelEnded` order is actually **deterministic** (GameManager always last via RuntimeInitialize-vs-Awake) — the GDD's "nondeterministic" hedge is backwards; spawn-grace wastes Hard bot pushes for up to 3s.

**Scenario verdict: CONCERNS** — 1 real blocker + 3 real bugs + 1 reclassification.

---

## GDDs Flagged for Revision

| GDD | Reason | Type |
|-----|--------|------|
| game-concept.md | C‑B1 stale event; D‑W1 loop hierarchy | Consistency / Design |
| movement-system.md | C‑B2 dead `TokensForFinish` | Consistency |
| bot-ai-system.md | C‑B2 dead `TokensForFinish`; D‑W5 Hard speed gap | Consistency / Design |
| game-pillars.md | D‑W3/W6 implicit guardrails; air-dash vs P4 | Design |
| token-economy-system.md | D‑W2 sink plan; C‑W2 wording | Design / Consistency |
| progression-system.md | S‑W1 baseline; C‑W4 guard intent | Code / Consistency |
| level-modes-system.md | S‑B1 recovery-radius clamp | Code |
| multiplayer-system.md | S‑W5 reclassify alive-count | Code / Design |
| systems-index.md | C‑W2 wording | Consistency |

---

## Verdict: CONCERNS

No ship-blocker for the live single-player build. The review's payoff is concrete: 2 documentation defects, ~4 real code findings, and 3 design decisions.

### Actions taken (2026-06-04, owner-approved)
- ✅ Report written (this file) + flagged GDD statuses set to "Needs Revision" in `systems-index.md`.
- ✅ Code fixes applied: S‑B1 (recovery clamp), S‑W1 (quest baseline), S‑W3 (double-shield), S‑W4 (`RoundOutcome.PlayerWon` dedupe).
- ✅ Doc defects fixed: C‑B1 (`ShrinkRadiusChanged`→`WaveTelegraphed`), C‑B2 (`TokensForFinish` repoint).
- ✅ Guardrails written into `game-pillars.md` (A1 IAP / A5 data); economy sink plan added to `token-economy-system.md`.

### Deferred to owner (design decisions, not auto-applied)
- D‑W1 (declare loop hierarchy), D‑W2 implementation (build an ongoing sink), D‑W5 (Hard-mode base catch-up), S‑W5 implementation (replicate bots before MP enablement).
