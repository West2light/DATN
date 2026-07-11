#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
backtest_scale_plots.py — Vẽ 7 biểu đồ cho Chương 4 từ bộ dữ liệu scaling đã sinh.
Đọc pivot trong BacktestResults/, xuất PNG (300 DPI, nền trắng) vào Hinhve/.

Baseline 6 agent (theo bản đồ): duration, replan(log), static-vs-dynamic.
Trục scale (theo số agent): duration, replan(log), timeout%, recoveries(log, động).

Chạy:  python Tools/backtest_scale_plots.py
"""
import csv
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

HINH = Path("adds/report/20225808_DuongQuangDong_2025.2/Hinhve")
BR = Path("BacktestResults")

MAPS = ["Alpha32", "Mansion", "Chantry", "Gallows", "Maze128"]
LEVELS = [6, 12, 24, 36, 72]
# (key, nhãn, màu, marker)
ALGOS = [("AStar", "A*", "#2a6fdb", "o"),
         ("PIBT", "PIBT C#", "#e8820e", "s"),
         ("PIBT_TCP", "PIBT C++/TCP", "#2e9e4f", "^")]


def rd(p):
    return list(csv.DictReader(open(p, encoding="utf-8")))


BYMAP = rd(BR / "report_scaling_by_map.csv")
BYAC = rd(BR / "report_scaling_by_agentcount.csv")


def mget(env, mp, n, algo, field):
    for r in BYMAP:
        if (r["Environment"] == env and r["Map"] == mp
                and int(r["AgentCount"]) == n and r["Algorithm"] == algo):
            return float(r[field])
    return 0.0


def aget(env, n, algo, field):
    for r in BYAC:
        if r["Environment"] == env and int(r["AgentCount"]) == n and r["Algorithm"] == algo:
            return float(r[field])
    return 0.0


plt.rcParams.update({"font.size": 11, "font.family": "DejaVu Sans",
                     "axes.titlesize": 12, "figure.dpi": 300})


def style_ax(ax, grid_axis="y"):
    ax.set_facecolor("white")
    ax.grid(axis=grid_axis, color="#cccccc", alpha=0.7, linewidth=0.7)
    ax.spines["top"].set_visible(False)
    ax.spines["right"].set_visible(False)
    for s in ("left", "bottom"):
        ax.spines[s].set_color("#888")


def fmt(v):
    return f"{v:.0f}" if v >= 100 else (f"{v:.1f}" if v >= 1 else f"{v:.2f}")


def grouped_by_map(field, title, ylabel, fname, env="static", n=6, logy=False):
    fig, ax = plt.subplots(figsize=(8.2, 4.8))
    x = range(len(MAPS))
    bw = 0.26
    maxv = 1.0
    for j, (a, lbl, col, _) in enumerate(ALGOS):
        vals = [mget(env, mp, n, a, field) for mp in MAPS]
        maxv = max(maxv, max(vals))
        ax.bar([i + (j - 1) * bw for i in x], vals, bw, label=lbl, color=col, zorder=3)
        for i, v in zip(x, vals):
            if v > 0:
                ax.text(i + (j - 1) * bw, v * (1.02 if logy else 1.0) + (0 if logy else maxv * 0.01),
                        fmt(v), ha="center", va="bottom", fontsize=7, color="#333", zorder=4)
    if logy:
        ax.set_yscale("log")
        ax.set_ylim(1, maxv * 2.2)
    else:
        ax.set_ylim(0, maxv * 1.18)
    ax.set_xticks(list(x))
    ax.set_xticklabels(MAPS, rotation=12)
    ax.set_ylabel(ylabel)
    ax.set_title(title, fontweight="bold")
    style_ax(ax)
    ax.legend(frameon=False, ncol=3, loc="upper left", fontsize=9)
    fig.tight_layout()
    fig.savefig(HINH / fname)
    plt.close(fig)
    print("wrote", fname)


def static_vs_dynamic(fname):
    fig, ax = plt.subplots(figsize=(7.6, 4.8))
    x = range(len(ALGOS))
    bw = 0.36
    sv = [aget("static", 6, a, "ReplanMean") for a, _, _, _ in ALGOS]
    dv = [aget("dynamic", 6, a, "ReplanMean") for a, _, _, _ in ALGOS]
    ax.bar([i - bw / 2 for i in x], sv, bw, label="Môi trường tĩnh", color="#7aa6d6", zorder=3)
    ax.bar([i + bw / 2 for i in x], dv, bw, label="Có chướng ngại động", color="#d6857a", zorder=3)
    for i, (s, d) in enumerate(zip(sv, dv)):
        ax.text(i - bw / 2, s, fmt(s), ha="center", va="bottom", fontsize=8, color="#333")
        ax.text(i + bw / 2, d, fmt(d), ha="center", va="bottom", fontsize=8, color="#333")
    ax.set_xticks(list(x))
    ax.set_xticklabels([lbl for _, lbl, _, _ in ALGOS])
    ax.set_ylabel("Số lần tái hoạch định trung bình")
    ax.set_ylim(0, max(sv + dv) * 1.18)
    ax.set_title("So sánh số lần tái hoạch định: tĩnh vs động (6 agent)", fontweight="bold")
    style_ax(ax)
    ax.legend(frameon=False, fontsize=9)
    fig.tight_layout()
    fig.savefig(HINH / fname)
    plt.close(fig)
    print("wrote", fname)


def line_vs_agents(field, env, title, ylabel, fname, logy=False, pct=False):
    fig, ax = plt.subplots(figsize=(8.2, 4.8))
    for a, lbl, col, mk in ALGOS:
        ys = [aget(env, n, a, field) for n in LEVELS]
        # tách nhẹ 2 đường trùng (A*/PIBT cùng bảng timeout) cho dễ nhìn
        off = 0.0
        if pct and a == "PIBT":
            ys = [y + 1.2 for y in ys]
        ax.plot(LEVELS, ys, marker=mk, color=col, label=lbl, linewidth=2.2,
                markersize=7, zorder=3)
    if logy:
        ax.set_yscale("log")
    if pct:
        ax.set_ylim(-5, 105)
    ax.set_xticks(LEVELS)
    ax.set_xlabel("Số lượng agent")
    ax.set_ylabel(ylabel)
    ax.set_title(title, fontweight="bold")
    style_ax(ax, grid_axis="both")
    ax.legend(frameon=False, fontsize=9, loc="best")
    fig.tight_layout()
    fig.savefig(HINH / fname)
    plt.close(fig)
    print("wrote", fname)


def main():
    HINH.mkdir(parents=True, exist_ok=True)
    # Baseline 6 agent theo bản đồ
    grouped_by_map("DurationMean",
                   "Thời gian hoàn thành trung bình theo bản đồ (6 agent, môi trường tĩnh)",
                   "Thời gian (s)", "backtest_duration_static.png")
    grouped_by_map("ReplanMean",
                   "Số lần tái hoạch định trung bình theo bản đồ (6 agent, thang log)",
                   "Số lần replan (log)", "backtest_replan_static.png", logy=True)
    static_vs_dynamic("backtest_static_vs_dynamic.png")
    # Trục scale theo số agent
    line_vs_agents("DurationMean", "static",
                   "Thời gian hoàn thành theo số lượng agent (môi trường tĩnh)",
                   "Thời gian trung bình (s)", "backtest_duration_vs_agents.png")
    line_vs_agents("ReplanMean", "static",
                   "Tổng số lần tái hoạch định theo số lượng agent (thang log)",
                   "Số lần replan trung bình (log)", "backtest_replan_vs_agents.png", logy=True)
    line_vs_agents("TimeoutRatePct", "static",
                   "Tỉ lệ timeout theo số lượng agent (môi trường tĩnh)",
                   "Tỉ lệ timeout (%)", "backtest_timeout_vs_agents.png", pct=True)
    line_vs_agents("RecovMean", "dynamic",
                   "Số lần phục hồi sau kẹt theo số lượng agent (môi trường động, thang log)",
                   "Số lần phục hồi trung bình (log)", "backtest_recoveries_vs_agents.png", logy=True)
    print("All charts written to", HINH)


if __name__ == "__main__":
    main()
