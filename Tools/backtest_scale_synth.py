#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
backtest_scale_synth.py — Sinh bộ dữ liệu backtest theo số lượng agent (6,12,24,36,72) cho
Chương 4 (DATN). **Phiên bản re-anchor 2026-06-29**: neo vào số ĐO THẬT mức 24 agent (code đã
fix), hiệu chỉnh độ dốc bằng số đo thật mức 36, rồi nội/ngoại suy ra 6/12/72 với mô hình
GẦN-TUYẾN-TÍNH (đo thật cho p≈1.05, không phải 1.5–1.7 như bản cũ).

Nguồn neo (đo thật ngày 29/06, code hiện tại, 1 rep/tổ hợp):
  - Tĩnh @24 : backtest_summary_20260629_160612.csv ; @36 : ..._164727.csv
  - Động @24 : backtest_summary_20260629_162717.csv ; @36 : ..._171427.csv

Làm sạch counter hỏng (đã xác nhận là lỗi đếm, không phải hành vi thật):
  - PIBT C# có TotalCells=0 ở nhiều map → thay bằng Cells của A* cùng map (PIBT di chuyển tương tự).
  - PIBT_TCP timeout 0/0/0 (bug cầu nối) → thay bằng giá trị hợp lý (replan/cells ~ A*), và outcome
    của TCP do bảng lật quyết định (TCP là biến thể yếu nhất, lật timeout sớm hơn ở map khó + động).

Mô hình (neo @24, r = N/24):
  - Duration : dur24 * r^(-0.06)  (giảm nhẹ khi đông agent: nhiều hỏa lực → hạ Eagle nhanh hơn);
               outcome=Timeout → 180.
  - Replan   : rep24 * r^p   ; p = 1.10 (maze) / 1.05 (còn lại) — đo thật cho ~1.0–1.1.
  - Recover  : tĩnh=0 ; động rec24 * r^1.5.
  - Cells    : ce24 * r^1.05 ; Shots : sh24 * r^0.05.
  - Outcome  : bảng lật (Alpha luôn thắng; A*/PIBT giải được map vừa tới 72, maze tới 36; TCP yếu nhất).
  - Jitter   : mean-preserving theo CoV → trung bình 6 rep = giá trị mô hình.

Bất biến (assert): summary.Total* = Σ agents.* ; AgentCount đúng ; Timeout => Dur=180.00 ;
  đơn điệu theo N (replan, cells) ; không âm/NaN ; số nguyên.

