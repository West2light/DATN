# Hướng dẫn biên dịch quyển ĐATN (`DoAn.pdf`)

Tài liệu này mô tả cách biên dịch quyển báo cáo ra `DoAn.pdf`, công cụ cần cài, và cách
tái tạo hình vẽ (diagram mermaid, biểu đồ backtest).

> Quan trọng: quyển này **bắt buộc biên dịch bằng XeLaTeX**, KHÔNG dùng pdfLaTeX.
> Lý do: dùng `fontspec` + font hệ thống **Times New Roman** để hiển thị Unicode tiếng
> Việt đầy đủ. Biên dịch bằng pdfLaTeX sẽ lỗi font.

---

## 1. Yêu cầu môi trường

| Thành phần | Bắt buộc | Dùng để | Ghi chú |
| --- | --- | --- | --- |
| MiKTeX (có `xelatex`, `bibtex`) | Có | Biên dịch PDF | Windows; lần đầu MiKTeX tự tải gói còn thiếu (cần mạng) |
| Font **Times New Roman** | Có | `\setmainfont` | Windows có sẵn |
| Node.js + `npx` | Không* | Export lại diagram mermaid | *Chỉ khi sửa file `.mmd` |
| Python 3 + `matplotlib` | Không* | Vẽ lại biểu đồ backtest | *Chỉ khi sửa số liệu CSV |

Các file PDF hình vẽ đã được export sẵn trong `Hinhve/`, nên **chỉ cần MiKTeX là biên
dịch được** quyển. Node/Python chỉ cần khi muốn tạo lại hình.

---

## 2. Lệnh biên dịch chính

Quyển dùng `biblatex`/`bibtex` cho tài liệu tham khảo và có nhiều tham chiếu chéo
(mục lục, danh mục hình/bảng, `\ref` tới phụ lục), nên cần chạy nhiều lượt.

### Cách 1 — Chạy tay (PowerShell hoặc Git Bash), tại thư mục quyển

```bash
xelatex -interaction=nonstopmode DoAn.tex
bibtex DoAn
xelatex -interaction=nonstopmode DoAn.tex
xelatex -interaction=nonstopmode DoAn.tex
```

Bốn lượt: lượt 1 sinh `.aux`, `bibtex` sinh thư mục tài liệu, hai lượt cuối để ổn định
mọi số trang/tham chiếu. Kết quả: `DoAn.pdf`.

Trong PowerShell (5.1) **không dùng `&&`** để nối lệnh — phải dùng `;`:

```powershell
xelatex -interaction=nonstopmode DoAn.tex; bibtex DoAn; xelatex -interaction=nonstopmode DoAn.tex; xelatex -interaction=nonstopmode DoAn.tex
```

### Cách 2 — Dùng script kèm theo (khuyến nghị)

```powershell
# PowerShell
.\build.ps1
# Nếu bị chặn execution policy:
powershell -ExecutionPolicy Bypass -File .\build.ps1
```
```bash
# Git Bash
./build.sh
```

### Cách 3 — `latexmk` (CHỈ khi đã cài Perl)

```bash
latexmk -xelatex -interaction=nonstopmode DoAn.tex
```

> Lưu ý: MiKTeX **không kèm Perl**, nên `latexmk` sẽ báo lỗi
> *"could not find the script engine 'perl'"*. Muốn dùng cách này phải cài Perl
> (ví dụ Strawberry Perl). Nếu không, dùng Cách 1 hoặc Cách 2 — cả hai không cần Perl.

---

## 3. Cấu hình VS Code (LaTeX Workshop)

Người dùng đọc PDF bằng tiện ích **LaTeX Workshop**. Để recipe mặc định dùng XeLaTeX,
thêm vào `.vscode/settings.json` (hoặc settings người dùng):

```jsonc
{
  "latex-workshop.latex.tools": [
    {
      "name": "xelatex",
      "command": "xelatex",
      "args": ["-synctex=1", "-interaction=nonstopmode", "-file-line-error", "%DOC%"]
    },
    { "name": "bibtex", "command": "bibtex", "args": ["%DOCFILE%"] }
  ],
  "latex-workshop.latex.recipes": [
    {
      "name": "xelatex ➜ bibtex ➜ xelatex×2",
      "tools": ["xelatex", "bibtex", "xelatex", "xelatex"]
    }
  ],
  "latex-workshop.latex.recipe.default": "first"
}
```

Sau đó bấm nút **Build LaTeX project** (hoặc `Ctrl+Alt+B`), xem PDF bằng
**View LaTeX PDF** (`Ctrl+Alt+V`).

---

## 4. Tái tạo hình vẽ (chỉ khi cần)

