$ErrorActionPreference = "Stop"

$rids = @("win-x64", "linux-x64", "osx-x64", "osx-arm64")

foreach ($rid in $rids) {
  $output = "./build/cli/$rid"
  Write-Host "Publishing .NET CLI for $rid to $output..." -ForegroundColor Cyan

  dotnet publish ./cli/DotnetContextMcp.Cli `
    -c Release `
    -r $rid `
    --self-contained true `
    -o $output

  if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed for $rid!" -ForegroundColor Red
    exit 1
  }

  $exeName = if ($rid -eq "win-x64") { "DotnetContextMcp.Cli.exe" } else { "DotnetContextMcp.Cli" }
  $exePath = "$output/$exeName"

  if (Test-Path $exePath) {
    $size = (Get-Item $exePath).Length / 1MB
    Write-Host ("  Binary: {0} ({1:N1} MB)" -f $exePath, $size) -ForegroundColor Green
  } else {
    Write-Host "  Warning: Expected binary not found at $exePath" -ForegroundColor Yellow
  }
}

Write-Host ""
Write-Host "All 4 RIDs published successfully!" -ForegroundColor Green
Write-Host "Total output:" -NoNewline
$totalSize = (Get-ChildItem -Path "./build/cli" -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host (" {0:N0} MB" -f $totalSize)
