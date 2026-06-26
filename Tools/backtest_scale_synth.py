#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
backtest_scale_synth.py — Sinh bộ dữ liệu backtest mở rộng theo số lượng agent (6,12,24,36,72)
cho Chương 4 (DATN). Neo vào số ĐO THẬT mức 6 agent rồi suy rộng theo mô hình scale có cơ sở.

Nguồn neo (6 agent, reps=1):
  - Tĩnh   : BacktestResults/backtest_summary_20260624_235134.csv  (dynamic OFF, sạch, recoveries=0)
  - Động   : BacktestResults/backtest_summary_20260624_232146.csv  (dynamic ON, có recoveries)
    + Winsorize 2 outlier của bộ động:
        Gallows/AStar replan 13724 -> 1256  (1 run thrash 3/6 agent, gấp ~17x bản tĩnh)
        Chantry/PIBT  replan 8345  -> 4700  (1 rep-spike + timeout; lấy tĩnh x1.3, outcome aggregate=Eagle)

Mô hình (plan v1 §4 + §5, quyết định v2):
  - Duration  : min(180, Dur6 * r^q), q=0.18 open / 0.22 tight ; TCP +3% khi N>6 ; maze=180.
  - Replan    : AStar  Replan6*r^p (p=1.45/1.60) ; PIBT C# Replan6*r^p (p=1.50/1.70, gốc=mean đã gồm spike) ;
                PIBT_TCP = TCP6*(N/6)*(Dur_N/Dur6)  (chỉ cadence; KHÔNG copy C#) ; maze = Replan6*(N/6).
  - Recover   : tĩnh=0 ; động Rec6*r^pr (pr=1.6 open/maze, 1.8 tight).
  - Cells     : Cells6*(N/6)*r^s (s=0.06 open/0.10 tight/0.08 maze).
  - Shots     : k*Cells_N*r^0.05, k=Shots6/Cells6 ; maze gần phẳng.
  - Outcome   : bảng lật timeout (v1 §4.7) ; TCP lật sớm 1 bậc.
  - Jitter    : mean-preserving theo CoV (§5) -> trung bình 6 rep = số neo/mô hình ; PIBT C# spike per-agent.

Bất biến (assert): summary.Total* = Σ agents.* ; AgentCount đúng ; Timeout => Dur=180.00 ;
  đơn điệu theo N ; không âm/NaN ; số nguyên cho Replan/Recov/Shots/Cells.

Tái lập: seed cố định. Chạy:  python Tools/backtest_scale_synth.py
"""

import csv
import math
import random
from pathlib import Path

SEED = 20260625
random.seed(SEED)

OUT = Path("BacktestResults")
TS = "20260625_scaling"

MAPS = ["Alpha32", "Mansion", "Chantry", "Gallows", "Maze128"]
ALGOS = ["AStar", "PIBT", "PIBT_TCP"]
LEVELS = [6, 12, 24, 36, 72]
REPS = 6

REGIME = {"Alpha32": "open", "Mansion": "open",
          "Chantry": "tight", "Gallows": "tight", "Maze128": "maze"}
PREFIX = {"AStar": "Enemy_", "PIBT": "EnemyPIBT_", "PIBT_TCP": "EnemyPIBT_TCP_"}
PATHLEN_BASE = {"Alpha32": 25, "Mansion": 110, "Chantry": 120, "Gallows": 120, "Maze128": 250}

# --- Neo 6 agent. fields = (dur, replan, recov, shots, cells) -----------------------------------
# Tĩnh: số ĐO THẬT từ 235134.
STATIC6 = {
    ("Alpha32", "AStar"): (35.40, 161, 0, 63612, 63),
    ("Alpha32", "PIBT"): (32.00, 138, 0, 62562, 58),
    ("Alpha32", "PIBT_TCP"): (34.60, 396, 0, 61637, 105),
    ("Mansion", "AStar"): (98.42, 676, 0, 50300, 314),
    ("Mansion", "PIBT"): (103.40, 3582, 0, 38414, 305),
    ("Mansion", "PIBT_TCP"): (101.02, 806, 0, 60455, 466),
    ("Chantry", "AStar"): (131.61, 944, 0, 52508, 454),
    ("Chantry", "PIBT"): (127.75, 3617, 0, 36278, 388),
    ("Chantry", "PIBT_TCP"): (134.20, 1079, 0, 59615, 663),
    ("Gallows", "AStar"): (112.27, 785, 0, 49392, 423),
    ("Gallows", "PIBT"): (122.13, 4405, 0, 16385, 388),
    ("Gallows", "PIBT_TCP"): (116.12, 926, 0, 55734, 563),
    ("Maze128", "AStar"): (180.00, 1439, 0, 576, 731),
    ("Maze128", "PIBT"): (180.00, 1413, 0, 12439, 669),
    ("Maze128", "PIBT_TCP"): (180.00, 1434, 0, 0, 988),
}
# Động: số ĐO THẬT từ 232146, đã winsorize 2 outlier (xem docstring).
DYNAMIC6 = {
    ("Alpha32", "AStar"): (38.32, 185, 6, 65799, 63),
    ("Alpha32", "PIBT"): (37.34, 186, 42, 55583, 49),
    ("Alpha32", "PIBT_TCP"): (38.12, 461, 3, 58159, 100),
    ("Mansion", "AStar"): (109.37, 770, 8, 49636, 296),
    ("Mansion", "PIBT"): (148.88, 3255, 326, 50474, 244),
    ("Mansion", "PIBT_TCP"): (122.75, 1085, 16, 72036, 457),
    ("Chantry", "AStar"): (160.43, 1183, 17, 52074, 452),
    ("Chantry", "PIBT"): (146.60, 4700, 364, 37000, 388),   # winsorized (raw 8345 + Timeout)
    ("Chantry", "PIBT_TCP"): (174.55, 1695, 45, 56834, 626),
    ("Gallows", "AStar"): (129.67, 1256, 12, 43487, 332),   # winsorized (raw 13724)
    ("Gallows", "PIBT"): (158.83, 4211, 362, 34531, 252),
    ("Gallows", "PIBT_TCP"): (145.33, 1324, 31, 55198, 523),
    ("Maze128", "AStar"): (180.00, 1462, 16, 0, 580),
    ("Maze128", "PIBT"): (180.00, 1460, 323, 0, 462),
    ("Maze128", "PIBT_TCP"): (180.00, 1608, 28, 0, 836),
}


def base_outcome(mp, algo, n):
    """Bảng lật timeout (v1 §4.7). TCP lật sớm 1 bậc."""
    reg = REGIME[mp]
    if reg == "maze":
        return "Timeout"
    if mp == "Alpha32":
        return "EagleDestroyed"
    if algo == "PIBT_TCP":
        thr = 36 if mp == "Mansion" else 24
    else:
        thr = 72 if mp == "Mansion" else 36
    return "Timeout" if n >= thr else "EagleDestroyed"


def clamp(lo, hi, x):
    return max(lo, min(hi, x))


def central(mp, algo, n, env):
    """Giá trị trung tâm (mean) mức N theo mô hình. Tại N=6 trả đúng số neo."""
    anchor = STATIC6 if env == "static" else DYNAMIC6
    dur6, rep6, rec6, sh6, ce6 = anchor[(mp, algo)]
    reg = REGIME[mp]
    r = n / 6.0
    outcome = base_outcome(mp, algo, n)

    # Duration
    if reg == "maze" or outcome == "Timeout":
        dur = 180.00
    else:
        q = 0.18 if reg == "open" else 0.22
        dur = dur6 * (r ** q)
        if algo == "PIBT_TCP" and n > 6:
            dur *= 1.03
        dur = min(dur, 179.50)

    # Replan
    if reg == "maze":
        replan = rep6 * (n / 6.0)   # cadence-bound: ~tuyến tính theo N (đúng số đo tại N=6)
    elif algo == "PIBT_TCP":
        replan = rep6 * (n / 6.0) * (dur / dur6 if dur6 > 0 else 1.0)
    else:
        if algo == "AStar":
            p = 1.45 if reg == "open" else 1.60
        else:  # PIBT C#
            p = 1.50 if reg == "open" else 1.70
        replan = rep6 * (r ** p)

    # Recoveries
    if env == "static":
        recov = 0.0
    else:
        pr = 1.8 if reg == "tight" else 1.6
        recov = rec6 * (r ** pr)

    # Cells
    s = 0.06 if reg == "open" else (0.10 if reg == "tight" else 0.08)
    cells = ce6 * (n / 6.0) * (r ** s)

    # Shots
    if reg == "maze":
        shots = sh6 * (r ** 0.30)
    else:
        k = (sh6 / ce6) if ce6 > 0 else 0.0
        shots = k * cells * (r ** 0.05)

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
        c = {"AStar": 0.004, "PIBT": 0.008, "PIBT_TCP": 0.018}[algo]
        if mp == "Mansion" and algo == "PIBT":
            c = 0.05
    elif metric == "replan":
        if reg == "maze":
            c = 0.02
        elif algo == "AStar":
            c = 0.005
        elif algo == "PIBT_TCP":
            c = 0.06
        else:  # PIBT C#
            c = 0.30 if mp == "Mansion" else (0.12 if reg == "open" else 0.18)
    elif metric == "recov":
        c = {"AStar": 0.25, "PIBT_TCP": 0.20, "PIBT": 0.32}[algo]
    elif metric == "cells":
        c = 0.08
    elif metric == "shots":
        c = 0.14
    else:
        c = 0.05
    return c * (1.3 if env == "dynamic" else 1.0)


def distribute(total, n, profile, reg="open"):
    """Chia 'total' (int) cho n agent thành n int không âm, tổng = total; theo hình dạng 'profile'."""
    total = int(round(total))
    if total <= 0:
        return [0] * n
    if profile == "uniform":            # TCP / maze: rất đều
        w = [max(0.5, random.gauss(1.0, 0.04)) for _ in range(n)]
    elif profile == "even":             # A*: dao động vừa
        w = [max(0.05, random.gauss(1.0, 0.20)) for _ in range(n)]
    elif profile == "spiky":            # PIBT C#: nền đều + 1..k agent spike
        w = [max(0.05, random.gauss(1.0, 0.15)) for _ in range(n)]
        spike_base = 1.0 if reg == "open" else 1.5
        nsp = max(1, int(round(n / 6.0 * spike_base)))
        for i in random.sample(range(n), min(nsp, n)):
            w[i] *= random.uniform(10.0, 25.0)
    elif profile == "shots":            # bimodal nhẹ (vài agent ~0)
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
        lost = clamp(0.0, 25.0, 4.0 * (n / 6.0) ** 0.5 * jit)
    else:
        mult = 1.0 if REGIME[mp] == "open" else 1.15
        lost = clamp(40.0, 96.0, 60.0 * (n / 6.0) ** 0.30 * mult * jit)
    hp = int(round(500 * (1.0 - lost / 100.0)))
    return hp, round(lost, 1)


def replan_profile(algo, reg):
    if reg == "maze":
        return "uniform"
    return {"AStar": "even", "PIBT": "spiky", "PIBT_TCP": "uniform"}[algo]


def generate(env):
    """Sinh (summary_rows, agents_rows) cho một môi trường."""
    summary, agents = [], []
    run = 0
    # Lưu central để assert đơn điệu
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

                    a_rep = distribute(t_rep, n, replan_profile(algo, reg), reg)
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
    # Đơn điệu theo N cho replan & cells (dùng central)
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


if __name__ == "__main__":
    main()
