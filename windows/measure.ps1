<#
.SYNOPSIS
    Measures an installed Convert, to compare builds: download and installed size, how long it takes to
    start, to hand a file to an open window, and to convert a short video.

.DESCRIPTION
    Each time is the median of several runs, after one run to warm up. Convert.exe is a windowed program,
    so every run goes through Start-Process -Wait, which waits for it to end. The results are printed and
    appended to -Results, so CI can show the measurements of several builds side by side at the end.

    "Start" runs Convert.exe --cli --targets <file>: it starts the app and works out which formats the
    window would offer for the file, which is what happens before the window appears.
    "Hand over" runs Convert.exe --open <file> while another Convert window (in report mode, so nothing
    is shown) is gathering a selection: what each extra selected file costs in the classic menu.
    "Convert a 1 s video" runs --cli mp4-h264 on a small clip into a folder of its own.

.EXAMPLE
    .\measure.ps1 -Label "This build" -Setup out\Convert-0.5.1-setup.exe
#>
param(
    [Parameter(Mandatory)] [string]$Label,
    [Parameter(Mandatory)] [string]$Setup,
    [string]$Dir = (Join-Path $env:LOCALAPPDATA "Programs\Convert"),
    [string]$Results = (Join-Path ([IO.Path]::GetTempPath()) "convert-measurements.txt")
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $Dir "Convert.exe"
$ffmpeg = Join-Path $Dir "tools\ffmpeg.exe"
$work = Join-Path ([IO.Path]::GetTempPath()) "convert-measure-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
$report = [Collections.Generic.List[string]]::new()
function Say([string]$text) { Write-Host $text; $report.Add($text) }

function Quote([string]$path) { '"' + $path + '"' }

function Run([string[]]$arguments) {
    $null = Start-Process $exe -ArgumentList $arguments -Wait -NoNewWindow `
        -RedirectStandardOutput (Join-Path $work "out.txt") -RedirectStandardError (Join-Path $work "err.txt")
}

function Median([string]$name, [scriptblock]$run, [int]$runs = 7) {
    & $run   # warm up: the first start also reads the program from disk
    $times = @(1..$runs | ForEach-Object { (Measure-Command { & $run }).TotalMilliseconds } | Sort-Object)
    $all = ($times | ForEach-Object { "{0:N0}" -f $_ }) -join ", "
    Say ("{0,-28} {1,8:N0} ms   (all: {2})" -f $name, $times[[int][Math]::Floor($runs / 2)], $all)
}

Say "== $Label"
Say ("{0,-28} {1,8:N1} MB" -f "Setup program", ((Get-Item $Setup).Length / 1MB))
Say ("{0,-28} {1,8:N1} MB" -f "Installed", ((Get-ChildItem $Dir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))

$mp3 = Join-Path $work "a.mp3"
$png = Join-Path $work "b.png"
$clip = Join-Path $work "c.mp4"
& $ffmpeg -v error -f lavfi -i "sine=duration=1" $mp3
& $ffmpeg -v error -f lavfi -i "testsrc2=size=64x64" -frames:v 1 $png
& $ffmpeg -v error -f lavfi -i "testsrc2=size=320x240:rate=30:duration=1" -pix_fmt yuv420p $clip

Median "Start (an MP3)" { Run @("--cli", "--targets", (Quote $mp3)) }
Median "Start (a photo)" { Run @("--cli", "--targets", (Quote $png)) }

# A window gathering a selection, in report mode so it shows nothing; then the files that join it.
$env:CONVERT_OPEN_REPORT = Join-Path $work "gathered.txt"
try {
    $first = Start-Process $exe -ArgumentList @("--open", (Quote $mp3)) -PassThru
    Start-Sleep -Milliseconds 1500
    Median "Hand over a file" { Run @("--open", (Quote $png)) } -runs 5
    $null = $first.WaitForExit(30000)
} finally {
    $env:CONVERT_OPEN_REPORT = $null
}

$outDir = Join-Path $work "converted"
$env:CONVERT_OUTPUT_DIR = $outDir
try {
    Median "Convert a 1 s video" {
        Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory $outDir | Out-Null
        Run @("--cli", "mp4-h264", (Quote $clip))
    } -runs 5
} finally {
    $env:CONVERT_OUTPUT_DIR = $null
}

Add-Content -Path $Results -Value (@($report) + "") -Encoding utf8
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
