# Token Economy System

**Slug:** `token-economy-system`
**Status:** Reverse-engineered from shipping code (2026-06-04). All values cite source paths under
`Assets/Scripts/`. This document describes only what the code does.

---

## 1. Overview

The Token Economy is StumbleKids' single soft-currency loop. The player earns **Tokens** by playing
rounds (win or place), by claiming a daily login reward, and (cross-system) by claiming progression
rewards. Tokens are spent in the title-screen shop on cosmetic skins, equippable perks, consumable
power-ups, level-mode unlocks, and a win-payout doubler. The currency itself is owned by
`TokenWallet` (`Assets/Scripts/Game/TokenWallet.cs`), a static, PlayerPrefs-backed balance that
raises `Changed` on every mutation so UI can refresh. There is no hard currency and no monetization
sink in code — Tokens are earned through play only.

The currency name is **Tokens** (the field/UI label is "tokens"; key `stumbleclone.tokens`).

---

## 2. Player Fantasy

"Every round pays out, so I'm always making progress." A win feels like a jackpot (full purse); a
near-miss still pays meaningfully, so finishing 2nd is clearly better than finishing last. Logging in
daily compounds via a streak, rewarding the "come back tomorrow" habit. The shop turns that earned
currency into self-expression (skins), small edges (perks/power-ups), and access to more content
(mode unlocks) — a steady earn-and-spend ladder rather than a paywall.

---

## 3. Detailed Rules

### 3.1 The Wallet (`TokenWallet.cs`)
- `Balance` is clamped to never go negative (`Mathf.Max(0, …)`), persisted in PlayerPrefs key
  `stumbleclone.tokens`, default 0.
- `Add(amount)` — no-op for `amount <= 0`; otherwise increments and saves.
- `CanAfford(cost)` — true when `Balance >= cost`.
- `TrySpend(cost)` — non-positive cost is free (returns true, spends nothing); otherwise spends only
  if affordable, else returns false and spends nothing.
- `SetBalance` saves PlayerPrefs and fires `Changed(newBalance)` on every change.

### 3.2 Earning — Round payout (`GameManager.HandleLevelEnded`, `GameManager.cs:72-98`)
- Paid only when the human player took part in the run (`player != null`).
- **Win:** player is the round winner → pays `GameConstants.TokensForWin`.
- **Loss/place:** pays a rank-scaled consolation (see Formulas §4), floored at 10.
- **Doubler:** on a win only, if the player owns a Token Doubler charge, one charge is consumed and
  the win payout is doubled (`reward *= 2`).
- The final payout is `TokenWallet.Add(reward)` and recorded on `lastResult` (`tokensAwarded`,
  `doublerUsed`) for the end screen.

### 3.3 Earning — Daily login reward (`DailyRewardStore.cs`)
- `RewardAvailable` is true once per UTC calendar day (compares stored `yyyyMMdd` stamp to today).
- `TryClaim(out streak)` grants the streak-scaled amount once per day; returns 0 if already claimed.
- Streak continues only if the last claim was literally yesterday; otherwise it resets to 1.
- Payout is added via `TokenWallet.Add(amount)`.

### 3.4 Sink — Skins (`SkinInventory.cs`, `SkinCatalog.cs`, `SkinStore.cs`)
- Skin id 0 (`BlueSoldier_Male`, "Blue Soldier") is the free default, always owned.
- `TryBuy(id)` — already-owned returns true free; else `TokenWallet.TrySpend(PriceOf(id))`, then
  marks `stumbleclone.skinowned.<id>=1` and fires `Changed`.
- `SkinStore` holds the equipped/selected skin id (`stumbleclone.skin`); buying ≠ equipping.

### 3.5 Sink — Equippable perks (`AbilityStore.cs`, applied by `AbilityApplier.cs`)
- Buy-once perks; one equipped at a time (`stumbleclone.perkequipped`, default "none").
- `BuyPerk(id)` spends via `TokenWallet.TrySpend(PerkPrice(...))`, marks owned, fires `Changed`.
- `AbilityApplier` subscribes to `GameEvents.LevelStarted` and applies the equipped perk's effect for
  the round via the player's buff APIs (effectively permanent for the round, duration `100000f`).

### 3.6 Sink — Consumable power-ups (`AbilityStore.cs`, applied by `AbilityApplier.cs`)
- Per-id charge counts in PlayerPrefs (`stumbleclone.powerup.<id>`), bought one charge at a time via
  `BuyPowerup(id)` → `TokenWallet.TrySpend(PowerupPrice(id))`.
