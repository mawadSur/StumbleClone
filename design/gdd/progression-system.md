# Progression System — Season Pass + Quests

> Reverse-engineered from shipped code. Every value and behavior below is grounded in source;
> paths are relative to project root `/mnt/c/Users/Mohammed Awad/Desktop/games/unity-projects/StumbleClone/`.
> Slug: `progression-system`. Local-only (PlayerPrefs); premium track + IAP are deferred stubs.

## 1. Overview

The progression system is a meta-retention loop layered on top of the core match loop. It has two
coupled halves: a **30-tier seasonal Battle Pass** (`Assets/Scripts/Game/SeasonPass.cs`) that
players climb by accumulating pass XP, and a **daily/weekly Quest** treadmill
(`Assets/Scripts/Game/QuestSystem.cs`) that feeds the pass with XP and pays the token economy.
Pass XP comes from two sources: a flat per-round drip on every finished round, and the larger
bursts paid out when a completed quest is claimed. Each pass tier carries a FREE reward (always
claimable once reached) and a PREMIUM reward gated behind an IAP **stub** (`OwnsPremium`, always
`false` today). Rewards pay either tokens (via `TokenWallet`) or grant a cosmetic unlock id
(`skin.*` / `emote.*`) recorded in the `SeasonRewards` ledger. Cosmetic emotes/victory poses are
also separately purchasable for tokens in the locker (`Assets/Scripts/Visuals/EmoteSystem.cs`).
Everything persists in PlayerPrefs; both `SeasonPass` and `QuestSystem` are self-bootstrapping
static services with no scene wiring.

## 2. Player Fantasy

"Every match moves me forward." A player who logs in for a few rounds always sees their pass bar
tick up (the per-round drip guarantees motion even on a quest-less, loss-only session), while the
daily/weekly quests give goal-directed sessions ("win 1 round", "earn 150 tokens") that pay the
satisfying bigger jumps. The 30-tier track promises a long-arc reward ladder — free cosmetics and
tokens for everyone, with a marquee premium lane dangled "coming soon" (the UI literally shows
*"PREMIUM track locked (coming soon)"*, `SeasonPassUI.cs:155`). The loop is designed to feel like a
live-service party game without yet charging money — the premium lane and IAP are scaffolded but
inert.

## 3. Detailed Rules

### 3.1 Pass XP sources

- **Per-round drip:** `SeasonPass.Bootstrap` subscribes to `GameEvents.LevelEnded`; every finished
  round awards `XpPerRound` (20) pass XP via `OnLevelEnded → AddXp(XpPerRound)`
  (`SeasonPass.cs:193-200`). Awarded regardless of win/loss.
- **Quest claims:** `QuestSystem.Claim` calls `SeasonPass.AddXp(q.XpReward)` *before* paying tokens,
  so claiming a completed quest is the primary source of large XP jumps (`QuestSystem.cs:131-133`).
- `AddXp` is a no-op for non-positive amounts and clamps total XP to ≥ 0 (`SeasonPass.cs:123-130`).

### 3.2 Tiers and tier rewards (free vs premium)

- 30 tiers, index `0..29`, shown to players as `1..30` (`TierCount = 30`, `SeasonPass.cs:18,78`).
- `CurrentTier = clamp(TotalXp / XpPerTier, 0, 29)` (`SeasonPass.cs:78`).
- Each tier has a FREE reward and a PREMIUM reward. A reward is **either** tokens (`amount > 0`)
  **or** an unlock id (non-empty string), held in four parallel index-aligned arrays
  (`SeasonPass.cs:33-58`).
- **FREE track** is claimable on any reached, unclaimed tier (`CanClaim`, `SeasonPass.cs:111-118`).
- **PREMIUM track** additionally requires `OwnsPremium == true`; since the IAP is a stub that stays
  false, the premium lane is unreachable in the shipping build (`CanClaim` returns false when
  `premium && !OwnsPremium`, `SeasonPass.cs:116`).
- Claiming (`Claim`, `SeasonPass.cs:134-149`): marks the tier/track claimed in PlayerPrefs, pays
  `tokens` via `TokenWallet.Add` if `tokens > 0`, and grants `unlock` via `SeasonRewards.Grant` if
  non-empty. Returns `false` if not claimable. Claims are idempotent (per-tier per-track flag).

### 3.3 Daily quests (4) + weekly quests (4)

