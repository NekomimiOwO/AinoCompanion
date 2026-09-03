A mod for the game R.E.P.O that adds an NPC/PET as Aino from Genshin Impact that helps the player, still under development. 

https://thunderstore.io/c/repo/p/MarceloOwO/AiChanCompanion/

A video of an older version of the mod:

[![Here a video of it](https://img.youtube.com/vi/xXLUuVCpgSU/0.jpg)](https://youtu.be/xXLUuVCpgSU)


# To fix:

- <del>Incosistency in the readme on "the host sends a snapshot every 0.05 seconds, up to 20 updates per second" the mod is sending every 0.02 seconds, up to 50 updates per second, I forgot to change back.</del>
- <del>evaluate reducing the network transmission rate of the mod from 50 to 20 or 30.</del>
- <del>Fix the cart not pushing her</del>
- <del> Add debug rays to the jump</del>
- <del> Add debug rays for navmesh navigation</del>
- <del>explode often deactivates her for a long time on ground (just pick her up and drop her, and she should come back...).</del>
- She sometimes goes inside the store tables.
- <del>rewrite and revise the readme to make it seem less artificial.</del>
- <del>delete "(like Factories/Mines)" from README</del>
- <del>Fix rotation on multiplayer for the client (priority)</del>
- <del>Investigate and fix the owner switch key</del>
- <del>The go command is probably using the master instead of the owner</del>
- <del>Remove: "NavMeshObstacle Carving Conflict: The PhysGrabCart actively carves a dynamic hole in the NavMesh. The Unity NavMeshAgent's native obstacle avoidance would detect this hole and force the agent to violently brake (dropping speed from 3.5m/s to ~1.5m/s) to prevent "falling off" the NavMesh." from the readme, since she walks normally near the cart—even generating NavMesh paths through it.
- <del>Make the interpolation system better.</del>

## Network Infrastructure Hardening To-Do List (It will not be done soon)

### Verified Architecture Notes
> **Transport Mode Reality:** `RepoSteamNetworking` transmits all messages using `(SendType)8` (`SendType.Reliable` / `k_nSteamNetworkingSend_Reliable`). This guarantees in-order, lossless packet delivery at the transport layer, eliminating the need for manual event retransmission or sequence filtering. However, running high-frequency 20 Hz position snapshots over a reliable stream introduces Head-of-Line Blocking during packet loss, which requires specific client-side buffering care.

---

### Active Action Items

- [ ] **Sanitize and validate incoming transform packets**
  Before applying `PetStatePacket` data to transforms or rigidbodies, verify that coordinates and rotations do not contain `float.IsNaN`, `float.IsInfinity`, or degenerate quaternions `(0, 0, 0, 0)`. Discard invalid packets to prevent Unity matrix corruption, invisible meshes, and console spam.

- [ ] **Include target entity ID in continuous snapshots (Full Self-Healing)**
  Extend `PetStatePacket` to include `CarriedTargetViewID` (int) when the pet is in `CarryItemToCart` state. If a client encounters a lifecycle desync, the next 50 ms snapshot allows immediate visual binding to the correct item/player without relying exclusively on one-time sync events.

- [ ] **Fix stationary heartbeat deadzone in `ShouldSendNetworkTransform`**
  In `PetCompanionController`, the condition `!moving && !recentMovement && !movedEnough && !rotatedEnough && hasNetworkSentTransform` completely halts packet transmission when idle. Adjust the logic so that the pet continues to send a lightweight transform packet at the planned `NetworkStoppedInterval` (1.5s) to anchor position and support late-joining clients.

- [ ] **Implement dynamic catch-up (time dilation) for snapshot bursts**
  Because the reliable transport can cause packet bursts after network stalls, the client buffer receives multiple snapshots in a single frame. Instead of hard-clamping synthetic local timestamps into the future (`packetTime = Mathf.Max(...)`), implement a slight playback speed-up (5%–10% time dilation) when the buffer exceeds target capacity, draining backlog smoothly before resorting to a hard snap.

- [ ] **Add debug logging for network anomalies**
  Utilize `Plugin.LogDebug(Plugin.LogCategory.SteamNet, ...)` behind `PetSettings.EnableDebugLogs` to log dropped packets, buffer overflow discards, missing `PhotonView` targets, and invalid float values.

- [ ] **(Maybe) Separate continuous snapshots to Unreliable transport**
  If `RepoSteamNetworking` adds support for specifying transport modes, switch `PetStatePacket` to `SendType.Unreliable` (`0`) or `SendType.NoNagle` (`1`), keeping RPCs and discrete state events on `SendType.Reliable` (`8`) to eliminate Head-of-Line Blocking entirely.

---

### Resolved / Superseded Items

- [x] ~~**Use reliable sending for critical one-time events**~~
  *Status: Already satisfied.* `RepoSteamNetworking` forces `(SendType)8` across all send paths, ensuring reliable delivery for `PetGiveItemPacket`, `PetSyncCarryPacket`, `PetExplodePacket`, etc.

- [x] ~~**Add sequence numbers to discrete event packets**~~
  *Status: Unnecessary / Superseded.* Steamworks Reliable channels guarantee strict in-order packet delivery. Applying sequence-based dropping on discrete state transitions is redundant and risks dropping valid state transitions.

- [x] ~~**Re-send client preferences until acknowledged**~~
  *Status: Already satisfied by transport.* `PetClientPreferencesPacket` is sent reliably via Steam. Ensure only that the local avatar and room connection are valid before invoking `SendPreferencesRoutine`.

- [x] ~~**Drop oldest snapshots on buffer overflow**~~
  *Status: Already implemented.* `NetworkInterpolation.cs` already executes `if (stateBuffer.Count > 20) stateBuffer.RemoveAt(0);`.

## Feedback


For feedback, suggestions, or to report errors, feel free to use the form below:
Does not require an email address.

https://docs.google.com/forms/d/e/1FAIpQLScmZjEb3weX5crLXjnJZaA0WBDP47EC4TETDK8DDkSCmK28fw/viewform?usp=dialog


Below, the readme:

# Ai-Chan Companion

> An intelligent companion for **REPO**. Ai-Chan follows the team, accepts light items (can change in configs), brings objects to the cart, can carry tumbled players, and includes chat commands, interactive physics, and Steam-powered multiplayer synchronization.

**Plugin GUID:** `com.neko3004.aichancompanion`

---

## Overview

First of all, if you are seeing this mod on Thunderstore or any other modding platform, it means I decided the mod is good enough and not something broken that doesn't work properly.
The initial idea and production for this mod began on August 6, 2026, at the end of my vacation and after I had finished creating my first simple mod to fix another mod (I didn't imagine it would be so difficult... I don't plan on making another mod anytime soon -_- )
As there have been many changes to the mod, and each significant change requires testing to be performed from scratch, some parts of this readme may be outdated, although most of it should be correct.

Ai-Chan Companion adds one pet companion to the game. She spawns automatically at the start of each level and in the shop, appears near a player, and chooses an owner to follow. 

She uses NavMesh navigation and Ghost Probing to move intelligently around obstacles, pathfinds toward her owner, can open doors, and includes recovery systems to prevent her from getting stuck. Players can grab and throw her as a physics object; after landing, she recovers and resumes following her owner.

In multiplayer, the host/Master Client is authoritative for the AI. Other players receive synchronized position, rotation, state, owner, and interaction events.

## Features

- Automatic Ai-Chan spawn in levels and in the shop.
- Floating Ai-Chan name tag above the character.
- Minimap Tracker: Ai-Chan appears as a distinct pink/magenta circle marker on the dirt finder minimap.
- Automatic owner following with configurable distance.
- Automatic owner switching in multiplayer.
- Manual owner switching by the current owner.
- Chat commands to call, send away, jump, play dead, change to preset sizes, or set a custom size.
- Petting system with animation, sound, and heart particles (heart particles not working properly yet).
- Can be grabbed, carried, and thrown by players.
- Falling physics, knockdown impact resistance, and recovery after being released or hit by fast-moving objects.
- No physical collision with players, preventing unwanted blocking and pushing.
- NavMesh movement with Ghost Probing to avoid tables/walls, automatic jumps when stuck, door opening, and an emergency safety teleport when stuck.
- Accepts objects held by players based on configurable mass limits.
- Carried Item Scaling(opcional): Items can optionally inherit her scale while she carries them (host only config).
- Carries accepted items to the cart/delivery objective when a valid target is available. If two carts or more are present, she chooses the nearest; if no carts are present, she will go to the active extractor.
- Can carry players in a tumble state or in a dead state.
- Audio feedback for commands and interactions.
- Network Profiler for tracking the mod steam data usage.
- Explosion!! :3

## Installation

### Thunderstore Mod Manager

1. Install **GenshinImpactOverhaul_REPO** by `GoblinKingShmee`. (If you want Aino but don't want the GenshinImpactOverhaul_REPO mod to replace the enemies, simply disable the replacement in the GenshinImpactOverhaul_REPO mod settings; my mod should still work.)
2. Install **REPO_SteamNetworking_Lib** by `Rune580`.
3. Install **Ai-Chan Companion** in the same profile.
4. Install **REPOConfig** by `nickklmao`.
5. Launch the game through Thunderstore Mod Manager or another compatible mod manager.

> **Important:**
> Ai-Chan uses the Aino prefab supplied by `GenshinImpactOverhaul_REPO`. The companion visual cannot be created without that dependency.
> REPO_SteamNetworking_Lib is necessary for multiplayer.

### Multiplayer

Multiplayer should already work for now...at least for what I tested with my friend (Woolfy), which I'm grateful for spending hours with me compiling, opening the game, testing, failing, fixing, compiling, opening, etc. <3

For a consistent multiplayer experience, every player in the lobby should use:

- Ai-Chan Companion on the same version
- GenshinImpactOverhaul_REPO
- REPO_SteamNetworking_Lib
- REPOConfig

The mod requires strict compatibility with the Steam networking library. Using different versions can prevent loading or correct synchronization.

## How it works

### Spawning and ownership

- In single-player, Ai-Chan is created locally at the beginning of a level or in the shop.
- In multiplayer, only the **Master Client/host** creates the authoritative pet instance.
- When joining an active room, a client requests synchronization after the Steam handshake; the host replies with the pet position and `ViewID` so that client can recreate the same pet locally.
- On startup, Ai-Chan selects the nearest player as her owner and starts following them.
- In multiplayer, ownership can automatically change every 3 minutes by default. The selection prioritizes players who have not yet been selected during that level.
- Only the current owner can request a manual ownership switch.

### Movement and recovery

Ai-Chan follows her owner through the NavMesh and stops near them according to the configured follow distances. With the probing system, it is able to navigate outside the navmesh. She can also open closed doors in her path. 

If she encounters an obstacle or a partial path, she may perform an automatic jump. If she remains far above her owner for several seconds or falls into the void, the safety system teleports her to a navigable position near the owner.

She can be grabbed and thrown. While held or falling, her navigation is paused. When she touches the ground or after the recovery timeout she stands up, finds the NavMesh again, and returns to the following state. Fast-moving heavy objects hitting her will also knock her down based on the configured impact resistance.

## Pathfinding & Movement Overhaul (The "Jitter" Fix)
After extensive debugging, a critical issue causing severe jittering, stuttering, and velocity drops (especially when carrying the player/items to the cart) has been completely resolved on August 24, 2026. (10+ hours just on it...I almost gave up on fixing it, it was soo exhausting). 

**The Core Problems (Suspects):**
1. **Procedural Floor Interpolation Tug-of-War:** In procedural levels, the floor has micro-seams. The `NavMeshAgent` forced the position to snap to these seams, while the `Rigidbody` interpolation tried to smooth it out. This created a 60-frames-per-second mathematical conflict, resulting in visual stutters.
2. **Update vs LateUpdate Desync:** Calculating carrying positions in `Update` while the camera renders in `LateUpdate` created a 1-frame visual desynchronization.

**The Solution (The "Decoupled Agent" Architecture):**
* **Brain/Body Decoupling:** Disabled `agent.updatePosition` and `agent.updateRotation`. The `NavMeshAgent` (the brain) now silently calculates the perfect mathematical path, while the physical 3D model (the body) uses a smooth `Vector3.Lerp` to follow it. This entirely absorbs any procedural floor bumps.
* **Native Evasion Disabled:** Disabled `autoBraking` and set `ObstacleAvoidanceType.NoObstacleAvoidance` so the pet confidently maintains a constant 3.5m/s speed without fearing the cart's carved hole.
* **Total Ragdoll Suppression:** Implemented a root-level collider scan that temporarily ignores collision for *every single limb* of the carried player, eliminating all physics friction.
* **Unified LateUpdate Kinematics:** All physical transportation of items and players is now strictly enforced inside `LateUpdate` with forced `isKinematic = true`, ensuring 1:1 frame-perfect synchronization with the player's camera.

### Items and rescue

1. Hold a physics item (She will not pick up carts, monsters, environment objects like doors or cosmetic boxes).
2. Stand near Ai-Chan.
3. Press the give-item key, `R` by default.
4. She picks up the object and attempts to bring it to the available cart/delivery destination.
5. If two or more players are holding the item, she will not carry it to prevent sync errors.
6. If she is grabbed or falls during the delivery, the item will drop on the ground near her.

The same interaction can be used with a player in a downed/tumble state. Hold a downed teammate and press `R`, or, if your own character is downed, press `R` while holding no item. Ai-Chan will attempt to carry that player.
She can optionally carry dead players.

> The accepted maximum item mass is configurable and defaults to 3. The internal item-give distance defaults to 4.5 m. Carried items can optionally scale to match Ai-Chan's current size by enabling the `Inherit Pet Scale On Carry` setting.

### Shop extractor behavior

In the shop, Ai-Chan can deliver items to the **Extraction Point** (the shop's "cart" equivalent). For this to work correctly:

- The extraction point must be **unlocked and active**.
- If the extraction point is locked, Ai-Chan will **instantly drop the item or player** at her feet instead of attempting delivery.
- This prevents the pet from getting stuck trying to deliver to an unavailable objective.
- She will not pick up carts (It would be chaos if she could 0.0).

When a valid extraction point is available, Ai-Chan approaches it and drops the item at a randomized position within the "In Cart" collider to avoid stacking items exactly on top of each other.

## Controls

| Action | Default key | Requirements |
|---|---:|---|
| Pet Ai-Chan | `E` | Be within 3 m, look at Ai-Chan, has a 1-second cooldown |
| Give item / carry player | `R` | Be near the pet; the item or player must be valid |
| Switch owner | `F5` | Only the current owner can use it | 

All three keys can be changed in the configuration.

## Chat commands

To recognize a command, the chat message must include a pet keyword: `aino`, `ai-chan`, `aichan`, or `pet`.
> It works with the SoloChat mod, so it doesn't necessarily need to be a hosted online room.

| Command example | Effect | Who can use it |
|---|---|---|
| `Ai-Chan go [dist]` / `vai [dist]` / `walk` / `anda` | Makes Ai-Chan walk to where the owner is looking (0.5m to 20m, default 4m) | Current owner |
| `Ai-Chan explode [delay]` / `exploda` / `kaboom` | Starts an explosion countdown with beeps and warning tag (1s to 30s) or explodes immediately | Current owner (if enabled) |
| `Ai-Chan stop` / `para` / `fica` | Cancels active manual movement or cancels explosion countdown | Current owner |
| `Ai-Chan drop` / `solta` / `larga` / `release` | Forces Ai-Chan to immediately drop any held item or player | Current owner |
| `Ai-Chan help` / `commands` / `ajuda` | Shows the local help text | Any player |
| `Ai-Chan net` / `rede` | Prints network profiler stats to the local console | Any player (Local only) |
| `Ai-Chan jump` / `pula` / `pule` | Makes Ai-Chan jump | Any player |
| `Ai-Chan come` / `here` / `vem` / `aqui` | Calls Ai-Chan close to the owner | Current owner |
| `Ai-Chan away` / `sai` / `longe` | Sends Ai-Chan away temporarily | Current owner |
| `Ai-Chan dead` / `play dead` / `morta` / `deita` | Makes Ai-Chan play dead for about 6 seconds | Current owner |
| `Ai-Chan small` / `pequena` / `mini` | Sets small size (0.5x) | Current owner |
| `Ai-Chan big` / `grande` / `gigante` | Sets large size (1.8x) | Current owner |
| `Ai-Chan normal` / `padrao` | Restores normal size (1.0x) | Current owner |
| `Ai-Chan size 2.5` / `tamanho 2.5` | Sets a custom exact size | Current owner |
| `Ai-Chan switch` / `pass` / `troca` / `leave` | Transfers Ai-Chan to another player | Current owner |

> The mod matches keywords in sentences, so typing "become small aino" will still trigger the small size command (0.5x).
> Commands that change the AI are processed by the Master Client. This prevents different clients from controlling the same pet at the same time.
> Chat messages are read locally only to trigger pet commands and are not hosted, stored, or sent to any external server.
> Why not just a keybind? Aside from there being so many commands, I thought it would be cool to hear commands from friends, since the game has a chat-reading system.
> Supports English and Brazilian Portuguese aliases.

## Configuration

After launching the game once, open the in‑game ModMenu configuration UI to adjust Ai‑Chan settings.

While the host still dictates the global physics configurations for everyone, clients can now define their own personal settings as well, such as Follow Range and Stopping Distance. These client-specific preferences are automatically read and applied every time she spawns in a level.

Don't be afraid to change these movement, interaction, and physics settings, as they may not be balanced or fit your play style.

| Section | Option | Default | Range / description |
|---|---|---:|---|
| Delivery | `CartApproachDistance` | `1.6` | Distance for Ai-Chan to approach the cart before stopping (0.5 to 4.0m) |
| Delivery | `CartDropDistance` | `1.8` | Maximum distance (in meters) to throw/drop item into the cart (0.5 to 5.0m) |
| Delivery | `ShopDropDistance` | `1.2` | Maximum distance (in meters) to drop item at the shop counter (0.5 to 4.0m) |
| Delivery | `GiveItemDistance` | `4.5` | Maximum distance to accept an item from a player's hand (2 to 8m) |
| Movement | `Speed` | `3.5` | Ai-Chan movement speed (1 to 10) |
| Movement | `Carry Speed` | `3.5` | Movement speed while carrying an item or player (1 to 10) |
| Movement | `Auto Jump Stuck Delay` | `1.0` | Time spent stuck before attempting an automatic jump (0.5 to 5s) |
| Movement | `Follow Range (Start)` | `2.0` | Distance at which she begins following (0.5 to 10m) |
| Movement | `Stopping Distance (Stop)`| `2.0` | Distance at which she stops near the owner (0.1 to 10m) |
| Movement | `Min Jump Obstacle Height` | `0.75` | Minimum obstacle height to jump (0.35m to 2.5m). Allows smooth stair walking |
| Movement | `Max Jump Obstacle Height` | `3.5` | Maximum obstacle height she will attempt to jump onto (1.5m to 8.0m) |
| Interaction | `Enable Carrying Dead Players`| `true` | Allows Ai-Chan to carry dead player heads to the cart/extraction like a normal item |
| Interaction | `Max Carried Mass` | `3.0` | Maximum item mass Ai-Chan can carry (0.5 to 20) |
| Interaction | `Enable Door Opening` | `true` | Allows Ai-Chan to unlock and physically push open nearby map doors.|
| Interaction | `Inherit Pet Scale On Carry`| `false` | Carried items scale proportionally with the pet's size |
| Commands | `Enable Explode Command` | `false` | Enables owner chat commands to detonate the pet |
| Explosion | `Player Damage` | `10` | Damage dealt to players within the explosion radius (0 to 200) |
| Explosion | `Enemy Damage` | `50` | Damage dealt to monsters/enemies (0 to 500) |
| Explosion | `Explosion Radius` | `1.2` | Size and radius of the explosion in meters (0.5 to 10.0m) |
| Explosion | `Explosion Force Multp.` | `1.0` | Multiplier for physical impulse and body launch force (0.5 to 10.0) |
| Physics | `Body Mass` | `1.5` | Ai-Chan physical mass (0.2 to 10) |
| Physics | `Knockdown Impact Resistance`| `3.5` | Minimum impact speed (m/s) required to knock Ai-Chan down (0.5 to 10) |
| Physics | `Stand Up Delay` | `2.0` | Delay before standing after a fall (0 to 10 seconds) |
| Physics | `Angular Drag` | `0.5` | Rotational resistance while thrown (0 to 10) |
| Audio | `Volume` | `50` | Audio volume percentage (0 to 100) |
| Multiplayer | `Owner Switch Interval` | `3.0` | Automatic owner-switch interval in minutes.|
| Multiplayer | `Enable Adaptive Interpolation`| `true` | Dynamically adjusts movement smoothness based on internet fluctuation and delay |
| Multiplayer | `Enable Anti-Flick Rotation`| `true` | Prevents Ai-Chan from doing bizarre spins when packets are dropped |
| Multiplayer | `Enable Snapshot Interpolation`| `true` | Uses a Jitter Buffer that delays Ai-Chan slightly in the past for smoothness |
| Multiplayer | `Snapshot Buffer (ms)` | `100` | The size of the Snapshot delay. (50 to 500ms) |
| Controls | `Give Item Key` | `R` | Item/rescue interaction key |
| Controls | `Pet Key` | `E` | Petting key |
| Controls | `Switch Owner Key` | `F5` | Key to transfer the pet to the next player |
| Logs | `Enable Debug Logs` | `true` | Enables network and debug logs in the console |
| Logs | `Enable State Transition Logs`| `false` | Logs detailed pet state transitions |
| Logs | `Enable NavMesh Transition Logs`| `false` | Logs when Ai-Chan enters or leaves the NavMesh |
| Logs | `Enable Carry Logs` | `false` | Logs verbose delivery and jitter diagnostics (spams console) |
| Performance | `Enable Ghost Probing` | `true` | Enables multi-ray pathfinding to avoid tables/walls smoothly |
| Performance | `Ghost Probe Distance` | `2.5` | Distance ghost probes look ahead to avoid walls (1.0 to 5.0m) |
| Performance | `Ghost Probe Update Interval`| `0.1` | How often pathfinding is calculated in seconds (0.02 to 0.5s) |
| Performance | `Ghost Probe Rays` | `7` | Number of projection rays (1 to 12) |
| Performance | `Enable Debug Rays` | `false` | Visualizes the ghost simulation paths in-game (Host only) |
| Performance | `Debug Rays Fade Time` | `0.25` | How long debug rays remain visible on screen |
| Performance | `Debug Breadcrumbs Fade Time`| `0.15` | How long breadcrumb trail rays remain visible |
| Experimental | `Enable Network Shadow` | `false` | Spawns on start a ghost Ai-Chan next to the real one in Singleplayer to visualize a network simulation |
| Experimental | `Shadow Simulated Ping` | `50` | Simulated latency for the ghost in milliseconds (0 to 100ms) |
| Experimental | `Shadow Packet Loss (%)`| `5` | Simulated packet loss percentage for the ghost (0 to 100%) |
| Experimental | `Shadow Simulated Jitter` | `20` | Simulated ping fluctuation for the ghost in milliseconds (0 to 200ms) |

> I'm from Brazil, so don't be surprised if I forgot to translate some of the logs into English, oops :3


### Explanation of some network settings added for my mod (client-side)

**Snapshot Buffer (ms)**
This setting defines the size of the "waiting room" for incoming network packets. **It does not add to the server's tick rate.** The Host always broadcasts Ai-Chan's position every 50ms (20 times per second). 

*   **`100` (Default & Recommended):** The Client holds exactly 2 packets in reserve before rendering the movement. This guarantees a mathematically perfect, buttery-smooth glide even if your internet fluctuates or drops packets.
*   **`50` (LAN):** The Client holds only 1 packet in reserve. Ai-Chan will react almost in true real-time, but there is **zero margin for error**. If a packet is delayed by even 1 millisecond due to ping jitter, the buffer dries up and the pet will micro-stutter on your screen.
*   **`150+` (Unstable Connections):** Increases the reserve to 3+ packets. Use this only if playing with very high ping or severe Wi-Fi packet loss.

**Enable Adaptive Interpolation**
Acts as a fallback system. If your internet completely chokes and the Snapshot Buffer runs dry, this dynamic system takes over. It constantly monitors your real-time ping fluctuation (jitter) and automatically downgrades Ai-Chan's interpolation speed to prevent severe rubber-banding, keeping her movement as smooth as possible until the connection stabilizes.

**Enable Anti-Flick Rotation**
A strict mathematical safety lock for her spine. When playing online, lost packets can cause severe rotation desyncs, making characters do bizarre backflips or snap their necks (Gimbal Lock) when the next packet suddenly arrives. If her rotation difference exceeds 100 degrees instantly, this setting overrides the organic physics and forces a restricted rotation path to keep her looking natural.


## Multiplayer networking

Ai-Chan Companion combines the game's Photon room infrastructure with **REPO SteamNetworking Lib** to send custom pet packets through Steam.

> **REPO SteamNetworking Lib** is an API for mod developers to offload mod networking from REPO’s Photon servers, helping the REPO devs save bandwidth. Ai‑Chan Companion uses it to send pet packets over Steam networking.

### Authority

The Master Client is responsible for:

- creating Ai-Chan in online sessions;
- running navigation, AI, door interactions, and relevant physics;
- processing item-give and player-carry requests;
- switching the owner;

Remote clients do not run pet navigation or physics. They reproduce the received position, rotation, state, and owner, while keeping the `Rigidbody` kinematic to avoid physics divergence.

The host should have the best computer among the players to avoid network lag regarding the pet for the clients. The mod should not be resource-heavy, it ran okay on my 3 year old laptop with intel integrated graphics.

The most resource-intensive part of the mod is likely when she navigates outside the navmesh; reduce the number of rays if necessary.


### State updates

The host sends a snapshot every 0.05 seconds, up to 20 updates per second, containing:

- `PetViewID` to identify the instance;
- `OwnerViewID` to identify the current owner;
- pet state;
- sequence number;
- position;
- rotation.

On clients, short movement corrections are smoothed through interpolation. If the position difference exceeds 3.0 m, the pet is moved directly to the received position to correct visible desynchronization.

### Synchronized events

In addition to continuous state, Steam networking sends dedicated packets for:

- pet spawning and synchronization for late-joining players;
- synchronization requests to the host;
- client-specific configuration preferences to the host;
- item delivery;
- player-carry requests;
- picking up and dropping an item or player (including scale inheritance);
- petting;
- manual owner switching.
- explosion trigger and countdown;

Item delivery, player carrying, and owner-switch events are sent to the host. The host resolves the referenced objects through their `PhotonView`, executes the authoritative action, and sends the necessary results to the other players via steam network.

## Troubleshooting

### Ai-Chan does not spawn

- Confirm that `GenshinImpactOverhaul_REPO` is installed and loads before this mod.
- Confirm that `REPO_SteamNetworking_Lib` is installed.
- Check the BepInEx console for errors and keep `Enable Debug Logs = true` while diagnosing the issue.

### A client cannot see the pet

- Make sure the host and clients have the mod and all dependencies installed.
- Make sure every player uses compatible versions.
- Rejoin the room if the Steam handshake fails; a client requests pet reconstruction about 2 seconds after joining.

### Ai-Chan does not accept an item

- Stay within the configured interaction distance.
- Hold a valid physics item that does not exceed `Max Carried Mass`.
- Make sure the pet isn't already carrying something.

## Compatibility and notes

- This mod is made for REPO and relies on game-provided classes, physics layers, NavMesh, and networking.
- The mod uses the Aino visual/prefab supplied by GenshinImpactOverhaul_REPO.
- In multiplayer, the host should maintain a stable connection because it is authoritative for the AI.
- Mods that heavily alter Photon, `PhysGrabObject` physics, carts, NavMesh, or the Aino prefab may cause incompatibilities.
- A major game update could potentially break this mod.

## Credits

- **BepInEx:** plugin loader.
- **Harmony:** runtime patching.
- **GenshinImpactOverhaul_REPO:** Insert Aino prefab/visual into the game, by `GoblinKingShmee`.
- **REPO_SteamNetworking_Lib:** Steam-based packets and multiplayer synchronization by `Rune580`.
- **REPOConfig (nickklmao):** in‑game configuration UI library.
- **R.E.P.O:** base game, physics, navigation, Photon, and interaction systems.
- **UnityExplorer** it really helped with in-game debugging.

## Thanks

A big thank you to GoblinKingShmee. I asked him via Discord, and he gave me permission to use his mod to load the model it adds to the game into my own mod.
Again, a huge thank you to my friend(Woolfy) for spending countless hours with me compiling, opening the game, testing, failing, fixing, and testing again. implementing multiplayer simply wouldn't have been possible without him <3
(So, be grateful to him too :<)


## Legal / Asset notice

- The Aino character model and related assets are the property of **HoYoverse**. This mod does not claim ownership of, nor distribute, HoYoverse assets.

## AI assistance disclaimer

This mod was developed with the assistance of AI tools for code generation and refactoring. Since I don't know C#, I heavily relied on AI to write the code while I tested, provided feedback, compiled, and rewrote parts the way I wanted. All gameplay logic, system design, implementation requirements, test plans, and iterative feedback were authored and directed by me through multiple review cycles (I gained over 50 hours of playtime just testing the mod -_-)

AI was used as a productivity aid, not as the designer of the mod's behavior or features. Most of the time, the AI would break a mechanic that was working perfectly (like grab, animations, or pathfinding/following) and couldn't fix it. When this happened, I was forced to roll back to an earlier save, losing progress. Sometimes, realizing my approach wasn't going to work, I had to start over from scratch.

For example, I was suggested to use an in-game item as a grab mechanism for Ai-Chan by inserting the item invisibly inside the model. It kind of worked—or so I thought—but it was never going to truly work because of collision issues. If the item broke, it disappeared along with its value, mechanics, and the model of Ai-Chan. It just didn't work! :)

A lot was fixed through this back-and-forth process. Many hours were spent on this mod, which I created with a lot of care~

### Debug Rays Color Guide

When `Enable Debug Rays` is turned on in the settings, Ai-Chan will project various colored lines to visualize her internal AI decisions in real-time:

**Pathfinding & Probing**
* **Cyan (Light Blue):** represents the active NavMesh GPS route.
* **White:** Vertical markers showing the exact corners/waypoints of her current NavMesh path.

**Manual Mode (No navmesh available)**
* **Cyan (Light Blue):** Free future path. The ghost probe traveled its full distance without hitting walls. 
* **Red:** Blocked path. A probe hit a wall/obstacle.
* **Green:** Best chosen path. The final, direction the AI selected to move forward safely.
* **Orange:** Breadcrumb trail system. Visualizes the recent safe steps taken by the owner and the links between them.

**Jumps & Physics**
* **Blue:** The path directly in front is clear of obstacles during a jump scan.
* **Green (Vertical):** A confirmed safe landing spot for an automatic jump.
* **Yellow:** Visualizes the predicted mathematical arc trajectory of an automatic jump.
* **Magenta:** Marks an abyss/drop-off detected, or a valid elevated ledge she is preparing to jump onto.

**Interactions**
* **Purple (Crosshair):** Door detection radar. Shows the area she is scanning to find map doors.
* **Magenta (Laser):** When she locks onto a closed door and is about to physically push it open.


## Known issues

- Her pathfinding is kinda experimental, so she might get lost if no one is in her direct line of sight, mainly in outside the navmesh.
- When spawning, she sometimes won't attach to the NavMesh properly and won't move (or moves very slowly). Just grab her and release; she will correct it. (I'm not sure if this is still happening)
- If the item explodes in her hand while she is carrying it, the item is not destroyed.

## Mod architecture (Just for curiosity, line counts might be outdated)

| File | Lines (Include blank lines and comments) | Responsibility |
|---|---:|---|
| `AiChanAudio.cs` | 151 | Controls Ai-Chan's audio system: loading bark clips, spatial audio playback, volume, distance falloff, pitch variation, and bark cooldowns. |
| `ElsaPetMod.csproj` | 114 | The .NET project file. Defines the target framework and references for the game DLLs, Unity, Photon, BepInEx, Harmony, Steam networking, and mod dependencies. |
| `NetworkInterpolation.cs` | 160 | Controls network interpolation for remote clients, including snapshot frequency, smooth position/rotation corrections, teleport snapping for large desyncs, and reduced update frequency while the pet is idle. |
| `PetCompanionController.Chat.cs` | 359 | Implements chat commands such as `come`, `here`, `away`, `jump`, `dead`, `small`, `big`, `normal`, `switch`, and custom `size` values. Also provides local help and network-stat commands. |
| `PetCompanionController.cs` | 578 | The main Ai-Chan controller. Handles pet states, initialization, owner tracking, animations, physics, grabbing/releasing, recovery, collision knockdowns, scaling, and the primary AI update loop. |
| `PetCompanionController.Delivery.cs` | 1421 | Controls item delivery and downed-player pick up. Finds carts and extractors, validates items, picks up and carries targets, handles drops, manages shop delivery behavior, and synchronizes carried objects. |
| `PetCompanionController.Explosion.cs` | 350 | Implements explosion mechanics: countdown coroutine, 3D billboard text tag, accelerating spatial audio beeps with dynamic pitch, game native particle explosion spawning, player/enemy damage application, physical impulse forces, and `PetExplodePacket` network synchronization. |
| `PetCompanionController.Interactions.cs` | 238 | Controls pet interactions: petting, heart particles, petting animation, sounds and stun behavior. |
| `PetCompanionController.Navigation.cs` | 1917 | Implements movement and pathfinding. Includes NavMesh following, owner switching, automatic jumps, Ghost Probing, door opening, anti-stuck systems, breadcrumbs, manual movement fallback, floor detection, and emergency teleports. |
| `PetInteraction.cs` | 261 | Reads local player input and converts it into pet interactions: petting, giving an item, carrying a downed player, and manually switching the owner. |
| `PetNetworkBridge.cs` | 307 | Bridges the pet controller with Steam/Photon networking. Sends and receives state snapshots, spawn events, petting, deliveries, player rescue requests, owner switching, and carry synchronization. |
| `PetNetworkProfiler.cs` | 82 | Tracks mod network usage: sent/received bytes, current upload/download rate, total traffic, uptime, and console statistics. |
| `PetSettings.cs` | 353 | Registers all BepInEx configuration entries: movement, mass, interaction distance, physics, audio, controls, owner switching, Ghost Probing, debug logs, rays, and breadcrumbs. |
| `PetSpawner.cs` | 705 | Creates and initializes Ai-Chan in levels and shops. Configures the PhotonView, Rigidbody, NavMeshAgent, colliders, Aino visual, name tag, heart particles, player collision filtering, and minimap icon. |
| `PetSteamPackets.cs` | 178 | Defines the custom Steam networking packets used by the mod: state updates, spawning, item delivery, petting, player carrying, carry synchronization, owner switching, and synchronization requests. |
| `Plugin.cs` | 72 | The mod entry point. Registers the BepInEx plugin, initializes settings, registers Steam packets, applies Harmony patches, and starts the network profiler. |

## What about the other models in GenshinImpactOverhaul_REPO?

ya..I briefly tested making a "general" version; I might even make it available later (without posting it because it's kind of terrible and it's way behind compared to what this mod does). Some models have specific peculiarities while others are easier, so I focused only on Aino, which is what initially motivated me to make this mod. But in theory, it would be possible to extend this in the future... or maybe load custom models without dependency. I don't know how imports work... hence the dependency on the GenshinImpactOverhaul_REPO mod.

Don't expect many updates, since I'm busy with a lot of things...but I'll try my best to fix the most critical issues that may appear, nya~

Anyway, I hope you have fun and give Aino lots of headpats!~
/ᐠ - ˕ -マ

## Feedback

For feedback, suggestions, or to report errors, feel free to use my **[Feedback Form](https://docs.google.com/forms/d/e/1FAIpQLScmZjEb3weX5crLXjnJZaA0WBDP47EC4TETDK8DDkSCmK28fw/viewform?usp=dialog)**. 
*(No email address required)*

*(This is a great option if you prefer not to contact me directly but still want to share your thoughts.)*

## Contact

You can contact me at discord: nekomimiowo
or at the github of this mod: https://github.com/NekomimiOwO/AinoCompanion


## Contact

You can contact me at discord: nekomimiowo
Or at the github of this mod: https://github.com/NekomimiOwO
