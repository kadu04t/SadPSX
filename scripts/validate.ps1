param(
    [string]$BiosPath = "Bios\SCPH1001.BIN",
    [uint64]$Instructions = 1000000,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repositoryRoot
try {
    if (-not (Test-Path -LiteralPath $BiosPath -PathType Leaf)) {
        throw "Arquivo de BIOS não encontrado: $BiosPath"
    }

    $buildArguments = @("build", "SadPSX.slnx")
    if ($NoRestore) {
        $buildArguments += "--no-restore"
    }

    dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet test SadPSX.slnx --no-build --no-restore
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet run --project SadPSX.Cli --no-build -- $BiosPath $Instructions --validate
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
