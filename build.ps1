$ErrorActionPreference = 'Stop'

$vsDevCmd = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat'

$envDump = & cmd.exe /c "`"$vsDevCmd`" -arch=x64 -host_arch=x64 >nul 2>&1 && set"

foreach ($line in $envDump) {
    if ($line -match '^([^=]+)=(.*)$') {
        [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2])
    }
}

# VsDevCmd.bat doesn't add the VS Installer dir (vswhere.exe) to PATH, but the
# Native AOT ILCompiler targets shell out to vswhere.exe directly.
$env:PATH = "$env:PATH;C:\Program Files (x86)\Microsoft Visual Studio\Installer"

dotnet publish (Join-Path $PSScriptRoot 'Goto\Goto.csproj') -c Release -r win-x64
exit $LASTEXITCODE
