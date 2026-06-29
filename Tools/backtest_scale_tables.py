#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""In thân bảng LaTeX cho Chương 4 từ pivot scaling (đảm bảo số khớp dữ liệu)."""
import csv
from pathlib import Path

BR = Path("BacktestResults")
MAPS = ["Alpha32", "Mansion", "Chantry", "Gallows", "Maze128"]
LEVELS = [6, 12, 24, 36, 72]
ALGO_LBL = {"AStar": "A*", "PIBT": "PIBT C\\#", "PIBT_TCP": "PIBT C++/TCP"}


def rd(p):
    return list(csv.DictReader(open(p, encoding="utf-8")))


BYMAP = {(r["Environment"], r["Map"], int(r["AgentCount"]), r["Algorithm"]): r
         for r in rd(BR / "report_scaling_by_map.csv")}
BYAC = {(r["Environment"], int(r["AgentCount"]), r["Algorithm"]): r
        for r in rd(BR / "report_scaling_by_agentcount.csv")}


def vi(x):
    """số nguyên, ngăn cách nghìn bằng \\,"""
    n = int(round(float(x)))
    s = f"{n:,}".replace(",", "\\,")
    return s


def vd(x):
    """1 chữ số thập phân, dấu phẩy"""
    return f"{float(x):.1f}".replace(".", ",")


def outcome_vi(o):
    return "EagleDestroyed" if o == "EagleDestroyed" else "Timeout"


def baseline(env, cols_recov):
    out = []
    for mp in MAPS:
        for algo in ("AStar", "PIBT", "PIBT_TCP"):
            r = BYMAP[(env, mp, 6, algo)]
            cells = vi(r["CellsMean"])
            row = f"{mp:<8} & {ALGO_LBL[algo]:<12} & {vd(r['DurationMean'])} & {vi(r['ReplanMean'])} & "
            if cols_recov:
                row += f"{vi(r['RecovMean'])} & "
            row += f"{cells} & {outcome_vi(r['Outcome'])} \\\\"
            out.append(row)
        out.append("\\hline")
    return "\n".join(out)


def scaling_agg(env, recov):
    out = []
    for n in LEVELS:
        for algo in ("AStar", "PIBT", "PIBT_TCP"):
            r = BYAC[(env, n, algo)]
            last = vi(r["RecovMean"]) if recov else vi(r["CellsMean"])
            out.append(f"{n:>2} & {ALGO_LBL[algo]:<12} & {vd(r['SuccessRatePct'])} & "
                       f"{vd(r['DurationMean'])} & {vi(r['ReplanMean'])} & {last} \\\\")
        out.append("\\hline")
    return "\n".join(out)


def detail_static():
    out = []
    for mp in MAPS:
        for n in LEVELS:
            cells = []
            for algo in ("AStar", "PIBT", "PIBT_TCP"):
                r = BYMAP[("static", mp, n, algo)]
                v = vi(r["ReplanMean"])
                if r["Outcome"] == "Timeout":
                    v += "$^\\dagger$"
                cells.append(v)
            mp_cell = mp if n == 6 else ""
            out.append(f"{mp_cell:<8} & {n:>2} & {cells[0]} & {cells[1]} & {cells[2]} \\\\")
        out.append("\\hline")
    return "\n".join(out)


print("% ===== tab:static_results (6 agent, static) =====")
print(baseline("static", cols_recov=False))
print("\n% ===== tab:dynamic_results (6 agent, dynamic, +Recov) =====")
print(baseline("dynamic", cols_recov=True))
print("\n% ===== tab:scaling_results (static aggregate) =====")
print(scaling_agg("static", recov=False))
print("\n% ===== tab:scaling_dynamic (dynamic aggregate, +Recov) =====")
print(scaling_agg("dynamic", recov=True))
print("\n% ===== tab:scaling_detail (static replan per map) =====")
print(detail_static())
