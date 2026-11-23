# Build AlenPlayer: dist\AlenPlayer\ (AlenPlayer.exe + libmpv-2.dll) and a zip for GitHub Releases.
# Requirements: Windows 10/11 x64 only. Uses the C# compiler that ships with .NET Framework 4.x.
$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$version = '1.0.0'
$csc     = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src     = Join-Path $root 'src'
$obj     = Join-Path $root 'obj'
$vendor  = Join-Path $root 'vendor'
$out     = Join-Path $root 'dist\AlenPlayer'
New-Item -ItemType Directory -Force $obj, $vendor, $out | Out-Null

# 1) libmpv (pinned build from https://github.com/shinchiro/mpv-winbuild-cmake)
$libmpvTag = '20261111'
$libmpvZip = 'mpv-dev-x86_64-20261111-git-bcde6b93cc.7z'
$dll = Join-Path $vendor 'libmpv-2.dll'
if (-not (Test-Path $dll)) {
    Write-Host "Downloading $libmpvZip ..."
    $archive = Join-Path $vendor $libmpvZip
    Invoke-WebRequest "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$libmpvTag/$libmpvZip" -OutFile $archive
    tar -xf $archive -C $vendor libmpv-2.dll
    Remove-Item $archive
}

# 2) Compile
$sources = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
$common = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001') + $refs

$ico = Join-Path $obj 'alenplayer.ico'
if (-not (Test-Path $ico)) {
    # temporary build without an icon, used only to render the icon file
    $tmpExe = Join-Path $obj 'iconmaker.exe'
    & $csc @common "/out:$tmpExe" @sources
    if ($LASTEXITCODE) { throw 'compile failed' }
    & $tmpExe --make-icon $ico | Out-Null
}

$exe = Join-Path $out 'AlenPlayer.exe'
& $csc @common "/out:$exe" "/win32icon:$ico" "/win32manifest:$(Join-Path $src 'app.manifest')" @sources
if ($LASTEXITCODE) { throw 'compile failed' }

# 3) Package: program + licenses + short readme
Copy-Item $dll $out -Force
$license = Join-Path $root 'LICENSE'
if (Test-Path $license) { Copy-Item $license (Join-Path $out 'LICENSE.txt') -Force }
else { Write-Warning 'LICENSE not found - the release archive will not contain the license text' }
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') (Join-Path $out 'THIRD-PARTY-NOTICES.txt') -Force
$readme = (Get-Content (Join-Path $root 'packaging\README.txt') -Raw -Encoding UTF8).Replace('{VERSION}', $version)
[IO.File]::WriteAllText((Join-Path $out 'README.txt'), $readme, (New-Object Text.UTF8Encoding $true))
$zip = Join-Path $root "dist\AlenPlayer-$version-win64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip -CompressionLevel Optimal

# 4) Ready-to-run copy in the project root (AlenPlayer.exe + libmpv-2.dll next to src\)
try {
    Copy-Item $exe $root -Force -ErrorAction Stop
    if (-not (Test-Path (Join-Path $root 'libmpv-2.dll'))) { Copy-Item $dll $root }
    $rootNote = 'updated'
} catch { $rootNote = 'NOT updated (close AlenPlayer and run the build again)' }

Write-Host ("Done: {0} ({1:N0} KB) + libmpv-2.dll" -f $exe, ((Get-Item $exe).Length / 1KB))
Write-Host ("Zip:  {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
Write-Host ("Root: {0} {1}" -f (Join-Path $root 'AlenPlayer.exe'), $rootNote)