- At round start, `AbilityApplier` consumes one charge of each owned power-up and stacks its burst
  effect on top of the equipped perk.

### 3.7 Sink — Mode unlocks (`LevelProgress.cs`)
- Knockout (LastStanding) is the free default mode. Race and Survival are token-gated.
- `TryUnlock(mode)` — already-unlocked returns true free; else `TokenWallet.TrySpend(PriceOf(mode))`,
  then sets `stumbleclone.modeunlocked.<int mode>=1`.

### 3.8 Sink — Token Doubler consumable (`AbilityStore.cs`)
- `BuyDoubler()` spends `DoublerPrice` and increments `stumbleclone.consumable.doubler`.
- `ConsumeDoubler()` spends one charge (called by `GameManager` on a win) to double the win payout.

---

## 4. Formulas

All token amounts are integers. Variable names match the source.

| Quantity | Formula | Value(s) | Source |
|---|---|---|---|
| **Win payout** | `TokensForWin` | **100** | `Core/GameConstants.cs:47` |
| **Loss/place payout** | `max(10, 60 − (playerRank − 2) × 8)` | rank 2 = **52**, rank 3 = 44, rank 4 = 36, …, floor **10** | `GameManager.cs:90` |
| **Doubler effect** | `reward × 2` on a win when a charge is consumed | win → **200** (×1 charge) | `GameManager.cs:91-92` |
| **Daily reward** | `BaseReward + PerDayBonus × (clamp(streak,1,MaxRewardDay) − 1)` | day1 **25**, +15/day, day7+ cap **115** | `DailyRewardStore.cs:15-17,36-37` |

Daily streak curve (`BaseReward=25`, `PerDayBonus=15`, `MaxRewardDay=7`):
day 1 = 25, day 2 = 40, day 3 = 55, day 4 = 70, day 5 = 85, day 6 = 100, day 7+ = 115 (capped).

Loss-payout note: `playerRank` is computed in `ComputePlayerRank` (`GameManager.cs:115-126`) as
`1 + (number of OTHER racers that finished before the player)`. Rank 1 is the win path (does not use
this formula). The expression is unclamped on the high side but floored at 10, so any rank ≥ 8 pays
the floor of 10.

---

## 5. Edge Cases

- **Two writers on one balance (shared ownership):** `TokenWallet` is written by BOTH this system
  (`GameManager` round payout, `DailyRewardStore`) AND the **progression-system**
  (`SeasonPass.Claim` → `TokenWallet.Add`, `QuestSystem.Claim` → `TokenWallet.Add`). See Dependencies
  §6 — this is the primary cross-review concern.