Catalog is static/hard-coded (`QuestSystem.cs:64-77`). A quest advances on a gameplay signal
(`QuestMetric`) and, when complete + claimed, pays **both** pass XP and tokens.

| Scope | Id | Description | Metric | Reset |
|---|---|---|---|---|
| Daily | `d_play3` | Play 3 rounds | `RoundsPlayed` | UTC day |
| Daily | `d_win1` | Win 1 round | `RoundsWon` | UTC day |
| Daily | `d_elim5` | Eliminate 5 racers | `RacersEliminated` | UTC day |
| Daily | `d_earn150` | Earn 150 tokens | `TokensEarned` | UTC day |
| Weekly | `w_play20` | Play 20 rounds | `RoundsPlayed` | ISO week |
| Weekly | `w_win5` | Win 5 rounds | `RoundsWon` | ISO week |
| Weekly | `w_elim40` | Eliminate 40 racers | `RacersEliminated` | ISO week |
| Weekly | `w_earn1000` | Earn 1000 tokens | `TokensEarned` | ISO week |

Metric → signal mapping (`QuestSystem.cs:141-167`):
- `RoundsPlayed` ← any `GameEvents.LevelEnded` (+1).
- `RoundsWon` ← `LevelEnded` where the winner is the human player (`ReferenceEquals(winner, RacerRegistry.Player)`).
- `RacersEliminated` ← `GameEvents.RacerEliminated` for any racer that is **not** the player
  (the human getting knocked out does not credit their own elimination quest; +1 per non-player elim).
- `TokensEarned` ← positive deltas on `TokenWallet.Changed` (see §5.3 baseline guard).

Reset cadence: dailies reset at UTC midnight (per calendar day stamp `yyyyMMdd`); weeklies reset per
ISO-8601 week (stamp `yyyyWww`, e.g. `202623`). Reset is implicit — a new period stamp simply reads
zeros from PlayerPrefs (`EnsureFresh` / `Hydrate`, `QuestSystem.cs:198-235`).

### 3.4 Cosmetic unlock ids

Two namespaces are granted by the pass into the `SeasonRewards` ledger (`SeasonPass.cs:39-58`):

- **`skin.*`** — `skin.Cowboy_Male`, `skin.Chef_Male`, `skin.Ninja_Male` (FREE track);
  `skin.Knight_Male`, `skin.Goblin_Male`, `skin.Elf`, `skin.BlueSoldier_Male`, `skin.premium_gold`
  (PREMIUM track). The `skin.` body (minus prefix) is expected to match a `SkinCatalog.Ids` entry
  (`Assets/Scripts/Game/SkinCatalog.cs:10-20`); `premium_gold` is intentionally **not** in the
  catalog and renders raw in the UI (`SeasonPassUI.PrettyUnlock`, `SeasonPassUI.cs:314-326`).
- **`emote.*`** — `emote.wave`, `emote.dance`, `emote.taunt` (FREE); `emote.flex`, `emote.spin`,
  `emote.victory`, `emote.backflip` (PREMIUM). Note: pass unlock ids are recorded in `SeasonRewards`
  (prefix `stumbleclone.passunlock.`), which is a **separate ledger** from `EmoteSystem`'s
  token-purchase ownership (prefix `stumbleclone.locker.owned.`). The pass-granted `emote.*` ids are
  not auto-wired into `EmoteSystem` ownership today; `SeasonRewards` is a standalone "is unlocked"
  query other systems can read (`SeasonPass.cs:206-223`).

Separately, the locker (`EmoteSystem.cs`) sells emotes/victory poses directly for tokens (index 0 of
each catalog is the always-owned free default; everything else is buy-then-equip via
`TokenWallet.TrySpend` in `LockerUI.OnRowClicked`, `LockerUI.cs:264-290`).

## 4. Formulas

All constants in `SeasonPass.cs:17-25` and the quest catalog in `QuestSystem.cs:64-77`.

