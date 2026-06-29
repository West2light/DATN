#!/usr/bin/env bash
# Biên dịch quyển ĐATN ra DoAn.pdf bằng XeLaTeX (MiKTeX).
# Cách chạy:  ./build.sh        (tại thư mục chứa DoAn.tex)
# Yêu cầu:    xelatex, bibtex trong PATH; font Times New Roman.

set -u
DOC=DoAn

if [ ! -f "$DOC.tex" ]; then
  echo "Khong tim thay $DOC.tex. Hay chay script trong thu muc chua quyen bao cao."
  exit 1
fi

echo "[1/4] xelatex (pass 1)..."
xelatex -interaction=nonstopmode "$DOC.tex" > /dev/null 2>&1
echo "[2/4] bibtex..."
bibtex "$DOC" > /dev/null 2>&1 || true
echo "[3/4] xelatex (pass 2)..."
xelatex -interaction=nonstopmode "$DOC.tex" > /dev/null 2>&1
echo "[4/4] xelatex (pass 3)..."
xelatex -interaction=nonstopmode "$DOC.tex" > /dev/null 2>&1

if [ -f "$DOC.pdf" ]; then
  echo ""
  echo "OK: $DOC.pdf ($(du -h "$DOC.pdf" | cut -f1))"
  if grep -q '^!' "$DOC.log"; then
    echo "Co loi trong log:"
    grep '^!' "$DOC.log" | head -10
  else
    echo "Khong co loi (!) trong log."
  fi
else
  echo ""
  echo "BIEN DICH THAT BAI: khong sinh duoc $DOC.pdf"
  grep '^!' "$DOC.log" | head -10
  exit 1
fi
