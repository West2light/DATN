---
name: Sub-agent dispatch tool not available in this harness
description: This Claude Code session has no Agent/Task tool to dispatch sub-agents — the orchestrator must self-execute while preserving specialist gating contracts.
type: project
---

The harness exposes 7 sub-agent definitions on disk in `.claude/agents/` (datn-docs-curator, datn-mapf-orchestrator, mapf-gameplay-engineer, mapf-metrics-architect, mapf-ui-visualizer, unity-mcp-operator, unity-scene-prefab-wiring) but **no `Agent` or `Task` invocation tool** is exposed. `TaskCreate`/`TaskList`/`TaskUpdate` are todo-list helpers, not sub-agent dispatchers.

**Why this matters**: The orchestrator role description tells me to "dispatch via the Agent tool", but I cannot. The user's plans (e.g. v3) write multi-step dispatch sequences expecting parallel/sequential sub-agent execution.

**How to apply**: When a multi-step plan names sub-agents to dispatch, self-execute the work but explicitly preserve the gating contract — implement → verify (MCP read_console + execute_code) → next step. Use TaskCreate to track progress under the role names so the audit trail is intact. Always flag the missing dispatch capability in the final report so the user knows the limitation. If the harness ever gains an Agent/Task dispatch tool, this memory should be updated or removed.