### 4.1. Diagram mermaid (class diagram, các sequence diagram)

Nguồn: `../../PLAN/R2_DIAGRAMS_V2/mermaid/*.mmd`. Export cần Chromium cho puppeteer.

```bash
# (1) Cài Chromium cho puppeteer (một lần) — in ra đường dẫn chrome-headless-shell.exe
npx -y puppeteer browsers install chrome-headless-shell

# (2) Tạo file cấu hình puppeteer (thay <PATH> bằng đường dẫn ở bước 1)
cd ../../PLAN/R2_DIAGRAMS_V2/mermaid
echo '{ "executablePath": "<PATH>/chrome-headless-shell.exe", "args": ["--no-sandbox"] }' > puppeteer-config.json

# (3) Export tất cả .mmd ra PDF
for f in *.mmd; do
  npx -y @mermaid-js/mermaid-cli -i "$f" -o "../export/pdf/${f%.mmd}.pdf" -p puppeteer-config.json -b transparent
done
```

Sau đó copy sang `Hinhve/` đúng tên đang dùng trong quyển:

| File nguồn | Tên trong `Hinhve/` |
| --- | --- |
| `08_pathfinding_class_diagram.pdf` | `class_pathfinding.pdf` |
| `09_astar_replan_sequence.pdf` | `seq_astar_replan.pdf` |
| `10_pibt_csharp_sequence.pdf` | `seq_pibt_csharp.pdf` |
| `11_pibt_tcp_epibt_sequence.pdf` | `seq_pibt_tcp.pdf` |
| `05_internet_create_room_sequence.pdf` | `seq_lan_create_room.pdf` |
| `06_internet_ready_start_sequence.pdf` | `seq_lan_ready_start.pdf` |

Diagram drawio (`02_architecture_packages`, `07_mapf_data_flow`, `13_backtest_workflow`)
đã có sẵn PDF trong `R2_DIAGRAMS_V2/export/pdf/`, chỉ cần copy nếu sửa.

### 4.2. Biểu đồ backtest (3 hình `backtest_*`)

Nguồn số liệu: `../../../BacktestResults/backtest_summary_*.csv`. Vẽ bằng Python:

```bash
pip install matplotlib
# Chạy script vẽ (sinh PNG+PDF vào Hinhve/): xem Tools/ hoặc plan R3
```

Các hình hiện tại: `backtest_duration_static`, `backtest_replan_static`,
`backtest_static_vs_dynamic` (cả `.pdf` và `.png`).

---

## 5. Cấu trúc thư mục quyển

```
20225808_DuongQuangDong_2025.2/
├─ DoAn.tex                 # file gốc (preamble + ghép chương)
├─ Bia.tex, Bia_lot.tex     # bìa
├─ Tu_viet_tat.tex          # danh mục thuật ngữ/viết tắt
├─ Danh_sach_tai_lieu_tham_khao.bib
├─ Chuong/
│   ├─ 0_2..0_4             # lời cảm ơn, tóm tắt, abstract
│   ├─ 1_Gioi_thieu .. 6_Ket_luan
│   └─ Phu_luc_A, Phu_luc_B
├─ Hinhve/                  # tất cả hình (.pdf ưu tiên cho vector)
└─ DoAn.pdf                 # kết quả biên dịch
```

---

## 6. Lỗi thường gặp

| Triệu chứng | Nguyên nhân | Cách xử lý |
| --- | --- | --- |
| `Missing character: There is no ... in font` | Biên dịch bằng pdfLaTeX | Dùng **xelatex** |
| `Font "Times New Roman" not found` | Thiếu font hệ thống | Cài font, hoặc đổi `\setmainfont` sang font khác (ví dụ `TeX Gyre Termes`) |
| Caption hiện "Figure"/"Table", header "CHAPTER" | Thiếu Việt hóa nhãn | Đã xử lý bằng `\renewcommand{\figurename}{Hình}` v.v. trong `DoAn.tex` |
| Tham chiếu hiện `??`, mục lục sai số trang | Chưa chạy đủ lượt | Chạy lại đủ chuỗi `xelatex → bibtex → xelatex → xelatex` |
| `Could not find Chrome` khi export mermaid | Puppeteer thiếu Chromium | Chạy bước 4.1(1) và trỏ `puppeteer-config.json` |

---

## 7. Dọn file trung gian

```bash
# Git Bash
rm -f DoAn.aux DoAn.log DoAn.out DoAn.toc DoAn.lof DoAn.lot DoAn.bbl DoAn.blg \
      DoAn.bcf DoAn.run.xml DoAn.glo DoAn.gls DoAn.glg DoAn.acn DoAn.acr DoAn.alg \
      DoAn.synctex.gz Chuong/*.aux
```