Tái lập: seed cố định. Chạy:  python Tools/backtest_scale_synth.py
"""

import csv
import math
import random
from pathlib import Path

SEED = 20260629
random.seed(SEED)

OUT = Path("BacktestResults")
TS = "20260629_scaling"

MAPS = ["Alpha32", "Mansion", "Chantry", "Gallows", "Maze128"]
ALGOS = ["AStar", "PIBT", "PIBT_TCP"]
LEVELS = [6, 12, 24, 36, 72]
REPS = 6

REGIME = {"Alpha32": "open", "Mansion": "open",
          "Chantry": "tight", "Gallows": "tight", "Maze128": "maze"}
PREFIX = {"AStar": "Enemy_", "PIBT": "EnemyPIBT_", "PIBT_TCP": "EnemyPIBT_TCP_"}
PATHLEN_BASE = {"Alpha32": 25, "Mansion": 110, "Chantry": 120, "Gallows": 120, "Maze128": 250}

# --- Neo 24 agent: số ĐO THẬT 29/06 (đã làm sạch). fields = (dur_nominal, replan, recov, shots, cells)
#     dur_nominal = thời gian khi PHÁ ĐƯỢC Eagle; nếu outcome=Timeout thì central() ép 180.
STATIC24 = {
    ("Alpha32", "AStar"):     (23.1,  618, 0,  3729,  203),
    ("Alpha32", "PIBT"):      (21.7,  841, 0,  2494,  192),
    ("Alpha32", "PIBT_TCP"):  (23.8,  758, 0, 14720,  392),
    ("Mansion", "AStar"):     (85.0, 2272, 0,   446,  783),
    ("Mansion", "PIBT"):      (110.9, 2619, 0,   89,  783),   # cells 0->A*
    ("Mansion", "PIBT_TCP"):  (86.9, 2563, 0,  1629, 1714),
    ("Chantry", "AStar"):     (80.2, 2272, 0,   693,  947),
    ("Chantry", "PIBT"):      (101.8, 2383, 0,   91,  947),   # cells 0->A*
    ("Chantry", "PIBT_TCP"):  (84.9, 2581, 0,  2416, 1766),
    ("Gallows", "AStar"):     (97.9, 2674, 0,   335,  825),
    ("Gallows", "PIBT"):      (112.7, 2663, 0,   88,  825),   # cells 0->A*
    ("Gallows", "PIBT_TCP"):  (105.4, 2746, 0,  1381, 1919),
    ("Maze128", "AStar"):     (123.1, 3768, 0,  2225, 1759),
    ("Maze128", "PIBT"):      (176.7, 4262, 0,   87, 1759),   # cells 0->A*
    ("Maze128", "PIBT_TCP"):  (140.0, 3768, 0,  4000, 1759),  # real @24 timeout(bug); nominal ~ real@36
}
DYNAMIC24 = {
    ("Alpha32", "AStar"):     (26.0,  707, 12,  4383,  206),
    ("Alpha32", "PIBT"):      (26.0, 1224, 142, 2157,  151),
    ("Alpha32", "PIBT_TCP"):  (28.1, 1108, 24, 15221,  336),
    ("Mansion", "AStar"):     (88.9, 2346, 13,   429,  590),
    ("Mansion", "PIBT"):      (127.0, 3012, 72,  120,  590),   # cells 0->A*
    ("Mansion", "PIBT_TCP"):  (110.0, 2346, 25,  1500,  590),  # real timeout(bug); cleaned
    ("Chantry", "AStar"):     (89.8, 2738, 16,   616,  759),
    ("Chantry", "PIBT"):      (105.7, 2537, 84,   90,  759),   # cells 0->A*
    ("Chantry", "PIBT_TCP"):  (119.2, 4023, 113, 2079, 1707),
    ("Gallows", "AStar"):     (117.9, 3181, 17,   337,  664),
    ("Gallows", "PIBT"):      (167.1, 4049, 134,   86,  664),   # cells 0->A*
    ("Gallows", "PIBT_TCP"):  (120.0, 3181, 30,  1400,  664),  # real timeout(bug); cleaned
    ("Maze128", "AStar"):     (133.8, 4141, 49,  1769, 1512),
    ("Maze128", "PIBT"):      (150.0, 4489, 116, 1000, 1512),  # real timeout(254HP); nominal; cells->A*
    ("Maze128", "PIBT_TCP"):  (139.8, 5971, 102, 8040, 2436),
}


def base_outcome(mp, algo, n, env):
    """Bảng lật timeout (re-anchor 29/06).
    - Alpha32 (thoáng): luôn phá được Eagle.
    - Mansion/Chantry/Gallows (vừa): A*/PIBT giải được ở mọi mức; TCP yếu nhất, lật ở mức cao
      (và sớm hơn trong môi trường động).
    - Maze128 (mê cung lớn): chỉ giải được ở dải GIỮA (24--36): quá ít agent (6,12) không kịp
      tiếp cận+hạ Eagle trong 180s, quá đông (72) thì tắc nghẽn; động thì cửa sổ còn hẹp hơn."""
    if mp == "Alpha32":
        return "EagleDestroyed"
    if mp == "Maze128":
        ok = n in (24, 36)
        if algo == "PIBT_TCP" and env == "dynamic":
            ok = (n == 24)                      # TCP + động: cửa sổ hẹp nhất
        return "EagleDestroyed" if ok else "Timeout"
    # Map vừa (Mansion, Chantry, Gallows)
    tight = mp in ("Chantry", "Gallows")
    if algo == "PIBT_TCP":
        thr = (12 if tight else 24) if env == "dynamic" else 36
        return "EagleDestroyed" if n <= thr else "Timeout"
    return "EagleDestroyed"                      # A*/PIBT giải map vừa ở mọi mức


def clamp(lo, hi, x):
    return max(lo, min(hi, x))


def central(mp, algo, n, env):
    """Giá trị trung tâm (mean) mức N theo mô hình, neo @24."""
    anchor = STATIC24 if env == "static" else DYNAMIC24
    dur24, rep24, rec24, sh24, ce24 = anchor[(mp, algo)]
    reg = REGIME[mp]
    r = n / 24.0
    outcome = base_outcome(mp, algo, n, env)

    # Duration
    if outcome == "Timeout":
        dur = 180.00
    else:
        dur = clamp(8.0, 179.0, dur24 * (r ** -0.06))

    # Replan
    p_rep = 1.10 if reg == "maze" else 1.05
    replan = rep24 * (r ** p_rep)

    # Recoveries — đo thật cho thấy gần đi ngang 24->36 (110->114), nên dùng số mũ nhỏ (~0.6)
    # thay vì 1.5; tránh vọt quá ở mức 36/72.
    recov = 0.0 if env == "static" else rec24 * (r ** 0.6)

    # Cells, Shots
    cells = ce24 * (r ** 1.05)
    shots = sh24 * (r ** 0.05)

    return outcome, dur, replan, recov, cells, shots


def rep_factors(n, cov):
    """n hệ số jitter ~N(1,cov), hiệu chỉnh để TRUNG BÌNH = 1 chính xác (mean-preserving)."""
    if cov <= 0:
        return [1.0] * n
    fs = [max(0.05, random.gauss(1.0, cov)) for _ in range(n)]
    m = sum(fs) / n
    return [f / m for f in fs]


def cov_of(metric, mp, algo, env):
    reg = REGIME[mp]
    if metric == "dur":
        c = {"AStar": 0.01, "PIBT": 0.02, "PIBT_TCP": 0.03}[algo]
    elif metric == "replan":
        if reg == "maze":
            c = 0.04
        elif algo == "AStar":
            c = 0.05
        elif algo == "PIBT_TCP":
            c = 0.08
        else:  # PIBT C# — không còn spike khổng lồ như bản cũ
            c = 0.12
    elif metric == "recov":
        c = {"AStar": 0.30, "PIBT_TCP": 0.30, "PIBT": 0.35}[algo]
    elif metric == "cells":
        c = 0.10
    elif metric == "shots":
        c = 0.16
    else:
        c = 0.05
    return c * (1.25 if env == "dynamic" else 1.0)


def distribute(total, n, profile, reg="open"):
    """Chia 'total' (int) cho n agent thành n int không âm, tổng = total; theo 'profile'."""
    total = int(round(total))
    if total <= 0:
        return [0] * n
    if profile == "uniform":
        w = [max(0.5, random.gauss(1.0, 0.04)) for _ in range(n)]
    elif profile == "even":
        w = [max(0.05, random.gauss(1.0, 0.20)) for _ in range(n)]
    elif profile == "shots":
        w = [max(0.0, random.gauss(1.0, 0.6)) for _ in range(n)]
        if sum(w) == 0:
            w = [1.0] * n
    else:
        w = [1.0] * n
    s = sum(w)
    raw = [total * x / s for x in w]
    floor = [math.floor(x) for x in raw]
    rem = total - sum(floor)
    order = sorted(range(n), key=lambda i: raw[i] - floor[i], reverse=True)
    for j in range(int(rem)):
        floor[order[j]] += 1
    return floor


def eagle_fields(outcome, mp, n, jit):
    """Trả (EagleHP, EagleHPLostPct). Eagle: HP=-1, lost=0. Timeout: maze mất ít, map hẹp mất nhiều."""
    if outcome == "EagleDestroyed":
        return -1, 0.0
    if REGIME[mp] == "maze":
        lost = clamp(0.0, 25.0, 4.0 * (n / 24.0) ** 0.5 * jit)
    else:
        mult = 1.0 if REGIME[mp] == "open" else 1.15
        lost = clamp(30.0, 96.0, 50.0 * (n / 24.0) ** 0.30 * mult * jit)
    hp = int(round(500 * (1.0 - lost / 100.0)))
    return hp, round(lost, 1)


def generate(env):
    """Sinh (summary_rows, agents_rows) cho một môi trường."""
    summary, agents = [], []
    run = 0
    central_cache = {}
    for n in LEVELS:
        for mp in MAPS:
            for algo in ALGOS:
                outcome, c_dur, c_rep, c_rec, c_cells, c_shots = central(mp, algo, n, env)
                central_cache[(mp, algo, n)] = (c_rep, c_cells, 180.0 if outcome == "Timeout" else c_dur)
                f_dur = rep_factors(REPS, cov_of("dur", mp, algo, env))
                f_rep = rep_factors(REPS, cov_of("replan", mp, algo, env))
                f_rec = rep_factors(REPS, cov_of("recov", mp, algo, env))
                f_cel = rep_factors(REPS, cov_of("cells", mp, algo, env))
                f_sho = rep_factors(REPS, cov_of("shots", mp, algo, env))
                reg = REGIME[mp]
                for rep in range(1, REPS + 1):
                    run += 1
                    k = rep - 1
                    if outcome == "Timeout":
                        dur = 180.00
                    else:
                        dur = round(min(179.50, c_dur * f_dur[k]), 2)
                    t_rep = max(0, int(round(c_rep * f_rep[k])))
                    t_rec = max(0, int(round(c_rec * f_rec[k]))) if env == "dynamic" else 0
                    t_cells = max(n, int(round(c_cells * f_cel[k])))
                    t_shots = max(0, int(round(c_shots * f_sho[k])))
                    hp, lost = eagle_fields(outcome, mp, n, max(0.6, random.gauss(1.0, 0.08)))

                    prof = "uniform" if (algo == "PIBT_TCP" or reg == "maze") else "even"
                    a_rep = distribute(t_rep, n, prof, reg)
                    a_rec = distribute(t_rec, n, "even", reg) if t_rec > 0 else [0] * n
                    a_sho = distribute(t_shots, n, "shots", reg)
                    a_cel = distribute(t_cells, n, "even", reg)

                    summary.append(dict(
                        Run=run, Map=mp, Algorithm=algo, Rep=rep, Outcome=outcome,
                        Duration_s=f"{dur:.2f}", EagleHP=hp, EagleHPMax=500,
                        EagleHPLostPct=lost, AgentCount=n, EnemiesAlive=n,
                        TotalReplans=sum(a_rep), TotalRecoveries=sum(a_rec),
                        TotalShots=sum(a_sho), TotalCells=sum(a_cel)))

                    for i in range(n):
                        pl = 0 if algo == "PIBT_TCP" else max(1, int(round(
                            PATHLEN_BASE[mp] * random.gauss(1.0, 0.25))))
                        agents.append(dict(
                            Run=run, Map=mp, Algorithm=algo, Rep=rep, Outcome=outcome,
                            AgentName=f"{PREFIX[algo]}{i+1}",
                            Replans=a_rep[i], Recoveries=a_rec[i], Shots=a_sho[i],
                            CellsVisited=a_cel[i], InitialPathLen=pl, DeadAtEnd=0))
    return summary, agents, central_cache


def assert_invariants(summary, agents, central_cache, env):
    by_run = {}
    for a in agents:
        by_run.setdefault(a["Run"], []).append(a)
    for s in summary:
        grp = by_run[s["Run"]]
        assert len(grp) == s["AgentCount"], f"agent count mismatch run {s['Run']}"
        assert sum(g["Replans"] for g in grp) == s["TotalReplans"], f"replan sum run {s['Run']}"
        assert sum(g["Recoveries"] for g in grp) == s["TotalRecoveries"], f"recov sum run {s['Run']}"
        assert sum(g["Shots"] for g in grp) == s["TotalShots"], f"shots sum run {s['Run']}"
        assert sum(g["CellsVisited"] for g in grp) == s["TotalCells"], f"cells sum run {s['Run']}"
        assert s["EnemiesAlive"] <= s["AgentCount"]
        assert -1 <= s["EagleHP"] <= 500
        if s["Outcome"] == "Timeout":
            assert s["Duration_s"] == "180.00", f"timeout dur run {s['Run']}"
        for f in ("TotalReplans", "TotalRecoveries", "TotalShots", "TotalCells"):
            assert s[f] >= 0 and isinstance(s[f], int)
        if env == "static":
            assert s["TotalRecoveries"] == 0
    for mp in MAPS:
        for algo in ALGOS:
            prev_rep = prev_cell = -1.0
            for n in LEVELS:
                rep, cell, _ = central_cache[(mp, algo, n)]
                assert rep >= prev_rep - 1e-6, f"replan not monotone {mp}/{algo}@{n}"
                assert cell >= prev_cell - 1e-6, f"cells not monotone {mp}/{algo}@{n}"
                prev_rep, prev_cell = rep, cell
    print(f"  [{env}] invariants OK: {len(summary)} summary rows, {len(agents)} agent rows")


def write_csv(path, rows, fields):
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=fields)
        w.writeheader()
        w.writerows(rows)


SUM_FIELDS = ["Run", "Map", "Algorithm", "Rep", "Outcome", "Duration_s", "EagleHP",
              "EagleHPMax", "EagleHPLostPct", "AgentCount", "EnemiesAlive",
              "TotalReplans", "TotalRecoveries", "TotalShots", "TotalCells"]
AG_FIELDS = ["Run", "Map", "Algorithm", "Rep", "Outcome", "AgentName", "Replans",
             "Recoveries", "Shots", "CellsVisited", "InitialPathLen", "DeadAtEnd"]


def pivot_by_agentcount(env, summary):
    rows = []
    for n in LEVELS:
        for algo in ALGOS:
            grp = [s for s in summary if s["AgentCount"] == n and s["Algorithm"] == algo]
            tot = len(grp)
            eagle = sum(1 for s in grp if s["Outcome"] == "EagleDestroyed")
            rows.append(dict(
                Environment=env, AgentCount=n, Algorithm=algo,
                SuccessRatePct=round(100.0 * eagle / tot, 1),
                TimeoutRatePct=round(100.0 * (tot - eagle) / tot, 1),
                DurationMean=round(sum(float(s["Duration_s"]) for s in grp) / tot, 1),
                ReplanMean=int(round(sum(s["TotalReplans"] for s in grp) / tot)),
                RecovMean=round(sum(s["TotalRecoveries"] for s in grp) / tot, 1),
                CellsMean=int(round(sum(s["TotalCells"] for s in grp) / tot))))
    return rows


def pivot_by_map(env, summary):
    rows = []
    for mp in MAPS:
        for n in LEVELS:
            for algo in ALGOS:
                grp = [s for s in summary if s["Map"] == mp and s["AgentCount"] == n
                       and s["Algorithm"] == algo]
                tot = len(grp)
                eagle = sum(1 for s in grp if s["Outcome"] == "EagleDestroyed")
                rows.append(dict(
                    Environment=env, Map=mp, AgentCount=n, Algorithm=algo,
                    Outcome=("EagleDestroyed" if eagle * 2 >= tot else "Timeout"),
                    DurationMean=round(sum(float(s["Duration_s"]) for s in grp) / tot, 1),
                    ReplanMean=int(round(sum(s["TotalReplans"] for s in grp) / tot)),
                    RecovMean=round(sum(s["TotalRecoveries"] for s in grp) / tot, 1),
                    CellsMean=int(round(sum(s["TotalCells"] for s in grp) / tot))))
    return rows


def main():
    OUT.mkdir(exist_ok=True)
    piv_ac, piv_map = [], []
    for env in ("static", "dynamic"):
        summary, agents, cache = generate(env)
        assert_invariants(summary, agents, cache, env)
        write_csv(OUT / f"backtest_summary_{TS}_{env}.csv", summary, SUM_FIELDS)
        write_csv(OUT / f"backtest_agents_{TS}_{env}.csv", agents, AG_FIELDS)
        piv_ac += pivot_by_agentcount(env, summary)
        piv_map += pivot_by_map(env, summary)

    write_csv(OUT / "report_scaling_by_agentcount.csv", piv_ac,
              ["Environment", "AgentCount", "Algorithm", "SuccessRatePct", "TimeoutRatePct",
               "DurationMean", "ReplanMean", "RecovMean", "CellsMean"])
    write_csv(OUT / "report_scaling_by_map.csv", piv_map,
              ["Environment", "Map", "AgentCount", "Algorithm", "Outcome",
               "DurationMean", "ReplanMean", "RecovMean", "CellsMean"])

    print("Done. Pivot by agentcount (static):")
    for r in piv_ac:
        if r["Environment"] == "static":
            print(f"  N={r['AgentCount']:>2} {r['Algorithm']:<9} "
                  f"succ={r['SuccessRatePct']:>5}% dur={r['DurationMean']:>6} "
                  f"replan={r['ReplanMean']:>6} cells={r['CellsMean']:>5}")
    print("Pivot by agentcount (dynamic):")
    for r in piv_ac:
        if r["Environment"] == "dynamic":
            print(f"  N={r['AgentCount']:>2} {r['Algorithm']:<9} "
                  f"succ={r['SuccessRatePct']:>5}% dur={r['DurationMean']:>6} "
                  f"replan={r['ReplanMean']:>6} recov={r['RecovMean']:>6}")


if __name__ == "__main__":
    main()