```
XpPerTier   = 100                 // SeasonPass.cs:19
XpPerRound  = 20                  // SeasonPass.cs:20  (per finished round)
TierCount   = 30                  // SeasonPass.cs:18
Season      = 1                   // SeasonPass.cs:25  (bump => season wipe, §5.1)

CurrentTier   = clamp(TotalXp / XpPerTier, 0, TierCount-1)               // SeasonPass.cs:78
XpIntoTier    = (CurrentTier == TierCount-1) ? XpPerTier                  // SeasonPass.cs:81-82
                : TotalXp - CurrentTier * XpPerTier
TierProgress01= (CurrentTier == TierCount-1) ? 1 : clamp01(XpIntoTier/XpPerTier)  // SeasonPass.cs:85-86

Quest.Complete    = Progress >= Target                                    // QuestSystem.cs:31
Quest.Progress01  = (Target<=0) ? 1 : clamp01(Progress/Target)            // QuestSystem.cs:32
Advance(metric,a) => for each matching unclaimed/incomplete quest:
                     Progress = min(Target, Progress + a)                 // QuestSystem.cs:185 (cap)
```

Total XP to fully climb the track = `XpPerTier * (TierCount-1)` = 100 × 29 = **2900 XP** to reach
tier 30 (index 29). At drip-only pace that is 145 rounds; quests accelerate it.

### Quest targets + rewards (actual values, `QuestSystem.cs:64-77`)

| Id | Target | XpReward | TokenReward |
|---|---|---|---|
| `d_play3` | 3 | 30 | 40 |
| `d_win1` | 1 | 40 | 60 |
| `d_elim5` | 5 | 35 | 50 |
| `d_earn150` | 150 | 30 | 40 |
| `w_play20` | 20 | 120 | 200 |
| `w_win5` | 5 | 150 | 250 |
| `w_elim40` | 40 | 140 | 220 |
| `w_earn1000` | 1000 | 130 | 200 |

Full daily sweep = 135 XP + 190 tokens/day. Full weekly sweep = 540 XP + 870 tokens/week.

### Pass tier reward tables (index 0 = tier 1; `SeasonPass.cs:33-58`)

```
FreeTokens   : 25,0,30,25,40,0,35,30,45,50, 30,35,0,40,50,35,45,0,50,60, 40,45,50,0,55,50,60,55,0,100
FreeUnlocks  : tier2 emote.wave, tier6 skin.Cowboy_Male, tier13 emote.dance,
               tier18 skin.Chef_Male, tier24 emote.taunt, tier29 skin.Ninja_Male
PremiumTokens: 50,60,0,75,60,80,0,70,90,100, 60,0,80,90,100,0,85,110,90,0, 100,120,0,110,130,0,120,140,0,250
PremiumUnlocks: tier3 skin.Knight_Male, tier7 emote.flex, tier10 skin.Goblin_Male,
               tier12 emote.spin, tier16 skin.Elf, tier20 emote.victory,
               tier23 skin.BlueSoldier_Male, tier26 emote.backflip, tier29 skin.premium_gold
```

### Emote/locker prices (`EmoteSystem.cs:58-74`)

Emotes: Wave 0 (free), Dance 120, Cheer 120, Bow 150, Backflip 220, Taunt 180.
Victory poses: Spin Out 0 (free), Big Flex 200, Float On 260.

## 5. Edge Cases

### 5.1 Season rollover wipe
`EnsureSeason` (`SeasonPass.cs:167-184`) runs once per process (guarded by `_bootstrapped`). It
compares the stored season number against the `Season` constant; on mismatch it zeroes XP, resets
the premium flag, and deletes every per-tier free/premium claim flag, then stamps the new season.
Bumping the `Season` constant in a build therefore resets every local player for the new pass.
`SeasonRewards` cosmetic unlocks (prefix `stumbleclone.passunlock.`) are **not** wiped — once a skin/
emote is granted it stays owned across seasons.

### 5.2 Period stamp hydration
`QuestSystem` never bulk-deletes stale quest keys. `EnsureFresh` recomputes the current day/week
stamp each access; when the stamp changes, `Hydrate` rebuilds the live quest list from the static
catalog and reads `Progress`/`Claimed` from PlayerPrefs **under the current stamp** — so a new period
naturally reads zeros and old keys simply go unread (`QuestSystem.cs:198-235`). Catalog entries are
cloned per period so the static template is never mutated. Both `SeasonPass.Bootstrap` and
`QuestSystem.Bootstrap` defensively unsubscribe before subscribing to survive Unity domain-reload
double-subscribe (`SeasonPass.cs:196-197`, `QuestSystem.cs:93-99`).

### 5.3 Baseline-capture guard on `TokensEarned`
`OnTokensChanged(int newBalance)` receives the *new balance*, not a delta. On the first event after
bootstrap (`_lastBalance == int.MinValue`) it captures the balance as a baseline and returns without
advancing — otherwise the player's entire existing wallet would falsely count as "earned this period"
(`QuestSystem.cs:160-167`). Thereafter it advances by the positive delta only (negative deltas from
spending are ignored).

