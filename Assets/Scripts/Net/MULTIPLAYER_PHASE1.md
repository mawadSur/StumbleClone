# Multiplayer — Phase 1 (Editor Wiring Checklist)

The Phase-1 **code** groundwork is in place and compiles against Netcode for GameObjects
(`com.unity.netcode.gameobjects` 2.2.0, `com.unity.transport` 2.7.2). It is **dormant**: nothing
spawns a `NetworkManager`, so single-player is completely unaffected. To make networked play actually
run, a human must do the editor steps below — they cannot be done headlessly.

## What already exists (code)

| File | Role |
|------|------|
| `NetworkBootstrap.cs` | Host/client entry points + `ConfigureTransport(address, port, useWebSockets)` helper. |
| `NetworkInputProvider.cs` | `NetworkBehaviour` implementing `IPlayerInput`. Owner streams local input to the server; non-owners read replicated values through the `IPlayerInput` surface. |
| `NetworkPlayerLink.cs` | `NetworkBehaviour` on the player. On spawn, for non-owners it calls `PlayerController.SetInputSource(networkInputProvider)` so remote players are driven by replicated input; the owner keeps local input. |

The input seam (`IPlayerInput`, `PlayerController.SetInputSource`, `PlayerInputHandler`) is unchanged —
the movement code does not know or care whether input is local or networked.

## Editor steps (do these, in order)

### 1. Make `Player.prefab` a NetworkObject
- Open `Player.prefab`.
- **Add Component → Network Object** (`NetworkObject`). This is required on every spawnable networked object.
- **Add Component → Network Input Provider** (`NetworkInputProvider`). On the owner it auto-resolves the
  local `PlayerInputHandler`; you may also drag the handler into its `localInput` field explicitly.
- **Add Component → Network Player Link** (`NetworkPlayerLink`). It auto-resolves `PlayerController` and
  `NetworkInputProvider` on the same object; optionally drag them into the fields to skip the lookup.
- Save the prefab.
- (Phase 2) For server-authoritative movement you'll also add a `NetworkTransform` / `NetworkRigidbody`,
  but Phase 1 only needs input replication, so skip those for now.

### 2. Create the NetworkManager + transport
- In the gameplay scene (or a dedicated bootstrap scene loaded first), create an empty GameObject named
  `NetworkManager`.
- **Add Component → Network Manager** (`Unity.Netcode.NetworkManager`).
- **Add Component → Unity Transport** (`Unity.Netcode.Transports.UTP.UnityTransport`). The NetworkManager
  auto-detects it as the active `NetworkTransport`.
- Leave the NetworkManager's "Start" behavior OFF — sessions start only when game code calls
  `NetworkBootstrap.StartHost()` / `StartClient()`, keeping everything dormant until then.

### 3. Register Player.prefab as the PlayerPrefab
- On the NetworkManager, under **NetworkConfig**, set **Player Prefab** = `Player.prefab`.
- NGO will then auto-spawn one Player per connected client and assign ownership to that client. That
  ownership is exactly what `NetworkPlayerLink` / `NetworkInputProvider` key off (`IsOwner`).
- Make sure `Player.prefab` is also in the **Network Prefabs** list if you reference it as a prefab
  elsewhere (the PlayerPrefab slot registers it automatically; additional spawns need the list).

### 4. Transport protocol: WebSocket (WebGL) vs UDP (native)
- WebGL **cannot** open raw UDP sockets — a WebGL build **must** use WebSockets.
- Native desktop / iOS / Android use plain UDP for lower latency.
- Set this from code before connecting:
  ```csharp
  // WebGL build:
  NetworkBootstrap.ConfigureTransport("127.0.0.1", NetworkBootstrap.DefaultPort, useWebSockets: true);
  // Native build:
  NetworkBootstrap.ConfigureTransport("127.0.0.1", NetworkBootstrap.DefaultPort, useWebSockets: false);
  ```
