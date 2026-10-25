$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScratch = Join-Path $projectRoot ('.validation/build-' + [guid]::NewGuid().ToString('N'))
$previousEnvironment = @{}
foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH')) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    New-Item -ItemType Directory -Path $buildScratch -Force | Out-Null
    $env:DOTNET_CLI_HOME = Join-Path $buildScratch 'dotnet'
    $env:NUGET_PACKAGES = Join-Path $buildScratch 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $buildScratch 'http-cache'
    dotnet restore (Join-Path $PSScriptRoot 'KwzEditor.csproj') --source https://api.nuget.org/v3/index.json --disable-parallel
    if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }
    dotnet build (Join-Path $PSScriptRoot 'KwzEditor.csproj') -c Release --no-restore --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Editor build failed.' }
    Write-Host ('Ready: ' + (Join-Path $PSScriptRoot 'bin/Release/net8.0/win-x64/KwzStudio.exe'))
} finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    if (Test-Path -LiteralPath $buildScratch) {
        $resolved = (Resolve-Path -LiteralPath $buildScratch).Path
        $allowed = [IO.Path]::GetFullPath((Join-Path $projectRoot '.validation')) + [IO.Path]::DirectorySeparatorChar
        if (!$resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean outside the build workspace.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