- **Quest feedback loop on `Changed`:** `QuestSystem` subscribes to `TokenWallet.Changed` and credits
  the `TokensEarned` quest metric with every positive balance delta (`QuestSystem.cs:159-167`). Any
  Token credit from anywhere (round win, daily, season-pass claim, even a quest's own token reward)
  advances that quest. Because `QuestSystem.Claim` itself calls `TokenWallet.Add`, claiming a
  token-rewarding quest re-enters `Changed` and feeds the `TokensEarned` quest — a self-referential
  loop the cross-review must confirm is bounded/intended (it cannot claim the same quest twice — guard
  at `QuestSystem.cs:125` — but it can advance a *different* still-open TokensEarned quest).
- **Negative/zero balance:** impossible to go below 0 — `SetBalance` and `Balance` both clamp.
- **Free spend:** `TrySpend(cost <= 0)` returns true and spends nothing — default skin/perk/Knockout
  (price 0) buy/unlock paths short-circuit to "owned" before reaching the wallet anyway.
- **Already-owned re-buy:** `SkinInventory.TryBuy`, `AbilityStore.BuyPerk`, `LevelProgress.TryUnlock`
  all return true without spending when already owned/unlocked (idempotent purchase).
- **Doubler only on win:** `doublerUsed = playerWon && AbilityStore.ConsumeDoubler()` — a charge is
  never consumed on a loss (short-circuit), so a placing run preserves the charge.
- **Doubler with 0 charges:** `ConsumeDoubler()` returns false; payout stays single.
- **Daily double-claim same day:** `TryClaim` returns 0 and adds nothing if `last == today`.
- **Daily streak gap:** any missed UTC day (last ≠ yesterday) resets streak to 1 (back to 25 tokens).
- **Daily timezone:** stamp uses `DateTime.UtcNow.Date` — rollover is at UTC midnight, not local.
- **Skin price overflow:** if `SkinCatalog` grows past the `Prices` array (8 entries), unlisted skins
  fall back to `DefaultPrice = 200` (`SkinInventory.cs:16-17,27`).
- **No-player runs:** if the human player isn't in the run, no round tokens are paid and no
  leaderboard entry is recorded (`GameManager.cs:86,102`).
- **PlayerPrefs is the only store:** all balances/ownership are local and unencrypted; clearing
  PlayerPrefs wipes the entire economy and trivially editable (no server authority in this system).

---

## 6. Dependencies

**This system OWNS:** `TokenWallet` (the currency) and all soft-currency prices/payout values listed
in §7.

**Depends on (inbound — who pays this system's currency):**
- **level-modes-system** — `GameManager.HandleLevelEnded` (subscribed to `GameEvents.LevelEnded`)
  computes and pays the round token reward, consuming the Doubler on a win. Round payout cannot fire
  without the level-modes-system raising `LevelEnded`. (`GameManager.cs:36,72-98`)

**Shared writer (CRITICAL — bidirectional with progression-system):**
- `TokenWallet` has **TWO independent writers**. Besides this system, the **progression-system** also
  *writes* the balance:
  - `SeasonPass.Claim` → `TokenWallet.Add(tokens)` (`SeasonPass.cs:142`)
  - `QuestSystem.Claim` → `TokenWallet.Add(q.TokenReward)` (`QuestSystem.cs:133`)
- The progression-system also *reads* this system's signal: `QuestSystem` subscribes to
  `TokenWallet.Changed` to drive its `TokensEarned` quest metric (`QuestSystem.cs:99,159-167`).
- **Implication for cross-review:** neither system has sole authority over the balance. A change to
  payout/credit logic in either system affects the other (potential double-credit via the
  `Changed` → `TokensEarned` → `Claim` → `Add` path; see §5). Ownership of the Tokens currency must be
  treated as shared between `token-economy-system` and `progression-system`.

**Consumed by (outbound — sinks read/spend this currency):**
- Skins (`SkinInventory` + `SkinCatalog`/`SkinStore`), perks & power-ups & Doubler (`AbilityStore`,
  applied via `AbilityApplier` on `GameEvents.LevelStarted`), mode unlocks (`LevelProgress`).
- UI: `TitleScreen.cs`, `LockerUI.cs` read `Balance` / subscribe to `Changed`.

**Engine/runtime:** UnityEngine `PlayerPrefs` (persistence), `GameEvents` bus (LevelStarted/Ended),
`RacerRegistry` (player + rank), `LeaderboardStore`/`LevelResult` (reporting only — not currency).

---

## 7. Tuning Knobs

Every price and reward, with its source. Changing these rebalances the whole loop.

### Rewards
| Knob | Value | Source |
|---|---|---|
| Win payout (`TokensForWin`) | 100 | `Core/GameConstants.cs:47` |
| Loss base | 60 | `GameManager.cs:90` |
| Loss per-rank decay | 8 | `GameManager.cs:90` |
| Loss floor | 10 | `GameManager.cs:90` |
| Daily `BaseReward` | 25 | `DailyRewardStore.cs:15` |
| Daily `PerDayBonus` | 15 | `DailyRewardStore.cs:16` |
| Daily `MaxRewardDay` (cap) | 7 | `DailyRewardStore.cs:17` |

### Sink prices
| Knob | Value | Source |
|---|---|---|
| Skin prices (by catalog index 0–7) | 0, 100, 100, 150, 200, 250, 300, 400 | `SkinInventory.cs:16` |
| Skin `DefaultPrice` (overflow) | 200 | `SkinInventory.cs:17` |
| Perk prices (`none, swift, spring, guardian`) | 0, 120, 120, 200 | `AbilityStore.cs:19` |
| Power-up prices (`rocket, bubble, megahop`) | 40, 60, 40 | `AbilityStore.cs:35` |
| Token Doubler price | 50 | `AbilityStore.cs:27` |
| Mode unlock — Race | 100 | `LevelProgress.cs:19` |
| Mode unlock — Survival | 150 | `LevelProgress.cs:21` |
| Mode unlock — Knockout (LastStanding) | 0 (free default) | `LevelProgress.cs:22` |

### Effect magnitudes (gameplay edges purchased with tokens — owned by movement/perk application)
| Knob | Value | Source |
|---|---|---|
| Perk Swift (Speed) | ×1.12 move speed, round-long | `AbilityStore.cs:18,32`; `AbilityApplier.cs:32` |
| Perk Spring (Jump) | ×1.30 jump, round-long | `AbilityApplier.cs:33` |
| Perk Guardian (Shield) | shield at round start | `AbilityApplier.cs:34` |
| Power-up Rocket Start | ×1.4 speed for 10s | `AbilityApplier.cs:42` |
| Power-up Bubble Shield | shield | `AbilityApplier.cs:44` |
| Power-up Mega Hops | ×1.5 jump for 10s | `AbilityApplier.cs:46` |
| Perk round duration constant | 100000f | `AbilityApplier.cs:13` |

---

## 8. Acceptance Criteria

1. **Win pays 100; doubled win pays 200.** A run the player wins adds exactly `TokensForWin` (100);
   with one Doubler charge owned, exactly 200 and one charge consumed. (`GameManager.cs:88-93`)
2. **Place payout matches the curve and floor.** 2nd→52, 3rd→44, 4th→36, … never below 10.
   (`GameManager.cs:90`)
3. **No payout when the player isn't in the run.** `player == null` → balance unchanged, no
   leaderboard entry. (`GameManager.cs:86,102`)
4. **Daily reward once per UTC day; streak scales then caps at 115.** Day1=25 … Day7+=115; second
   claim same day adds 0; a skipped day resets to 25. (`DailyRewardStore.cs`)
5. **Wallet never negative; `TrySpend` is all-or-nothing.** Spending more than `Balance` returns false
   and changes nothing. (`TokenWallet.cs:31-37`)
6. **`Changed` fires on every mutation** with the new balance (UI + the progression `TokensEarned`
   quest both observe it). (`TokenWallet.cs:44`)
7. **Each sink spends the exact listed price and is idempotent when already owned.** Skins, perks,
   power-ups, Doubler, and Race/Survival unlocks deduct precisely their §7 price; re-buying an owned
   item spends nothing and returns true.
8. **Shared-ownership integrity (cross-review gate):** `SeasonPass.Claim` and `QuestSystem.Claim` add
   tokens through the same wallet; the `TokensEarned` quest credits every positive `Changed` delta.
   Verify no unintended double-credit and that the claim→Add→Changed→advance path is bounded.
   (`SeasonPass.cs:142`, `QuestSystem.cs:133,159-167`)
9. **Effects apply on `LevelStarted`** for the equipped perk and one charge of each owned power-up,
   stacking; charges decrement by one per round. (`AbilityApplier.cs:25-47`)

---

## 9. Known Limitation / Next — ongoing token sink (cross-review D-W2)

**Risk (flagged 2026-06-04):** the economy is **source-rich but sink-poor in the long run.** One-time
sinks total ≈ **2,190 tokens** (skins 1,500 + perks 440 + mode unlocks 250; plus ~1,000 more in locker
emotes/poses), while sources are effectively infinite and steady — win 100, rank consolation 10–52
*every* round, daily 25–115, daily-quest sweep ≈190/day, weekly sweep ≈870/week, plus pass-tier token
rewards. A daily-active player clears the entire one-time catalogue within ~2 weeks, after which the
**only** remaining sinks are the consumable power-ups (40/60/40) and the Token Doubler (50). Past that
point Tokens are an infinite-source / near-zero-sink currency — the classic "currency becomes
meaningless" failure, which also hollows out the shop as a goal and leaves the pass as the only live
progression (compounding the competing-loop question — see `game-concept` core loop).

**Status:** acceptable for the shipped MVP (the first weeks play well). This is a **post-launch
retention item, not a ship-blocker.**

**Recommended fix (the #1 economy item for the first live-ops content drop) — pick one, owner decision:**
- **(a) Ongoing pillar-safe sink:** a rotating/seasonal cosmetic shop (re-buyable refreshes) or
  re-rollable cosmetics — *looks-and-access* spends only. The standard fix.
- **(b) Token → pass-XP converter:** spend surplus tokens to climb the battle pass (a permanent sink
  that also unifies the two meta-loops, addressing D-W1). Keep the rate bounded so it can't trivialise
  a season.
- **(c) Lower the faucet:** make quests pay pass-XP instead of tokens (overlaps D-W1), so the catalogue
  takes meaningfully longer to clear.

Whatever is chosen, it must **never** be a power sink (anti-pillar A1) — any new spend buys
looks / access / pass progress, never an arena advantage.
