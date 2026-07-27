param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = "0.0.1-beta.1",
    [ValidatePattern('^[a-z0-9]+-[a-z0-9]+$')]
    [string]$Runtime = "win-x64",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$releaseName = "SadPSX-v$Version-$Runtime"
$artifactsDirectory = Join-Path $repositoryRoot "artifacts"
$publishDirectory = Join-Path $artifactsDirectory "publish\$releaseName"
$releaseDirectory = Join-Path $artifactsDirectory "releases"
$archivePath = Join-Path $releaseDirectory "$releaseName.zip"

Push-Location $repositoryRoot
try {
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

    $publishArguments = @(
        "publish",
        "SadPSX.Frontend/SadPSX.Frontend.csproj",
        "-c", "Release",
        "-r", $Runtime,
        "--self-contained", "true",
        "-o", $publishDirectory,
        "-p:Version=$Version",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )

    if ($NoRestore) {
        $publishArguments += "--no-restore"
    }

    dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Copy-Item -LiteralPath "README.md" -Destination $publishDirectory
    Copy-Item -LiteralPath "README.pt-BR.md" -Destination $publishDirectory
    Copy-Item -LiteralPath "LICENSE" -Destination $publishDirectory
    Copy-Item -LiteralPath "CHANGELOG.md" -Destination $publishDirectory
    Copy-Item -LiteralPath "THIRD_PARTY_NOTICES.md" -Destination $publishDirectory

    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Compress-Archive -Path "$publishDirectory\*" -DestinationPath $archivePath

    Write-Host ""
    Write-Host "Release package created:"
    Write-Host $archivePath
}
finally {
    Pop-Location
}
