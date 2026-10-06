# Сборка DgusPlus.exe (C# 3 / .NET 3.5 — тот же рантайм CLR 2.0, что у DGUS).
#   -Target      папка DGUS V7.650 (DLL DGUS нужны как ссылки при компиляции)
#   -Out         куда положить exe (по умолчанию — в папку DGUS)
#   -FontEditor  папка font-editor (index.html, font-generator.html, dgus.css) — встраиваются в exe
param(
    [string]$Target = 'C:\DGUS_V7650',
    [string]$Out = '',
    [string]$FontEditor = (Join-Path $PSScriptRoot 'font-editor')
)
if (-not $Out) { $Out = "$Target\DgusPlus.exe" }
$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
$src = Get-ChildItem $PSScriptRoot -Filter *.cs | ForEach-Object FullName
$refs = 'DGUS_V7.650.exe', 'DwinTerminal.dll', 'DWINUI.dll' | ForEach-Object { "/r:$Target\$_" }
$res = @()
if (Test-Path "$FontEditor\index.html") {
    $res += "/resource:$FontEditor\index.html,fonts.editor.html"
    $res += "/resource:$FontEditor\font-generator.html,fonts.generator.html"
    $res += "/resource:$FontEditor\dgus.css,fonts.css"
    $res += "/resource:$FontEditor\i18n.js,fonts.i18n.js"
} else {
    Write-Warning "font-editor не найден ($FontEditor) — шрифтовые инструменты не будут встроены"
}
if (Test-Path "$PSScriptRoot\lang\Russian.ini") {
    $res += "/resource:$PSScriptRoot\lang\Russian.ini,lang.russian.ini"
}
& $csc /nologo /target:winexe /platform:x86 /optimize+ /codepage:65001 `
    "/out:$Out" "/win32icon:$PSScriptRoot\dgusplus.ico" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "/r:C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\v3.5\System.Web.Extensions.dll" `
    $refs $res $src
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
"OK -> $Out"
