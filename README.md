# PipeLab Unity XR Client — Implementation Plan

## Purpose

This Unity project is the mixed-reality client for the PipeLab website in [`PROGRAMIK9/Minor-Project`](https://github.com/PROGRAMIK9/Minor-Project). The website is a browser-based plumbing-network editor and digital-twin prototype. The goal is to let Unity load the same project, display its components in mixed reality, receive authoritative project and simulation updates, and eventually send supported user edits back through the existing service.

This plan describes the work, system boundaries, validation, and VS Code-to-Unity workflow. It is not an assertion that the network client, device builds, telemetry, or spatial alignment already exist.

## Repository and project baseline

### Unity repository

- Unity editor version in `ProjectSettings/ProjectVersion.txt`: **6000.6.4f1**.
- Unity project root is the directory containing `Assets/`, `Packages/`, and `ProjectSettings/`.
- Existing Unity code includes a five-slot inventory interface in `Assets/Scripts/UI/BlockyInventoryUI.cs`.
- `Packages/manifest.json` and `Packages/packages-lock.json` contain a Bezi Sidekick dependency with a machine-local Windows path (`C:/Users/gopal/Downloads/...`). That dependency must be made reproducible or removed if unused before a clean clone can be expected to resolve all packages.
- Keep Unity `.meta` files and intentional scene/project settings under version control. Generated data such as `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, `Logs/`, and `UserSettings/` should remain ignored.

### Website repository

The linked website repository is `PROGRAMIK9/Minor-Project` (default branch `master`). Its README and backend currently describe:

- A React + TypeScript + Vite browser client using React Three Fiber/Three.js and Zustand.
- A FastAPI backend in `backend/app`, with SQLAlchemy persistence. PostgreSQL is configured in Docker Compose; the API can use SQLite when run directly for development.
- A WebSocket project channel at `/ws/projects/{project_id}`.
- A project snapshot endpoint at `GET /projects/{project_id}`. The snapshot contains project `id`, `name`, `source_pressure`, `components`, and `connections`.
- Component create/update/delete and project/simulation operations. The API's component data includes stable IDs, type/name, position/rotation, material, dimensions and simulation properties, ports, plus open/valve state.
- Version-1 event envelopes shaped as `{version, type, project_id, payload}`. The backend currently broadcasts authoritative component and simulation events to sockets connected to the project.

The backend README describes the simulation as deterministic and simplified. It is demonstration output, not validated CFD, water-hammer analysis, or engineering design advice. The research plan also describes ESP32 sensing and mixed-reality clients as future work, not features already implemented in the website repository.

### What the current backend does not yet guarantee

The website README identifies several production/reliability steps still needed before treating the event stream as robust collaboration infrastructure: event sequencing, reconnect snapshots, authentication, and Redis pub/sub fan-out for multi-worker deployment. The present event envelope has a `version`, but the inspected schema does not define a sequence number or revision. The backend's current in-process WebSocket set is suitable for the initial prototype, not proof of durable delivery or multi-instance fan-out. The Unity prototype currently avoids that channel and refreshes complete snapshots over HTTP.

The endpoint paths and schema above were inspected on the repository's default branch. Changes to that branch may alter them; confirm the contract in the website repository before implementing the Unity client. The Unity prototype currently uses the HTTP snapshot endpoint and does not connect to the WebSocket channel.

## Target architecture

```text
Website editor (React / TypeScript)
           │ REST mutations + project events
           ▼
FastAPI service (authoritative project state, simulation, persistence)
           ▲
           │ REST project snapshots (current Unity client polls)
           ▼
Unity shared client layer (DTOs, API client, validation, state mapping)
           │
       ┌───┴────────────┐
       ▼                ▼
HoloLens 2 adapter   XREAL One Pro adapter
tracking/input       host/runtime/input limits
```

The service remains the single authority for shared project data and simulation results. The browser and Unity are clients. Neither client should invent a competing copy of the project's authoritative graph or simulation output. Unity maintains a local rendered cache and refreshes it from periodic backend snapshots; website edits appear at the next poll.

The shared layer should own project IDs, component/port/connection DTOs, units, event parsing, validation, and mapping from domain state to Unity presentation. Device adapters should own tracking origin, spatial placement/alignment, input, comfort, and device build/runtime setup. The visual scene should reference domain objects by stable component ID; Unity GameObject hierarchy and transforms are render state, not the network identity or source of truth.

## Scope and synchronization rules

### Initial read-only vertical slice

Start with the safest end-to-end path:

1. Configure a project ID and API base URL for a development instance.
2. Request `GET /projects/{project_id}` when the app starts.
3. Parse and validate the snapshot, then render components and connections by stable IDs.
4. Poll the snapshot on a configurable interval and support a manual refresh.
5. If a request fails, keep the last valid render and show the error; later polls retry automatically.
6. Keep the local render visible if the service is unavailable, and clearly show that it is last-known server state.

This proves the website-to-Unity path without creating simultaneous writers. The current Unity client follows this snapshot-polling approach. Do not begin by mirroring every website field or by scraping the browser UI.

### Bidirectional edits after read-only sync

Bidirectional edits are deferred. The existing component update route requires a full component object and the project allows arbitrary JSON in `properties`; Unity's built-in JSON serializer cannot preserve that arbitrary object safely. Add a backend PATCH contract for a specific field such as valve `open` before enabling edits from Unity.

Before adding edits, define ownership and conflict behavior. A future ESP32 flow sensor owns measured telemetry fields; users own configuration fields; simulation output is computed by the service. A client edit must not overwrite sensor measurements, and sensor messages must not overwrite design configuration. Use a project revision/sequence and origin metadata when the backend supports them. Until it does, avoid claiming concurrent-edit safety.

### Data format and validation

Use serializable C# DTOs matching the API's JSON contract, separate from Unity MonoBehaviours and rendering classes. Validate required IDs, known component types, finite numeric positions/values, and port/connection references before applying anything. Unknown optional properties are retained as uninterpreted JSON; malformed snapshots must not replace the last valid project state. Keep API schema changes coordinated with the website repository and record compatibility expectations.

Project and simulation state should be distinguished. Project snapshots define geometry/configuration; simulation responses/events define computed pressure, flow, diagnostics, and failures. The website backend's current simulation is synthetic/deterministic demonstration behavior, so show a clear label and do not present it as measured physical telemetry. Later sensor telemetry should carry units, sensor/device identity, capture time, sequence/quality information, and a separate source label.

### HTTP behavior

- Use Unity-supported networking APIs and async flows that do not block rendering/input.
- Make the API base URL configurable per environment; do not hard-code a developer laptop address or production secret.
- For local device tests, remember `localhost` on a headset refers to the headset itself, not the PC running the backend. Use a reachable development host and a network/security setup appropriate to the device.
- Set explicit request timeouts; surface loading, available, offline, and error states.
- Poll at a configurable interval and let the user trigger an immediate refresh.
- Do not put privileged backend credentials in a Unity client build. If authentication is needed, use a suitable user/device authorization flow with short-lived credentials and backend-side authorization.
- Use TLS for network traffic outside a trusted local development setup. CORS is a browser policy and does not secure a native Unity client or replace API authentication.

## XR device plan

### Shared simulation, separate presentation adapters

The same project snapshot, component identities, connections, units, and event semantics should feed both devices. Input and spatial behavior must be selected for the actual device and runtime. Record Unity version, package versions, device firmware/OS, host/accessories, and build settings for every verified run.

### HoloLens 2

First confirm the available HoloLens 2 hardware, OS/runtime, Windows deployment/signing setup, and supported Unity/OpenXR or other chosen toolchain. Then create a minimal target build that launches, renders the shared scene, receives a project snapshot, and supports the intended selection/inspection interaction. Add hand/gaze interaction and spatial mapping/anchors only after confirming the chosen runtime APIs work on the actual device. Keep calibration/relocalization separate from project geometry: a device's transient tracking origin is not a durable project coordinate frame.

### XREAL One Pro

First document the host device, connection mode, available XREAL SDK/runtime, input method, and tracking mode. The research plan notes 3DoF spatial modes and cautions that this does not establish HoloLens-equivalent room-locked 6DoF tracking, spatial mapping, hand tracking, or persistent anchors. If the tested setup only supports a screen-anchored/following view, implement and describe that workflow honestly. If stable room-locked placement is required, investigate a supported host/accessory/tracking arrangement and measure its behavior; do not simulate unsupported capabilities in software and describe them as native tracking.

### Coordinate frames and alignment

Define and document component-local → project/world → device-tracking transforms. Keep the project-to-physical calibration/anchor reference separate from component geometry. Provide visible calibration geometry, a realignment/reset path, and a tracking-loss/relocalization path. Treat QR markers as a candidate aid, not an accuracy guarantee. If registration accuracy is reported, measure translation/rotation error against a surveyed reference over repeated alignment and recovery trials.

## Implementation phases

### Phase 0 — Compatibility and dependency cleanup

1. Resolve the local Bezi Sidekick entries in both `manifest.json` and `packages-lock.json`: determine whether it is required, then replace it with a reproducible package source or remove it if unused.
2. Confirm the exact Unity editor and XR packages/toolchain for each device before installing or changing XR packages.
3. Keep package manifest/lockfile changes intentional and review them in Unity. Avoid adding experimental packages without a clear platform need.
4. Open the current project in the recorded Unity editor and record baseline Console/import/build status before feature work.

### Phase 1 — API contract and development configuration

1. Confirm project snapshot, component/connection shapes, and required network settings against the website's current code.
2. Decide how a Unity session chooses project ID (temporary inspector setting for the prototype; a project picker can follow).
3. Define development/staging API URL configuration without committing secrets or machine-specific LAN addresses.
4. Write representative snapshot and event fixtures for repeatable local checks.

### Phase 2 — Read-only shared-state client

1. Add typed DTOs and schema validation in an isolated Unity C# namespace/folder.
2. Add an HTTP snapshot client with timeouts, periodic polling, and manual refresh.
3. Add a project state model that validates and applies complete snapshots atomically.
4. Add a renderer/mapping layer that creates, updates, and removes Unity views using stable IDs, without embedding networking in individual component prefabs.
5. Show connection and data-source status in the Unity UI.

### Phase 3 — Simulation presentation and first device slice

1. Display server-returned simulation values/diagnostics separately from project configuration and future measured telemetry.
2. Implement a minimal interaction for selecting and inspecting a named component.
3. Build and verify one end-to-end target workflow on hardware, including network reachability from the device.
4. Choose the second device workflow based on actual SDK/host support; preserve the same DTO/state layer.

### Phase 4 — Limited bidirectional edit (blocked on backend contract)

1. Add a safe partial update route for a specific field, preserving all other component values and arbitrary `properties`.
2. Define stale revision, duplicate request, and conflicting update behavior in the service.
3. Send the edit through the API and reconcile from the authoritative response/snapshot.
4. Verify a Unity edit appears in the browser only after server acceptance.

### Phase 5 — Sensor/ESP32 path (later milestone)

Treat sensor acquisition as a separate edge tier. Calibrate the selected flow sensor, define units, sampling interval, quality flags, device identity, timestamps/sequences, queue bounds, and reconnect behavior. Send sensor readings to a validated backend endpoint/channel; have the service own validation, timestamps/revisions, persistence/buffering, and broadcast. Do not stream directly from ESP32 to Unity or conflate sensor readings with synthetic simulation outputs. Measure sensor error and end-to-end latency before making performance claims.

### Phase 6 — Hardening and evaluation

Add backend event sequencing/revision semantics, reconnect snapshots or replay, and authentication as appropriate to the deployment. Consider Redis pub/sub if the WebSocket channel is adopted for multi-process fan-out; Redis is present in Docker Compose, but current broadcasts are process-local. Evaluate snapshot convergence, network loss/recovery, state divergence, device rendering/interaction, and measured performance. Report only measured results with setup and definitions documented.

## Current implementation status

The Unity integration slice now includes:

- Serializable DTOs for the current project snapshot, components, ports, and connections.
- `PipeLabProjectClient`, a read-only HTTP client for `GET /projects/{project_id}` with configurable URL/project ID, request timeout, periodic polling, manual refresh, structural/numeric validation, and status/snapshot events.
- `PipeLabProjectRenderer`, which generates simple component and connection geometry and reconciles it by stable IDs.
- `PipeLabProjectHud`, a runtime status/selection display with desktop mouse selection and `R` refresh shortcut.
The client uses polling instead of WebSockets because the Unity project does not include a WebSocket package and cross-target support has not been verified. It is read-only for now: the website's current full-component PUT contract plus arbitrary `properties` object makes a safe Unity valve-only update impossible with the project's current serializer. This integration has not yet been compiled or run in Unity in this environment. Unity Editor/device build verification is therefore still required before treating the implementation as finished.

### Configure the first snapshot request

1. Start the website backend and get a project ID from `GET /projects` or the browser client.
2. In Unity, open `Assets/Scenes/SampleScene.unity`.
3. In the Hierarchy, right-click an empty area and choose **Create Empty**. Name it `PipeLab Integration`.
4. Select it, choose **Add Component**, and add `PipeLabProjectClient`, `PipeLabProjectRenderer`, and `PipeLabProjectHud`.
5. In the `PipeLabProjectClient` Inspector, set **Api Base Url** to `http://127.0.0.1:8000` for Editor testing on the same computer. Paste the project ID into **Project Id**. Leave **Connect On Start** enabled and keep the refresh interval at 5 seconds for now.
6. On `PipeLabProjectRenderer` and `PipeLabProjectHud`, drag the `PipeLab Integration` GameObject into their **Project Client** reference if it is not filled automatically. The renderer's **Content Root** can stay empty; it will use this GameObject.
7. Enter Play Mode. The status HUD should report a successful load. Click a generated component to select it; press **R** to fetch a snapshot immediately. Website edits appear on the polling interval.
8. In the browser, change a component position or add/remove a component, wait up to 5 seconds, and confirm the Unity geometry matches. A headset must use an API host reachable from the device; `127.0.0.1` on the headset refers to the headset itself.

Current Player Settings include Android. This alone does not configure XREAL device mode or establish a HoloLens build path. Do not change `ProjectVersion.txt` by hand to downgrade from Unity 6; make a backup/branch, open a copy in the required Unity 2022 editor, and resolve migration/package issues there.

## VS Code, Git, and Unity workflow

1. Pull the Unity repository branch. Open the repository root (the folder containing `Assets`, `Packages`, and `ProjectSettings`) in Unity Hub using Unity **6000.6.4f1**.
2. Let Unity resolve packages and import assets before opening the scene or changing code. If package resolution fails, inspect the Console and manifest path; do not delete `Library/` as a substitute for fixing a missing dependency.
3. Use VS Code to edit C# files saved under `Assets/`. Unity performs compilation, serialization, asset import, and builds. A successful VS Code text edit alone does not prove the Unity project compiles.
4. Create/modify scenes, prefabs, and XR project settings through Unity where possible. When Unity creates or changes an asset, commit its `.meta` file with it. Do not hand-edit generated `.csproj` or `.sln` files as the source of truth; regenerate them through Unity's external tools settings when needed.
5. Run the project scene in Unity Play Mode, inspect the Console, then build and verify each device target using its documented toolchain. Desktop Play Mode does not validate device tracking, input, deployment, or device-to-API network access.
6. Review `git status` before commit. Commit source assets, `.meta` files, and intentional `Packages/`/`ProjectSettings/` changes. Do not commit `Library/`, `Temp/`, `Obj/`, build output, local secrets, absolute machine paths, or per-user state.
7. Before merging integration work, clone into a clean directory and open it in Unity to confirm the package setup and asset references work from a normal pull.

### Clean-pull acceptance checklist

- Unity Hub recognizes the repository root as a project and opens it with the recorded editor version.
- All manifest dependencies resolve without a developer-specific absolute path.
- Unity finishes import and compilation with no new Console errors; intended scene opens and runs.
- Stable asset references survive checkout because `.meta` files are tracked.
- No machine-local endpoint, credential, or untracked generated output is required to run the client.
- Device builds are tested on actual hardware; network configuration is reachable from those devices.

## Integration acceptance criteria

- Unity can load a selected backend project snapshot and render the correct component/connection identities.
- Website changes appear in Unity within the polling interval; malformed snapshots do not replace the last valid state.
- Unsupported/malformed payloads do not partially overwrite the last valid state and produce a useful diagnostic.
- Website edits appear in Unity at the polling interval; bidirectional edits remain disabled until the API provides a safe partial update contract.
- Simulation results are identified as backend-generated illustrative values; measured telemetry is separately labeled and carries units/source metadata.
- HoloLens 2 and XREAL One Pro workflows are described according to the actual hardware/runtime capabilities tested, with setup and version details recorded.
- Any latency, frame-rate, or spatial-accuracy claim comes from an explicit measurement method and named test setup.

## Known work remaining

- Replace/remove the machine-specific Bezi package dependency after verifying whether the project uses it.
- Confirm available HoloLens 2 hardware/toolchain and XREAL One Pro host/tracking configuration.
- Add a safe partial update route before enabling bidirectional edits.
- Replace snapshot polling with the website WebSocket channel only after selecting and verifying a Unity WebSocket implementation on both target platforms.
- Implement device-specific XR presentation and eventually the ESP32 telemetry path; these are not present functionality.

## Source repositories

- Unity client: this repository.
- Website/backend: [`PROGRAMIK9/Minor-Project`](https://github.com/PROGRAMIK9/Minor-Project).
- Research and evaluation scope: the separate `RESEARCH_PLAN.md` supplied by the project team.
