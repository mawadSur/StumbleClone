# Design Pillars & Anti-Pillars — StumbleKids

> The cross-review (`/review-all-gdds`) checks every system GDD against these.
> Pillars are **load-bearing**: a feature that fights a pillar, or quietly
> violates an anti-pillar, is flagged. Each is written to be concrete and
> testable — "does this design serve / violate it, and how would we tell?"
>
> Inferred honestly from what the game **is** (see `game-concept.md`): a
> chaotic physics-knockout party game with short rounds, cosmetic-only
> progression, and a deliberately approachable, all-ages tone.

---

## Pillars (what the game IS)

### P1 — Chaotic Physics Comedy
The core verb is *getting bumped, shoved, and stumbling*, and that being
**funny**, not frustrating. Rigidbody movement, knockback, the push interaction,
and the obstacle kit (spinning bars, swinging hammers, shrinking arenas) exist to
manufacture slapstick chaos. Wobble is a feature.

- **Serves it:** physics-driven knockback, shove-a-rival-off mechanics, obstacles
  that create reversals, ragdoll/squash feedback, "funny failure" framing.
- **Test:** Does the system add or preserve emergent, comedic, physics-driven
  moments? Does a loss feel like a laugh and a re-try, not a punishment?
- **Violates it:** deterministic/scripted outcomes, precision-platformer
  tolerances, anything that makes a stumble feel like the game's fault rather than
  the chaos's.

### P2 — Fast, Low-Stakes Rounds
Matches are **short and drop-in**. You can play one round in a couple of minutes,
losing costs you nothing real, and a new round is always one tap away. Race,
Survival, and Last-Stand all resolve quickly via clear win conditions.

- **Serves it:** quick round length, instant restart, clear single win condition
  per mode, no grind-gates blocking the next round.
- **Test:** Can a player start, play, and finish a round in a few minutes and
  immediately start another? Does any system insert a wait, a long tutorial, or a
  mandatory sit-through between rounds?
- **Violates it:** long mandatory sessions, energy/stamina timers that block play,
  multi-stage matches you can't bail out of.

### P3 — Cosmetic Progression, Earn-by-Playing
Progression is **expression and goals, never power**. Tokens, battle-pass XP,
quests, daily streak, and mode-unlocks give you reasons to come back, and the
rewards are skins, emotes, victory poses, and access — not advantages in the
arena. Perks (`AbilityStore`) are a deliberate, bounded exception kept small and
match-fair, not stat-stacking.

- **Serves it:** earnable cosmetics, battle-pass tiers, quests that reward play,
  daily reason-to-return, mode-unlock goals — all tradable for *looks and access*.
- **Test:** Does the reward change how you *look* or *what you can play*, not how
  hard you *hit*? If a system grants in-match advantage, is it small, bounded, and
  earnable by playing (never by paying)?
- **Violates it:** any reward that makes a player meaningfully stronger than an
  equally-skilled opponent, especially one gated behind money.

### P4 — Approachable for Everyone
A new player should understand the goal in seconds and have fun in their first
round. Controls are simple (move / jump / air-dash / push), the tone is bright candy-party
all-ages, and the genre's intent is immediate readability. No manual, no jargon,
no skill wall.

- **Serves it:** one obvious goal per mode, minimal control surface, forgiving
  FTUE, readable feedback (crown pop, win sting, clear HUD).
- **Test:** Could a first-time player, with no instruction, do roughly the right
  thing within the first round? Is the goal visible on screen at all times?
- **Violates it:** hidden mechanics, dense UI, mode rules that need a tutorial to
  parse, mature/edgy tone that narrows the all-ages audience.

---

## Anti-Pillars (what the game deliberately is NOT)

### A1 — NOT Pay-to-Win
No purchasable in-match advantage, ever. Money may (future) buy cosmetics or pass
tiers; it must never buy a stronger character, a better perk, or a competitive
edge. This is the hard line that protects P3.

- **Flag if:** any GDD proposes a paid item, perk, or boost that affects arena
  outcomes; any "premium" stat; any soft-currency-for-power conversion.
- **Guardrail (explicit — 2026-06-04 cross-review D-W3):** if IAP is ever enabled, it may purchase
  **only** pass tiers and cosmetic items (`skin.*` / `emote.*` / victory poses). It must **never**
  sell perks, power-ups, the Token Doubler, or **tokens themselves** — tokens buy perks and power-ups,
  so selling tokens is selling power. The premium battle-pass track must grant cosmetics only, never a
  stat or perk. Today this line holds only because `SeasonPass.OwnsPremium` is `false` and
  `GrantPremiumStub` is never called; when IAP work begins, enforce it in code (assert every premium
  grant resolves to a `skin.*`/`emote.*` id, and never wire a real-money → token product).

### A2 — NOT a Realistic / Serious Simulation
This is cartoon slapstick, not a physics sim or a competitive esport. Imperfect,
exaggerated, comedic physics is correct. We do not chase realism, precise
control, or simulation fidelity.

- **Flag if:** a GDD adds realistic damage, fatigue, simulationist tuning,
  grimdark tone, or precision demands that fight P1/P4.

### A3 — NOT a Long-Session Grind
No mechanics that demand or reward marathon sessions. No energy systems that
block play, no daily-login *obligation* dressed as a feature, no progression that
*requires* hours per day. The daily reward invites a return; it must not punish
absence.

- **Flag if:** a system gates core play behind time/energy, escalates required
  session length, or makes missing a day costly rather than merely a missed bonus.

### A4 — NOT a Mechanically Deep / Hardcore Game
Depth comes from *chaos and other players*, not from layered systems, combos,
skill trees, or rules a newcomer must study. We add breadth (more modes, more
cosmetics) before we add mechanical complexity.

- **Flag if:** a GDD introduces combo systems, deep skill trees, stat builds, or
  rule complexity that raises the floor against P4.

### A5 — NOT Data-Hungry / Account-Gated (current posture)
The shipped product is local-only with no account requirement, no ads SDK, and no
analytics SDK active — an honest "no data collected" posture and a competitive
advantage. New systems should not silently require accounts or collect data
without an explicit owner decision (turning on the dormant cloud/multiplayer path
*is* such a decision and changes this posture deliberately).

- **Flag if:** a GDD assumes mandatory accounts, an always-online requirement, or
  background data collection as a baseline rather than an opt-in feature.
- **Guardrail (explicit — 2026-06-04 cross-review D-W6):** `Analytics.cs` already auto-instruments a
  full behavioral funnel (session / level / elimination / currency events); the HTTP sink is inert
  **only** because the endpoint string is empty (and it is PlayerPrefs-overridable). Enabling any
  analytics endpoint, Cloud Save, or the multiplayer/backend path is an owner decision that **changes
  the privacy posture** and must be disclosed to players (updated store privacy label + in-app notice).
  A shipped build must not silently enable collection via a stray config / PlayerPrefs key.

---

## How the cross-review uses this

For each system GDD, the reviewer asks:
1. Which pillar(s) does this system **serve**, and is the link real (testable)?
2. Does any rule **violate** an anti-pillar (especially A1 pay-to-win, A3 grind)?
3. If a system is in tension (e.g. perks vs. P3/A1), is the tension **bounded and
   acknowledged**, or unmanaged?