### 5.4 Other guards
- Max tier: `XpIntoTier`/`TierProgress01` pin to full at tier index 29 so the bar reads 100% rather
  than overflowing (`SeasonPass.cs:81-86`).
- `Claim` re-checks `CanClaim` first, so double-claims and not-yet-reached/locked claims are rejected
  (`SeasonPass.cs:136`); `QuestSystem.Claim` rejects unknown/incomplete/already-claimed quests
  (`QuestSystem.cs:124-125`).
- Reward queries out of range return `0` / `""` rather than throwing (`InRange`, `SeasonPass.cs:96-99,186`).

## 6. Dependencies

Bidirectional. Slugs: `movement-system` / `bot-ai-system` / `level-modes-system` /
`token-economy-system` / `progression-system` / `multiplayer-system`.

### Depends on → `level-modes-system`
Subscribes to `GameEvents.LevelEnded` (XP drip + `RoundsPlayed`/`RoundsWon` quest metrics) and
`GameEvents.RacerEliminated` (elimination quests). `RoundsWon` uses `RacerRegistry.Player` identity to
detect a human win (`GameEvents.cs:11-17`, `QuestSystem.cs:141-157`, `SeasonPass.cs:200`). The level
modes system must continue to raise these events with the correct winner reference, or
win/play/elim progress stalls.

### Depends on → `token-economy-system` (and is a SECOND WRITER of its resource — KEY CROSS-REVIEW ITEM)
`TokenWallet` is **owned by** `token-economy-system` (`Assets/Scripts/Game/TokenWallet.cs`; the
file's own doc says tokens are earned per round in `GameManager.HandleLevelEnded` and spent in the
title-screen shop). The progression system is a **SECOND WRITER** of that wallet:

- `SeasonPass.Claim` calls `TokenWallet.Add(tokens)` on a tier claim (`SeasonPass.cs:142`).
- `QuestSystem.Claim` calls `TokenWallet.Add(q.TokenReward)` on a quest claim (`QuestSystem.cs:133`).

**Competing progression loop / shared-resource feedback (must be reviewed):** `TokenWallet.Add` fires
`TokenWallet.Changed` (`TokenWallet.cs:44`). The progression system *also reads* that same event —
`QuestSystem.OnTokensChanged` is subscribed to `TokenWallet.Changed` to drive the `TokensEarned`
quests (`d_earn150`, `w_earn1000`). So a quest-claim or pass-claim token payout **re-fires
`TokenWallet.Changed`, which feeds the `TokensEarned` quest** — a closed feedback loop on a shared
resource where one progression payout can advance another progression objective.

The code's **anti-recursion / termination guards** (for the cross-review to judge):
1. `OnTokensChanged` only advances on a **positive** delta and is guarded by the baseline capture
   (§5.3), so it cannot advance on the first event or on spends (`QuestSystem.cs:160-167`).
2. `Advance → AdvanceList` **skips any quest that is already `Complete` or `Claimed`** and clamps
   progress with `Mathf.Min(q.Target, q.Progress + amount)` (`QuestSystem.cs:184-185`). A completed
   `TokensEarned` quest therefore stops accumulating, and a claimed one is never re-advanced.
3. The recursion does **not** re-enter `Claim`: advancing a quest's `Progress` does not auto-claim
   it (claim is user-initiated in the UI). So the chain is `Claim → TokenWallet.Add →
   TokenWallet.Changed → OnTokensChanged → Advance (Progress only, capped, no re-claim)` and
   terminates in one hop. There is no `SeasonPass`-side listener on `TokenWallet.Changed`, so pass
   token payouts cannot tier-up the pass directly.

Reviewer note: the loop is bounded but is a genuine "two progression systems writing/reading one
shared wallet" coupling — auditing tier/quest token payout volume vs. the `TokensEarned` targets is
worthwhile (e.g. claiming `w_win5` pays 250 tokens, which alone advances `d_earn150` past its 150
target).

