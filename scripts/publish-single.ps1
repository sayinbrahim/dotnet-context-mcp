param(
  [Parameter(Mandatory=$false)]
  [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"
$output = "./build/cli/$Rid"

Write-Host "Publishing .NET CLI for $Rid..." -ForegroundColor Cyan
dotnet publish ./cli/DotnetContextMcp.Cli `
  -c Release `
  -r $Rid `
  --self-contained true `
  -o $output

if ($LASTEXITCODE -ne 0) {
  exit 1
}

Write-Host "Done." -ForegroundColor Green
