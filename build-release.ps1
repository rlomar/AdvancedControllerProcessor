# Builds the official single-file release exe.
#
# IMPORTANT: IncludeNativeLibrariesForSelfExtract is MANDATORY.
# Without it, WPF native libraries (PresentationNative, wpfgfx, D3DCompiler...)
# stay as separate files next to the exe. The exe then works only inside the
# publish folder and CRASHES with DllNotFoundException when users download
# just the exe from GitHub Releases.
#
# Usage:  powershell -File build-release.ps1
# Output: src\AdvancedControllerProcessor\bin\Release\net8.0-windows\win-x64\publish\AdvancedControllerProcessor.exe

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

dotnet test "$root\tests\AdvancedControllerProcessor.Tests" -c Debug --nologo -v m
if ($LASTEXITCODE -ne 0) { throw "Tests failed - aborting release build" }

dotnet publish "$root\src\AdvancedControllerProcessor" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

$exe = Join-Path $root "src\AdvancedControllerProcessor\bin\Release\net8.0-windows\win-x64\publish\AdvancedControllerProcessor.exe"

# Standalone smoke test: run the exe ALONE in an empty folder, exactly like a
# user who downloaded only the exe. It must survive window creation (~8s).
$iso = Join-Path $env:TEMP "acp_standalone_$(Get-Random)"
New-Item -ItemType Directory -Path $iso | Out-Null
Copy-Item $exe (Join-Path $iso "AdvancedControllerProcessor.exe")
$proc = Start-Process (Join-Path $iso "AdvancedControllerProcessor.exe") -PassThru
Start-Sleep -Seconds 8
$proc.Refresh()
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Remove-Item $iso -Recurse -Force -ErrorAction SilentlyContinue

if ($proc.HasExited) {
    throw "Standalone smoke test FAILED (exit code $($proc.ExitCode)) - do NOT ship this build"
}

Write-Host ""
Write-Host ("OK  Release ready: {0}  ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))
