$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "PinWindow.csproj"
$releaseRoot = Split-Path -Parent $projectRoot

foreach ($architecture in @("x64", "arm64", "x86")) {
    $runtime = "win-$architecture"
    $publishDirectory = Join-Path $projectRoot "artifacts\$runtime"
    $releaseFile = Join-Path $releaseRoot "PinWindow-$runtime.exe"

    dotnet publish $projectFile `
        -c Release `
        -r $runtime `
        --self-contained false `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "Publishing $runtime failed."
    }

    Copy-Item -LiteralPath (Join-Path $publishDirectory "PinWindow.exe") -Destination $releaseFile -Force
    Write-Host "Built $releaseFile" -ForegroundColor Green
}
