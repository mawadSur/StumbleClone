# StumbleKids — Design Task Backlog

> Source: competitive design study (`/plan-design-review`, 2026-06-04) — three parallel
> research agents studied the art style, screens/UX, and store setup of top casual/party
> mobile games (Stumble Guys, Fall Guys, Brawl Stars, Subway Surfers, Among Us) and
> compared them to StumbleKids' current code. Saved per request; **not yet implemented.**

## Headline

> **StumbleKids has product-grade plumbing but ships the wrong genre signals — flip
> night→day, give it a face, build the hub, and get on the stores.**

The study's verdict: the codebase is more complete than a typical prototype (real design
tokens, almost every screen *type* the genre expects, an unusually clean privacy posture),
but three things make it read as a moody prototype instead of a candy-party product:
the dark look, the lack of a mascot face/branding, and the missing navigation hub — plus
it is currently **invisible to both app stores** (WebGL on Vercel + a debug sideload APK only).

### ⚠️ Night-sky tension (owner decision required)

The study's **#1 recommendation (D1)** is to flip the global night starfield to a bright
daytime sky, arguing the genre is "universally bright/daytime." **This directly contradicts
the deliberate art direction you chose** (black sky + stars in `SceneAtmosphere.cs`). It is
recorded here as the researchers' finding **for your decision** — it has **not** been acted
on, and the night sky remains the shipped look. Treat D1 as "consider," not "do." Options if
you want to honor both: keep night as the StumbleKids signature, OR make the sky a per-level
theme (bright daytime default + a night biome), which D18 already proposes.

## Top 5 priorities (study's ranking)

| # | Task | Area | Effort | Note |
|---|------|------|--------|------|
| D1 | Flip global night sky → bright daytime (demote night to per-level/seasonal) | Art/UI | M | **⚠️ conflicts with your chosen art direction — your call** |
| D2 | Replace the "SK" monogram app icon with a chibi mascot face | Art/UI | M | Per-store export (Play 512 w/ alpha, Apple 1024 flat) |
| D7 | Collapse the title button-soup into a HOME HUB + 5-tab bottom nav | Screens/UX | XL | PLAY/LOCKER/SHOP/PASS/SOCIAL, 3D char center-stage |
| D11 | Produce the captioned, device-framed store screenshot set | Store/Deploy | L | 6–8 portrait shots; first 3 sell it |
| D14 | Build a signed release + open the Play closed-test track **now** | Store/Deploy | L | Hard ~2-week blocker: 12 testers / 14 days |

---

## Full backlog (D1–D20)

Effort: S = hours · M = ~1 day · L = a few days · XL = multi-day. Priority: P0 = do first.

### Art / UI

| ID | Title | Pri | Effort | Why (from study) |
|----|-------|-----|--------|------------------|
| D1 | Flip global night sky → bright high-key daytime | P0 ⚠️ | M | `SceneAtmosphere.cs` hard-codes a near-black starfield (zenith 0.015,0.020,0.045) + moonlit ambient for **all** levels; genre is universally bright daytime, and the darkness forces an in-shader saturation hack. **Conflicts with the night look you explicitly requested — owner decision.** |
| D2 | Replace SK-monogram icon with a chibi mascot face | P0 | M | `AppIcon.png` is an abstract "SK" letterform with no face; every top party-game icon is a screaming mascot face. Install-rate liability + prototype tell. |
| D3 | Chibi/blobby character proportions + wire female variants | P1 | L | 8 Quaternius models are realistic-human proportions on a shared rig → generic silhouettes; `SkinCatalog.cs` only wires the **male** variants despite female FBX on disk. Exaggerate head-up/body-down, widen per-skin contrast. |
| D4 | Music bed + real win-moment juice pass | P1 | L | **Zero audio files** in the project (SFX procedural only); win moment is modest. Add looping menu/match music, full-screen confetti, screen shake, crown pop + win sting on `VictoryScreen`, character squash-and-stretch. |
| D5 | Deepen claymorphism on buttons + lighten/warm surfaces | P1 | M | `UITheme` `RoundedSprite` buttons are flat-tinted rects (no soft shadow / top-highlight gradient); deep-navy surfaces (0E1018/1B2030) reinforce the moody mood. Add shadow + inner highlight, lighten surfaces. |
| D6 | Outline + shadow treatment on title/headers | P1 | S | Fredoka is the right typeface, but "STUMBLE KIDS" is a flat 120pt gold fill — no outline/shadow/gradient. Add dark outline + soft shadow via the existing TMP SDF font (material change, no new art). |
| D18 | Distinct bright biome per level + a few themed props | P2 | L | `SceneAtmosphere` applies one global look to all 3 modes built from grey primitives → no biome variety (clearest prototype tell after the sky). Parameterize per-scene; theme Race/Survival/Last-Stand into distinct biomes. (Also the natural home for an optional night biome — see D1 tension.) |

### Screens / UX

