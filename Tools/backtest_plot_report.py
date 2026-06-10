#!/usr/bin/env python3
import base64
import csv
import html
import sys
from collections import OrderedDict, defaultdict
from datetime import datetime
from pathlib import Path

try:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
except ImportError:
    matplotlib = None
    plt = None


METRICS = [
    ("Duration_s", "Thoi gian TB (s)", True),
    ("TotalReplans", "Replan tong", False),
    ("TotalShots", "Tong so shot", False),
    ("TotalCells", "Cells da di", False),
]


def parse_float(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def read_summary(csv_path):
    maps = OrderedDict()
    sums = defaultdict(lambda: [0.0] * len(METRICS))
    counts = defaultdict(lambda: [0] * len(METRICS))

    with open(csv_path, newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        for row in reader:
            map_name = row.get("Map", "")
            algo = row.get("Algorithm", "")
            if not map_name or not algo:
                continue

            maps.setdefault(map_name, None)
            key = (map_name, algo)
            for idx, (field, _, _) in enumerate(METRICS):
                sums[key][idx] += parse_float(row.get(field))
                counts[key][idx] += 1

    return list(maps.keys()), sums, counts


def avg(sums, counts, map_name, algo, metric_idx):
    key = (map_name, algo)
    count = counts[key][metric_idx]
    return sums[key][metric_idx] / count if count else 0.0


def fmt_value(value):
    return f"{value:.0f}" if value >= 100 else f"{value:.1f}"


def plot_png(maps, sums, counts, png_path):
    if plt is None:
        raise RuntimeError("matplotlib is not installed. Install it with: python3 -m pip install matplotlib")

    fig, axes = plt.subplots(2, 2, figsize=(14, 8), constrained_layout=True)
    fig.patch.set_facecolor("#0e1014")
    axes = axes.flatten()

    x = list(range(len(maps)))
    bar_width = 0.36
    colors = {"AStar": "#4a96ff", "PIBT": "#ff8c24"}

    for metric_idx, (_, label, _) in enumerate(METRICS):
        ax = axes[metric_idx]
        ax.set_facecolor("#161820")

        astar_vals = [avg(sums, counts, m, "AStar", metric_idx) for m in maps]
        pibt_vals = [avg(sums, counts, m, "PIBT", metric_idx) for m in maps]

        ax.bar([i - bar_width / 2 for i in x], astar_vals, bar_width, label="A*", color=colors["AStar"])
        ax.bar([i + bar_width / 2 for i in x], pibt_vals, bar_width, label="PIBT", color=colors["PIBT"])

        max_val = max(astar_vals + pibt_vals + [1.0])
        ax.set_ylim(0, max_val * 1.22)
        ax.set_title(label, color="#d0d8e8", fontsize=12, fontweight="bold")
        ax.set_xticks(x)
        ax.set_xticklabels(maps, rotation=20, ha="right", color="#8a93a8")
        ax.tick_params(axis="y", colors="#8a93a8")
        ax.grid(axis="y", color="#ffffff", alpha=0.08, linewidth=0.8)
        for spine in ax.spines.values():
            spine.set_color("#2a2d38")

        for offset, values in [(-bar_width / 2, astar_vals), (bar_width / 2, pibt_vals)]:
            for i, value in enumerate(values):
                if value <= 0:
                    continue
                ax.text(
                    i + offset,
                    value + max_val * 0.025,
                    fmt_value(value),
                    ha="center",
                    va="bottom",
                    color="#c0c8d8",
                    fontsize=8,
                )

    handles, labels = axes[0].get_legend_handles_labels()
    fig.legend(handles, labels, loc="upper center", ncol=2, frameon=False, labelcolor="#d0d8e8")
    fig.suptitle("Backtest Report - A* vs PIBT", color="#f5d050", fontsize=18, fontweight="bold")
    fig.savefig(png_path, dpi=160, facecolor=fig.get_facecolor())
    plt.close(fig)


def build_html(maps, sums, counts, csv_path, png_path):
    image_data = base64.b64encode(Path(png_path).read_bytes()).decode("ascii")
    generated_at = datetime.now().strftime("%d/%m/%Y %H:%M")

    metric_headers = "".join(f"<th>{html.escape(label)}</th>" for _, label, _ in METRICS)
    rows = []
    for map_name in maps:
        for algo in ("AStar", "PIBT"):
            cells = []
            for metric_idx, (_, _, _) in enumerate(METRICS):
                cells.append(f"<td>{fmt_value(avg(sums, counts, map_name, algo, metric_idx))}</td>")
            rows.append(
                "<tr>"
                f"<td>{html.escape(map_name)}</td>"
                f"<td>{html.escape(algo)}</td>"
                + "".join(cells)
                + "</tr>"
            )

    return f"""<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="UTF-8">
  <title>Backtest Report - A* vs PIBT</title>
  <style>
    *{{box-sizing:border-box}}
    body{{margin:0;background:#0e1014;color:#d0d8e8;font-family:Segoe UI,Arial,sans-serif;padding:32px}}
    h1{{color:#f5d050;font-size:24px;margin:0 0 6px}}
    .subtitle{{color:#8a93a8;font-size:13px;margin:0 0 24px}}
    .panel{{background:#161820;border-radius:8px;padding:24px;margin-bottom:24px}}
    img{{display:block;width:100%;max-width:1500px;height:auto}}
    h2{{font-size:14px;color:#8a93a8;text-transform:uppercase;letter-spacing:.05em;margin:0 0 16px}}
    table{{width:100%;border-collapse:collapse;font-size:12px}}
    th{{background:#1e2128;color:#8a93a8;padding:8px 12px;text-align:left}}
    td{{padding:7px 12px;border-bottom:1px solid #1e2128;color:#c0c8d8}}
  </style>
</head>
<body>
  <h1>Backtest Report - A* vs PIBT</h1>
  <p class="subtitle">Ngay chay: {generated_at} &bull; CSV: {html.escape(Path(csv_path).name)} &bull; {len(maps)} map(s)</p>
  <div class="panel"><img alt="Backtest chart" src="data:image/png;base64,{image_data}"></div>
  <div class="panel">
    <h2>Bang tong ket trung binh</h2>
    <table>
      <tr><th>Map</th><th>Thuat toan</th>{metric_headers}</tr>
      {''.join(rows)}
    </table>
  </div>
</body>
</html>
"""


def main(argv):
    if len(argv) != 4:
        print("Usage: backtest_plot_report.py summary.csv report.html chart.png", file=sys.stderr)
        return 2

    csv_path = Path(argv[1])
    html_path = Path(argv[2])
    png_path = Path(argv[3])

    maps, sums, counts = read_summary(csv_path)
    if not maps:
        print("No rows found in summary CSV.", file=sys.stderr)
        return 1

    try:
        plot_png(maps, sums, counts, png_path)
    except RuntimeError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    html_path.write_text(build_html(maps, sums, counts, csv_path, png_path), encoding="utf-8")
    print(f"Wrote {png_path.name} and {html_path.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
