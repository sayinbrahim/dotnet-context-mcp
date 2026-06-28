$ErrorActionPreference = "Stop"
$rid = "win-x64"
$output = "./build/cli/$rid"

Write-Host "Publishing .NET CLI to $output..."
dotnet publish ./cli/DotnetContextMcp.Cli `
  -c Release `
  -r $rid `
  --self-contained true `
  -o $output

if ($LASTEXITCODE -ne 0) {
  Write-Host "Publish failed!" -ForegroundColor Red
  exit 1
}

Write-Host "Binary built at: $output/DotnetContextMcp.Cli.exe" -ForegroundColor Green
Write-Host "Size:" -NoNewline
$size = (Get-Item "$output/DotnetContextMcp.Cli.exe").Length / 1MB
Write-Host (" {0:N1} MB" -f $size)
