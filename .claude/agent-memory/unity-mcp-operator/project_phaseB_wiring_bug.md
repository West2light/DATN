---
name: Phase B wiring — tankClearanceRadius/navMask — RESOLVED
description: Phase B GridEnemyAgent wiring was confirmed broken on 2026-05-10 (t=105s sample), then fixed by mapf-gameplay-engineer and re-verified on 2026-05-10.
type: project
---

## Original bug (2026-05-10, t~105s sample — RESOLVED)

MapScenarioBootstrap.tankClearanceRadius = 0.4 in Inspector, but all 4 spawned GridEnemyAgent
instances showed tankClearanceRadius=0 and navMask=null via reflection. Root cause was that the
AddGridEnemyAgent method was sampled BEFORE the wiring lines executed (timing race), or against a
stale DLL. mapf-gameplay-engineer confirmed wiring was already present at lines 180-181 of
MapScenarioBootstrap.cs and added two diagnostic logs to surface any true null case.

## Verification run (2026-05-10, re-verified)

Pre-flight: isCompiling=false, scene=MapF_TankTest.unity, Editor not in Play Mode, console empty.

Console logs after entering Play Mode (~4s wait):
  [MapScenarioBootstrap] Configured Enemy_1: navMask=OK, clearance=0.40
  [MapScenarioBootstrap] Configured Enemy_2: navMask=OK, clearance=0.40
  [MapScenarioBootstrap] Configured Enemy_3: navMask=OK, clearance=0.40
  [MapScenarioBootstrap] Configured Enemy_4: navMask=OK, clearance=0.40

Reflection sample via execute_code (codedom, post-spawn):
  Agent count: 4
  Enemy_4: navMask=OK, clearance=0.4
  Enemy_3: navMask=OK, clearance=0.4
  Enemy_1: navMask=OK, clearance=0.4
  Enemy_2: navMask=OK, clearance=0.4

No LogError "navMask is null" fired. Zero errors/warnings in full session.

Screenshot: Assets/Screenshots/phaseB_verified_play_t13.png (map active, enemies moving).

**Why this matters:** Phase A (GridEnemyAgent A* path following) and Phase B (swept-capsule clearance
via HasClearance / SmoothPath) are both active at runtime. The diagnostic logs (TEMP markers) can
now be removed by mapf-gameplay-engineer.

**How to apply:** Treat Phase A + B as wiring-confirmed. Next work is Milestone 3 metrics
instrumentation per mapf_eagle_enemy_astar_plan.md.

## P1+P2+P3 combined fix verification (2026-05-11, v4.2 post-fix)

enemySpawnCells[2] changed from (30,30) to (28,28) [P3].
P1 (GetBaseInflateRadius clamp): CONFIRMED — exactly 1 "inflate radius 1" log in 30s.
P2 (scuff reset removal): CONFIRMED — escalation monotonic: reached ExpandedMask level, no downgrade.
Enemy_3 scuff count in 30s: ~151 (approx 2.5 Hz rate, firing every 0.40s).
Enemy_3 final recoveryLevel=ExpandedMask, dist to Eagle=9.82 at t=30s — still STUCK.
Enemy_1/2/4: dist 4.57–4.81 at t=30s, recoveryLevel=None — reached Eagle zone successfully.

Remaining issue: ExpandedMask replanning every 0.40s means the 0.40s scuff threshold is being
hit every single cycle. Enemy_3 is in a local dead-end where even ExpandedMask path replanning
finds the same blocked route. This is a deeper stuck pattern than P1+P2+P3 address.
Next: mapf-gameplay-engineer to investigate why ExpandedMask path still fails every 0.40s
for Enemy_3 at spawn cell (28,28) corner area.