### Cross-reference → cosmetics (`EmoteSystem` / skins)
Pass unlocks grant `skin.*` / `emote.*` ids into `SeasonRewards` (`SeasonPass.cs:145,206-223`).
`EmoteSystem` (`Assets/Scripts/Visuals/EmoteSystem.cs`) is the procedural emote/victory-pose catalog
+ playback used by the locker; it maintains its own token-purchase ownership ledger separate from
`SeasonRewards` (see §3.4 note). `SkinCatalog` (`SkinCatalog.cs`) defines the canonical skin ids the
`skin.*` rewards reference.

### Relationship → `multiplayer-system`
None — the progression system reads only local `GameEvents` and PlayerPrefs; no networking, no
authority/replication. All reward state is client-local.

### Relationship → `movement-system` / `bot-ai-system`
None direct — progression observes only the outcome events (`LevelEnded`, `RacerEliminated`) those
systems ultimately produce via the level layer; it does not read movement or AI state.

## 7. Tuning Knobs

| Knob | Value | Location | Notes |
|---|---|---|---|
| `XpPerTier` | 100 | `SeasonPass.cs:19` | Flat XP cost per tier |
| `XpPerRound` | 20 | `SeasonPass.cs:20` | Per-finished-round drip |
| `TierCount` | 30 | `SeasonPass.cs:18` | Tier-table length must match |
| `Season` | 1 | `SeasonPass.cs:25` | Bump = season wipe (§5.1) |
| `FreeTokens[]` / `FreeUnlocks[]` | 30-len arrays | `SeasonPass.cs:33-44` | Free reward table |
| `PremiumTokens[]` / `PremiumUnlocks[]` | 30-len arrays | `SeasonPass.cs:47-58` | Premium reward table |
| Daily quest defs (target/xp/tokens) | see §4 | `QuestSystem.cs:64-70` | 4 dailies |
| Weekly quest defs (target/xp/tokens) | see §4 | `QuestSystem.cs:71-77` | 4 weeklies |
| Daily reset | UTC midnight | `QuestSystem.cs:113-117,247-251` | `yyyyMMdd` stamp |
| Weekly reset | ISO week (Mon) | `QuestSystem.cs:256-274` | `yyyyWww` stamp |
| Emote/pose prices | see §4 | `EmoteSystem.cs:58-74` | Locker token prices |
| `OwnsPremium` (IAP stub) | always false | `SeasonPass.cs:88-90,154-160` | `GrantPremiumStub` flips it; shipping build never calls it |

All tables are deliberately data-shaped (static arrays) so a future remote-config pass can replace
them without touching callers (`SeasonPass.cs:13-14`, `QuestSystem.cs:50-51`).

## 8. Acceptance Criteria

1. **Drip:** Finishing any round (win or lose) increases `SeasonPass.TotalXp` by exactly 20.
2. **Tiering:** At 100 XP `CurrentTier` is 1 (index 1, shown "Tier 2"); at 2900 XP it pins at index
   29 ("Tier 30") and `TierProgress01 == 1`.
3. **Free claim:** A reached, unclaimed FREE tier is claimable; claiming pays its token amount (or
   grants its unlock) and flips it to claimed; re-claim returns false.
4. **Premium gate:** With `OwnsPremium == false`, no PREMIUM tier is claimable; the pass UI shows
   "PREMIUM track locked (coming soon)". Calling `GrantPremiumStub(true)` makes reached premium
   tiers claimable (debug-only path).
5. **Quest advance:** Playing 3 rounds completes `d_play3`; a human win completes `d_win1`; 5
   non-player eliminations complete `d_elim5`; earning 150 tokens completes `d_earn150`.
6. **Quest claim payout:** Claiming a complete quest adds its `XpReward` to the pass and its
   `TokenReward` to the wallet, exactly once.
7. **Baseline guard:** Starting a fresh session with a non-zero wallet does **not** advance any
   `TokensEarned` quest until tokens are actually earned after bootstrap.
8. **Shared-wallet loop terminates:** Claiming a token-paying quest/tier advances `TokensEarned`
   quests by the payout delta but never re-enters `Claim`, never advances a `Complete`/`Claimed`
   quest, and is capped at the quest target — no unbounded recursion.
9. **Daily reset:** Crossing UTC midnight resets all daily progress/claimed to zero; weekly progress
   persists until the ISO week changes.
10. **Season wipe:** Incrementing the `Season` constant zeroes XP, the premium flag, and all tier
    claim flags on next launch, while `SeasonRewards` cosmetic unlocks survive.
11. **Local-only:** All state lives in PlayerPrefs; no network/IAP calls are made.
```

