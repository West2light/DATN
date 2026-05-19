---
name: MCP editor state goes stale during Play Mode
description: During Play Mode, mcpforunity://editor/state freezes at its pre-play sequence number and marks itself stale; use execute_code to query live runtime state instead
type: feedback
---

During Play Mode, `mcpforunity://editor/state` returns `is_stale=true`, `sequence` frozen at pre-play value, `phase=playmode_transition` indefinitely. Calling `refresh_unity` returns "Refresh recovered after Unity disconnect/retry" but the state snapshot still doesn't update.

**Why:** The MCP bridge loses its stateful connection during domain reload on Play Mode entry. The HTTP transport reconnects but the state cache is not refreshed.

**How to apply:**
- Do NOT rely on editor/state resource fields during Play Mode (isPlaying, phase, etc.)
- Use `execute_code` with `UnityEngine.Application.isPlaying`, `UnityEngine.Time.time`, `UnityEngine.Time.frameCount` to confirm Play Mode status live.
- Use `execute_code` + reflection to query MonoBehaviour field values at runtime.
- **CRITICAL:** `transform.position` read via `execute_code` also returns a frozen/stale value (t=0.02s cached). Always read `tankMover.rb2d.position` for the live physics position in Play Mode — `transform.position` is unreliable in MCP during Play Mode.
- **CRITICAL:** `GridEnemyAgent` is placed on the ROOT enemy GO but `Rigidbody2D` lives on the `EnemyTank` CHILD. `agent.GetComponent<Rigidbody2D>()` returns null. Use `agent.GetComponentInChildren<Rigidbody2D>()` to get the live physics body and its `.position` / `.linearVelocity` for runtime observation.
- **Agent root transform is frozen during Play Mode** — `agent.transform.position` via MCP returns the spawn position for the whole session. Same for `execute_code` querying `agent`-root fields. Only child-component queries (`GetComponentInChildren`) return live values.
- `read_console` still works during Play Mode.
- `manage_camera screenshot` still works during Play Mode.
- Call `refresh_unity(wait_for_ready=true)` after exiting Play Mode before querying editor state again.