| ID | Title | Pri | Effort | Why (from study) |
|----|-------|-----|--------|------------------|
| D7 | HUB + 5-tab bottom nav (replace title button-soup) | P0 | XL | `TitleScreen.cs` crams ~9 controls on one flat plane with no nav architecture. Genre's settled IA is a persistent hub + fixed bottom nav (PLAY/LOCKER/SHOP/PASS/SOCIAL); make PLAY the single dominant CTA. |
| D8 | Posed 3D character center-stage on the hub | P1 | M | No hero character on screen — seeing your character is a genre staple + retention hook. `LockerUI` already proves the live RenderTexture turntable works; reuse it. |
| D9 | Wire orphaned Locker/Pass/Quests into nav + unify Locker | P0 | M | `LockerUI.Open()` and `SeasonPassUI.Open()` had **no callers** (built but unreachable). *(Partially addressed 2026-06-04: PASS + LOCKER buttons now wired on the title — see "Already done" below.)* Still: fold skins into one Locker tab (Skins/Emotes/Victory) once the hub exists. |
| D10 | Build Profile + Social screens | P1 | L | No profile screen aggregates existing identity/stats (name, `LeaderboardStore` bests, `SeasonPass` XP); **no friends/clubs/presence at all** — biggest retention gap. Add Profile tab (move name-entry here) + a friends/party shell feeding the join-by-code lobby. |
| D19 | Real first-match FTUE funnel + first-win reward | P2 | L | Onboarding is a single static "HOW TO PLAY" card (`TutorialOverlay.cs`), not a guided first match. Add a forgiving tutorial drop, hub coachmarks, first-win bonus / free-skin funnel. |

### Store / Deploy

| ID | Title | Pri | Effort | Why (from study) |
|----|-------|-----|--------|------------------|
| D11 | Captioned, device-framed store screenshot set | P0 | L | Only two raw debug captures exist (700×324, 1077×498 — wrong size/aspect, landscape). Need 6–8 portrait phone shots + 2025 Apple baseline (6.9" iPhone 1290×2796, 13" iPad); shots 1–3 sell chaotic-race / customization / competition. |
| D12 | Play feature graphic + 15–30s preview video | P0 | M | No 1024×500 feature graphic → **Play Console hard-blocks publishing** without it. No promo video / iOS app preview (best converter for party games). Hook in the first 3 seconds. |
| D13 | Listing copy, ASO keywords, category, "What's New" | P0 | M | No store title (30-char cap), subtitle, Apple 100-char keyword field, descriptions, or category/tags. Front-load party-royale/knockout fantasy; Casual/Action + Party/Multiplayer tags. **Trademark risk:** "Stumble" + bean-art invites a copycat/impersonation rejection — consider a distinct name for the store. |
| D14 | Signed release + open Play closed-test track now | P0 | L | Debug-signed sideload APK only (keystore empty, `androidUseCustomKeystore:0`). Post-2023 personal Play accounts need a closed test, **12+ testers opted in for 14+ days** before production — a hard ~2-week blocker. Start the clock immediately. |
| D15 | IARC/Apple age rating + Data Safety / privacy label | P0 | S | Unusually clean posture (hosted honest `web/privacy.html`, no ads/analytics/account SDKs) but nothing submitted. Trivial honest "no data collected" at a low age band. **First verify `ProjectSettings submitAnalytics:1` is disabled** for shipping so it doesn't contradict the privacy claim. |
| D16 | Disable Unity splash + add a 1s branded splash | P1 | S | Unity Personal splash still ON; no branded first-impression (only favicon + SK icon). Add a ~1s branded splash (logo + mascot on gradient); remove Unity splash after tier upgrade. |
| D17 | Native in-app rating prompt wired to a win event | P1 | S | Reviews are a top-3 ASO factor; no SKStoreReviewController / Play In-App Review yet. Cheap to add via the `GameEvents` bus (fire on a win behind an "enjoying the game?" happy fork) so it's ready day-one. |
| D20 | Localize listing copy + screenshot captions | P2 | M | English/US-only; party/runner games over-index in BR, ID, TR, HI and both stores index localized keywords separately. Defer until the first English listing is live (caption localization is highest-ROI). |

---

## Already done (this session, 2026-06-04)

These overlap the backlog and were shipped as part of the live-ops build, so they are **not**
open work:

- **Battle Pass + daily/weekly quests** (`SeasonPass.cs`, `QuestSystem.cs`, `SeasonPassUI.cs`) — the
  Pass/Quests screens D8/D9 reference now exist and are reachable.
- **Cosmetics Locker** (emotes + victory poses, live 3D preview — `EmoteSystem.cs`, `LockerUI.cs`).
- **D9 (partial):** `PASS` and `LOCKER` buttons wired on the title screen, so both previously
  orphaned screens are now reachable. The *full* hub/unified-Locker (D7/D9) is still open.
- Analytics logger + UGS backend (Cloud Save / Leaderboards) scaffolding — **dormant** until the
  cloud dashboard services are enabled and a real key/endpoint is provided.

## Notable cross-cutting findings (context, not tasks)

- **Strongest existing areas:** in-match HUDs (per-mode), results/victory flows, the design-token
  system, and button-press feel are already production-shaped.
- **Privacy is a competitive advantage:** no account, no ads/analytics SDKs, all state local — makes
  the Data Safety form + Apple privacy label an honest "no data collected" at a low age band.
- **Landscape orientation** is unusual for a casual-mobile store set (portrait-dominant chart);
  flagged as a "non-native-mobile" signal worth revisiting.
- **No cross-progression today** (local-only by design) — a future positioning opportunity that
  would require an account/backend and would change the privacy label.

## Sources (study bibliography)

Play/App Store listings for Stumble Guys, Fall Guys, Brawl Stars, Subway Surfers; pixune.com &
marticocompany.com (2025–26 mobile-game UI); medium.com claymorphism/juicy-UI guides;
fonts.google.com/specimen/Fredoka; theorangebyte.com splash guidelines; Apple 2025 age-rating &
screenshot-spec changes; Google Play closed-testing (12-tester/14-day) policy.