- Host and client must agree on the protocol. `UseWebSockets` must be set **before** the socket binds;
  `ConfigureTransport` does this, so call it before `StartHost` / `StartClient`.
- For a hosted WebSocket server you'll later need TLS/`wss://` + a cert (out of Phase-1 scope; localhost
  testing works over plain `ws://`).

### 5. Run a 2-client local test
Two instances on one machine, talking over `127.0.0.1`:

**Option A — ParrelSync / Multiplayer Play Mode (fastest loop)**
- Use Unity's **Multiplayer Play Mode** (Window → Multiplayer Play Mode) or the ParrelSync clone tool to
  open a second virtual player from the same project.
- Player 1: call `ConfigureTransport("127.0.0.1", 7777, useWebSockets:false)` then `StartHost()`.
- Player 2 (clone): same `ConfigureTransport`, then `StartClient()`.

**Option B — Two builds (closest to shipping)**
- Make a desktop build (UDP) — or a WebGL build served locally with WebSockets enabled.
- Launch instance A → host; launch instance B → client; both point at `127.0.0.1:7777`.

**What you should see**
- Two Player objects spawn. Each instance owns one (`IsOwner == true`) and drives it with local input.
- The other (remote) Player on each instance is driven by replicated input via `NetworkPlayerLink` →
  `SetInputSource(networkInput)`. Move on host → remote sees it move; jump/push edges replicate.
- A small input latency (~1 tick) on remote players, and possible coalescing of rapid presses, are the
  expected Phase-1 approximations (see edge-handling notes in `NetworkInputProvider.cs`).

> A LAN test across two machines is the same, but use the host's LAN IP instead of `127.0.0.1` and open
> the port in the firewall. Internet play needs Relay/NAT punch-through — Phase 2.

## Known Phase-1 limitations (by design)

- **Input only is replicated**, not transforms. Both sides simulate the Rigidbody locally from the same
  input, so positions will drift without a `NetworkTransform`/`NetworkRigidbody` + reconciliation.
- **Edge-press approximation.** `JumpPressed`/`PushPressed`/`PausePressed` are modeled as replicated
  counters consumed once; rapid presses in one replication window can coalesce, and a press that arrives
  while `InputLocked` is held is deferred (fires when the lock lifts) rather than dropped.
- **No bots, no win logic, no lobby** over the network yet (see Phase 2).
- **`Pause` over the network is questionable** — pausing is normally a local-only concern; it's
  replicated here for completeness but a networked game likely shouldn't let one client pause the
  session. Decide policy in Phase 2.

## What Phase 2 needs

- **Server-puppeted bots.** `NavMeshAgent` has no networking API. Run AI/NavMesh on the server only and
  replicate the resulting transforms (e.g. a server-write `NetworkTransform`); clients render bots as
  pure visuals. `BotController` currently drives a kinematic Rigidbody from the agent — that stays
  server-side.
- **Client-side prediction + reconciliation** for the player Rigidbody so owners feel zero input latency
  while the server stays authoritative (store input + state per tick, replay on correction).
- **Replicated `RacerRegistry` / win logic.** Racer registration, finish/elimination, ranks, survival
  timer, and shrink radius (the `GameEvents` bus) must become server-authoritative and replicate to
  clients (NetworkVariables or RPC fan-out) instead of each client computing locally.
- **`NetworkTransform`/`NetworkRigidbody`** on `Player.prefab` and bot prefabs.
- **Lobby / matchmaking + Relay** for 8-player internet play (Unity Relay/Lobby or a custom dedicated
  server), with `wss://` + TLS for WebGL.
- **Ownership & spawn flow** for power-ups, hazards, and arena tilt so their effects are consistent
  across clients.

---
*This file documents human-only editor steps; the corresponding code (`NetworkBootstrap`,
`NetworkInputProvider`, `NetworkPlayerLink`) compiles and is dormant until the above wiring exists.*
