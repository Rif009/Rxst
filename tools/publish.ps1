$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace 'src\XstReader.App\XstReader.App.csproj'
dotnet publish $project -c Release -p:PublishProfile=WindowsSingleFile -m:1
if ($LASTEXITCODE -ne 0) { throw 'Rxst publish failed.' }
$output = Join-Path $workspace 'artifacts\Rxst-single'
$files = @(Get-ChildItem -LiteralPath $output -File -Recurse)
if ($files.Count -ne 1 -or $files[0].Name -ne 'Rxst.exe') {
    throw 'Expected exactly one file: artifacts\Rxst-single\Rxst.exe. Inspect the publish folder for loose dependencies.'
}
Write-Host ('Rxst.exe: {0:N1} MB' -f ($files[0].Length / 1MB))
Write-Host $files[0].FullName
