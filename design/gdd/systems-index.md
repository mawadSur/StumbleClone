# Systems Index — StumbleKids

> Authoritative system list and the source of truth for the design-order layers
> and cross-system dependencies the six system GDDs are checked against. Layers
> run **Foundation → Core → Feature → Presentation → Polish** (design/author
> order). A system depends on everything in earlier layers it touches; deps below
> are the *direct, load-bearing* ones.
>
> Status legend: **Shipped** = in the playtest-confirmed single-player build ·
> **Dormant** = code-complete but switched off, needs external wiring ·
> **GDD-only** = documented here, not a separate authored GDD this pass.

## System table

| System | GDD file | Layer | Status | Depends On | Depended On By |
|--------|----------|-------|--------|------------|----------------|
| Engine / Input foundation | _(no GDD — engine-provided)_ | Foundation | Shipped | Unity 6, URP, New Input System, NavMesh | movement, bots |
| Core contract (`IRacer`, `GameEvents`, `RacerRegistry`, `GameConstants`) | _(no GDD — see `README.md` Architecture contract)_ | Foundation | Shipped | engine foundation | movement, bots, level-modes |
| Movement System | `movement-system.md` | Core | Shipped | engine input, `GameConstants` | level-modes, multiplayer |
| Bot AI System | `bot-ai-system.md` | Core | Shipped | NavMesh, level-modes | level-modes, multiplayer |
| Level Modes System | `level-modes-system.md` | Core | Shipped | movement, bots | token-economy, progression, multiplayer |
| Token Economy System | `token-economy-system.md` | Feature | Shipped | level-modes | progression (co-writes `TokenWallet`) |
| Progression System | `progression-system.md` | Feature | Shipped | level-modes, token-economy | cosmetics surfacing (Locker/Pass UI) |
| Multiplayer System | `multiplayer-system.md` | Feature | **Dormant** (code-complete) | movement, bots, level-modes | _(none yet — switched off)_ |
| Cosmetics / Locker surfacing | _(folded into progression GDD)_ | Presentation | Shipped | progression, token-economy | — |
| UI / HUD / Results | _(no separate GDD this pass)_ | Presentation | Shipped | level-modes, token-economy, progression | — |
| Audio / VFX / juice | _(backlog — see `docs/DESIGN_TASKS.md` D4)_ | Polish | Backlog | level-modes, presentation | — |

## Layer notes

**Foundation.** Not authored as standalone GDDs — they are engine-provided or the
shared architecture contract documented in `README.md` / `CLAUDE.md`. Everything
above sits on `IRacer` + `GameEvents` + `RacerRegistry` + `GameConstants`.

**Core (3 GDDs).** The playable game.
- `movement-system.md` — Rigidbody player control, knockback, jump, push. The
  input seam (`IPlayerInput`) is shared with multiplayer.
- `bot-ai-system.md` — NavMeshAgent-driven bots with per-mode behavior
  strategies; goes dynamic during knockback then re-snaps to NavMesh.
- `level-modes-system.md` — **owns** `GameEvents` and the win-conditions for
  Race / Survival / Last-Stand. The hinge the Feature layer earns from.

**Feature (3 GDDs).** Built on Core.
- `token-economy-system.md` — **owns** `TokenWallet` (earn on round outcomes,
  spend on skins/perks/unlocks).
- `progression-system.md` — battle pass + daily/weekly quests + daily-reward
  streak. **Co-writes `TokenWallet`** with the economy GDD — this shared
  ownership is the most important cross-GDD seam for the review to check
  (no double-credit, no conflicting wallet mutation rules).
- `multiplayer-system.md` — **Dormant**. Documents intended join-by-code session
  behavior; depends on Core but nothing depends on it because it is switched off.
  Its GDD should be clearly marked "code-complete, not live."

**Presentation.** Cosmetics surfacing (Locker / Season Pass UI) and the
per-mode HUDs / results / victory flows. These are shipped and production-shaped
but are not separately authored GDDs this pass — they are owned within the
progression and level-modes GDDs respectively.

**Polish.** Audio bed, VFX, and the win-moment juice pass are open backlog
(`docs/DESIGN_TASKS.md` D4) — there are zero audio files today (SFX procedural
only). Listed here for completeness; not in scope for this GDD pass.

## Dependency sanity checks (for the cross-review)

- **`TokenWallet` co-ownership:** economy and progression both write the wallet.
  Confirm both GDDs agree on who credits round rewards vs. quest/pass rewards and
  the order of operations.
- **`GameEvents` single owner:** level-modes owns the event definitions; movement,
  bots, economy, and progression are all *consumers*. Confirm no other GDD claims
  to *define* or mutate the event contract.
- **Multiplayer is a leaf:** nothing should declare a dependency *on* multiplayer
  while it is dormant. If a GDD does, that is a flag (it would couple the shipped
  product to a switched-off feature).
- **Layer ordering holds:** no Core GDD should depend on a Feature GDD (e.g.
  movement must not depend on token-economy). Bot AI's stated dependency on
  level-modes is intra-Core and expected (bots need mode context).

## Review status — 2026-06-04 cross-review

Full report: `design/gdd/gdd-cross-review-2026-06-04.md`. Overall verdict **CONCERNS**
(no ship-blocker). The Status column above tracks *build* state (Shipped/Dormant); the
column below tracks *GDD review* state from this pass.

| GDD | Review state | Open item (if any) |
|-----|-------------|--------------------|
| game-concept.md | Resolved (2026-06-04) — C-B1 event fixed | **Needs Revision** (owner): D-W1 declare meta-loop hierarchy |
| movement-system.md | Resolved (2026-06-04) — C-B2 dead constant repointed | — |
| bot-ai-system.md | Resolved (2026-06-04) — C-B2 repointed; S-B1 code clamp shipped | **Needs Revision** (owner): D-W5 Hard-mode base catch-up (design) |
| level-modes-system.md | Resolved (2026-06-04) — S-B1 recovery clamp shipped | — |
| token-economy-system.md | Resolved (2026-06-04) — D-W2 documented (§9) | **Needs Revision** (owner): D-W2 *implement* an ongoing sink |
| progression-system.md | Resolved (2026-06-04) — S-W1 baseline + S-W4 dedupe shipped | — |
| game-pillars.md | Resolved (2026-06-04) — A1/A5 guardrails + air-dash added | — |
| multiplayer-system.md | **Needs Revision** (owner) | S-W5 reclassify alive-count desync as a Phase-1 enablement blocker (replicate bots before MP on) |

"Resolved" = the doc/code defect was fixed this pass. "**Needs Revision**" = an open
owner decision (design/balance/scope) the cross-review surfaced; not auto-applied.
