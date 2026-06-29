# Biên dịch quyển ĐATN ra DoAn.pdf bằng XeLaTeX (MiKTeX).
# Cách chạy:  ./build.ps1        (tại thư mục chứa DoAn.tex)
# Yêu cầu:    MiKTeX (xelatex, bibtex) trong PATH; font Times New Roman.

$ErrorActionPreference = "Continue"
$doc = "DoAn"

if (-not (Test-Path "$doc.tex")) {
    Write-Host "Khong tim thay $doc.tex. Hay chay script trong thu muc chua quyen bao cao." -ForegroundColor Red
    exit 1
}

Write-Host "[1/4] xelatex (pass 1)..." -ForegroundColor Cyan
xelatex -interaction=nonstopmode "$doc.tex" | Out-Null

Write-Host "[2/4] bibtex..." -ForegroundColor Cyan
bibtex $doc | Out-Null

Write-Host "[3/4] xelatex (pass 2)..." -ForegroundColor Cyan
xelatex -interaction=nonstopmode "$doc.tex" | Out-Null

Write-Host "[4/4] xelatex (pass 3)..." -ForegroundColor Cyan
xelatex -interaction=nonstopmode "$doc.tex" | Out-Null

# Bao cao ket qua
if (Test-Path "$doc.pdf") {
    $size = "{0:N0} KB" -f ((Get-Item "$doc.pdf").Length / 1KB)
    Write-Host "`nOK: $doc.pdf ($size)" -ForegroundColor Green
    $err = Select-String -Path "$doc.log" -Pattern '^!' -ErrorAction SilentlyContinue
    if ($err) {
        Write-Host "Co loi trong log:" -ForegroundColor Yellow
        $err | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.Line)" }
    } else {
        Write-Host "Khong co loi (! ) trong log." -ForegroundColor Green
    }
} else {
    Write-Host "`nBIEN DICH THAT BAI: khong sinh duoc $doc.pdf" -ForegroundColor Red
    Select-String -Path "$doc.log" -Pattern '^!' | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.Line)" }
    exit 1
}
