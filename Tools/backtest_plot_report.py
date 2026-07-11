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
    ("Duration_s", "Average time (s)", True),
    ("TotalReplans", "Total replans", False),
    ("TotalShots", "Total shots", False),
    ("TotalCells", "Cells traveled", False),
    ("EagleHP", "Final Eagle HP", False),
]

ALGOS = [
    ("AStar", "A*", "#4a96ff"),
    ("PIBT", "PIBT", "#ff8c24"),
    ("PIBT_TCP", "PIBT-C++", "#66d98c"),
    ("Mixed", "Mixed", "#b07cff"),
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


# CSS class tô đậm/đổi màu ô thắng trong bảng (khớp màu ALGOS).
WIN_CLASS = {"AStar": "win-a", "PIBT": "win-p", "PIBT_TCP": "win-t", "Mixed": "win-m"}


def best_algo(sums, counts, map_name, metric_idx, lower_better):
    """Thuật toán tốt nhất cho (map, metric): min nếu lower_better, ngược lại max.
    Bỏ qua giá trị <= 0 (không có dữ liệu)."""
    best, best_val = None, None
    for algo, _, _ in ALGOS:
        v = avg(sums, counts, map_name, algo, metric_idx)
        if v <= 0:
            continue
        if best is None or (v < best_val if lower_better else v > best_val):
            best, best_val = algo, v
    return best


def fmt_value(value):
    return f"{value:.0f}" if value >= 100 else f"{value:.1f}"


def plot_png(maps, sums, counts, png_path):
    if plt is None:
        raise RuntimeError("matplotlib is not installed. Install it with: python3 -m pip install matplotlib")

    ncols = 3
    nrows = (len(METRICS) + ncols - 1) // ncols
    fig, axes = plt.subplots(nrows, ncols, figsize=(6 * ncols, 4 * nrows), constrained_layout=True)
    fig.patch.set_facecolor("#0e1014")
    axes = axes.flatten()
    # Ẩn các ô subplot dư (khi số metric không lấp đầy lưới)
    for j in range(len(METRICS), len(axes)):
        axes[j].set_visible(False)

    x = list(range(len(maps)))
    # Chia đều N cột thuật toán quanh mỗi vạch map (tự co theo số ALGOS).
    n_algos = len(ALGOS)
    group_span = 0.82
    bar_gap = 0.02
    bar_width = max(0.08, (group_span - (n_algos - 1) * bar_gap) / n_algos)
    offsets = [(a - (n_algos - 1) / 2.0) * (bar_width + bar_gap) for a in range(n_algos)]

    for metric_idx, (_, label, lower_better) in enumerate(METRICS):
        ax = axes[metric_idx]
        ax.set_facecolor("#161820")

        values_by_algo = [
            [avg(sums, counts, m, algo, metric_idx) for m in maps]
            for algo, _, _ in ALGOS
        ]

        # Thuật toán tốt nhất mỗi map (để tô viền trắng nổi bật cột thắng).
        best_per_map = []
        for mi in range(len(maps)):
            col = [(a, values_by_algo[a][mi]) for a in range(len(ALGOS)) if values_by_algo[a][mi] > 0]
            if col:
                pick = min(col, key=lambda t: t[1]) if lower_better else max(col, key=lambda t: t[1])
                best_per_map.append(pick[0])
            else:
                best_per_map.append(-1)

        for a, ((algo, display, color), values, offset) in enumerate(zip(ALGOS, values_by_algo, offsets)):
            edgecolors = ["#ffffff" if best_per_map[mi] == a else "none" for mi in range(len(maps))]
            ax.bar([i + offset for i in x], values, bar_width, label=display,
                   color=color, edgecolor=edgecolors, linewidth=1.6, zorder=3)

        all_values = [v for values in values_by_algo for v in values]
        max_val = max(all_values + [1.0])
        ax.set_ylim(0, max_val * 1.22)
        ax.set_title(label, color="#d0d8e8", fontsize=12, fontweight="bold")
        ax.set_xticks(x)
        ax.set_xticklabels(maps, rotation=20, ha="right", color="#8a93a8")
        ax.tick_params(axis="y", colors="#8a93a8")
        ax.grid(axis="y", color="#ffffff", alpha=0.08, linewidth=0.8)
        for spine in ax.spines.values():
            spine.set_color("#2a2d38")

        for offset, values in zip(offsets, values_by_algo):
            for i, value in enumerate(values):
                if value <= 0:
                    continue
                ax.text(
                    i + offset,
                    value + max_val * 0.03,
                    fmt_value(value),
                    ha="center",
                    va="bottom",
                    color="#c0c8d8",
                    fontsize=7,
                    rotation=90,
                )

    handles, labels = axes[0].get_legend_handles_labels()
    fig.suptitle("Backtest Report - A* vs PIBT vs PIBT-C++ vs Mixed", color="#f5d050", fontsize=18, fontweight="bold")
    # Legend đặt NGOÀI lưới subplot (phía dưới) để không chồng chéo tiêu đề/cột.
    # "outside" locs cần constrained_layout (đã bật ở subplots()).
    fig.legend(handles, labels, loc="outside lower center", ncol=len(ALGOS), frameon=False, labelcolor="#d0d8e8")
    fig.savefig(png_path, dpi=160, facecolor=fig.get_facecolor())
    plt.close(fig)


def build_html(maps, sums, counts, csv_path, png_path):
    image_data = base64.b64encode(Path(png_path).read_bytes()).decode("ascii")
    generated_at = datetime.now().strftime("%d/%m/%Y %H:%M")

    metric_headers = "".join(f"<th>{html.escape(label)}</th>" for _, label, _ in METRICS)
    rows = []
    for map_name in maps:
        for algo, display, _ in ALGOS:
            cells = []
            for metric_idx, (_, _, lower_better) in enumerate(METRICS):
                v = avg(sums, counts, map_name, algo, metric_idx)
                cls = WIN_CLASS[algo] if algo == best_algo(sums, counts, map_name, metric_idx, lower_better) else ""
                cells.append(f'<td class="{cls}">{fmt_value(v)}</td>')
            rows.append(
                "<tr>"
                f"<td>{html.escape(map_name)}</td>"
                f"<td>{html.escape(display)}</td>"
                + "".join(cells)
                + "</tr>"
            )

    return f"""<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="UTF-8">
  <title>Backtest Report - A* vs PIBT vs PIBT-C++ vs Mixed</title>
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
    .win-a{{color:#4a96ff;font-weight:700}}
    .win-p{{color:#ff8c24;font-weight:700}}
    .win-t{{color:#66d98c;font-weight:700}}
    .win-m{{color:#b07cff;font-weight:700}}
  </style>
</head>
<body>
  <h1>Backtest Report - A* vs PIBT vs PIBT-C++ vs Mixed</h1>
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
