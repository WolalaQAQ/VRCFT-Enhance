param(
    [ValidateSet('vrcft5','vrcft6')][string]$Target = 'vrcft6',
    [Parameter(Mandatory)][string]$UpstreamSource,
    [string]$ArtifactsPath
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targetInfo = (Get-Content (Join-Path $root 'targets.json') -Raw | ConvertFrom-Json).$Target
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$UpstreamSource = [IO.Path]::GetFullPath($UpstreamSource)
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $root ('artifacts/' + $Target + '-' + (Get-Date -Format yyyyMMdd-HHmmss-fff)) }
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
if (Test-Path $ArtifactsPath) { throw "Choose a new output directory: $ArtifactsPath" }
& git -C $UpstreamSource cat-file -e "$($targetInfo.commit)^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Pinned upstream commit unavailable: $($targetInfo.commit)" }
New-Item -ItemType Directory -Path $ArtifactsPath | Out-Null
$source = Join-Path $ArtifactsPath 'source'
New-Item -ItemType Directory -Path $source | Out-Null
$archive = Join-Path $ArtifactsPath 'upstream.tar'
# Read the exact Git commit, not local edits or build products in the supplied checkout.
& git -C $UpstreamSource archive --format=tar "--output=$archive" $targetInfo.commit
if ($LASTEXITCODE -ne 0) { throw 'Could not archive the pinned upstream source' }
& tar -xf $archive -C $source
if ($LASTEXITCODE -ne 0) { throw 'Could not extract the upstream source snapshot' }
Remove-Item -LiteralPath $archive
& git -C $source init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the isolated patch workspace' }
$patch = Join-Path $root $targetInfo.patch
& git -C $source apply --check --ignore-whitespace $patch
if ($LASTEXITCODE -ne 0) { throw "Patch does not apply: $patch" }
& git -C $source apply --ignore-whitespace $patch
if ($LASTEXITCODE -ne 0) { throw 'Patch application failed' }
$build = Join-Path $ArtifactsPath 'build'
$product = "$($targetInfo.assemblyVersion)-vrcft-enhance-$version+$($targetInfo.commit)"
$properties = @('-p:Platform=x64', "-p:InformationalVersion=$product")
$buildLog = Join-Path $ArtifactsPath 'build.log'
& dotnet build (Join-Path $source 'VRCFaceTracking.ModuleProcess/VRCFaceTracking.ModuleProcess.csproj') `
    -c Release --artifacts-path $build @properties --nologo *> $buildLog
if ($LASTEXITCODE -ne 0) { Get-Content $buildLog -Tail 30; throw "Host build failed: $buildLog" }
$binaries = @(Get-ChildItem (Join-Path $build 'bin/VRCFaceTracking.ModuleProcess') -Recurse -File -Filter 'VRCFaceTracking.ModuleProcess.dll')
if ($binaries.Count -ne 1) { throw 'Expected exactly one ModuleProcess build' }
$binaryDirectory = $binaries[0].DirectoryName
$native = $targetInfo.native.ToString().ToLowerInvariant()
$testLog = Join-Path $ArtifactsPath 'test-build.log'
& dotnet build (Join-Path $PSScriptRoot 'Tests/Tests.csproj') -c Release --artifacts-path $build @properties `
    "-p:HostSource=$source" "-p:HostModuleProcess=$($binaries[0].FullName)" "-p:NativeHost=$native" --nologo *> $testLog
if ($LASTEXITCODE -ne 0) { Get-Content $testLog -Tail 30; throw "Patch tests build failed: $testLog" }
$testDll = @(Get-ChildItem (Join-Path $build 'bin/Tests') -Recurse -File -Filter 'Tests.dll')
if ($testDll.Count -ne 1) { throw 'Expected exactly one test executable' }
& (Join-Path $PSScriptRoot 'test.ps1') -TestDll $testDll[0].FullName -OutputDirectory $ArtifactsPath
$stage = Join-Path $ArtifactsPath 'stage'
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($name in @('VRCFaceTracking.Core.dll','VRCFaceTracking.ModuleProcess.dll')) {
    $file = Join-Path $binaryDirectory $name
    if ([Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString() -ne $targetInfo.assemblyVersion) { throw "Wrong assembly version: $name" }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($file).ProductVersion -cne $product) { throw "Missing patch provenance: $name" }
    Copy-Item $file $stage
}
Copy-Item (Join-Path $root 'LICENSE') $stage
$instructions = (Get-Content (Join-Path $root 'INSTALL.txt') -Raw).
    Replace('@VERSION@', $version).Replace('@HOST_VERSION@', $targetInfo.assemblyVersion).
    Replace('@UPSTREAM_COMMIT@', $targetInfo.commit)
[IO.File]::WriteAllText((Join-Path $stage 'INSTALL.txt'), $instructions, [Text.UTF8Encoding]::new($false))
$provenance = @{target=$Target; patchVersion=$version; upstreamCommit=$targetInfo.commit;
    hostAssemblyVersion=$targetInfo.assemblyVersion; patchFile=$targetInfo.patch; productVersion=$product}
$provenance | ConvertTo-Json | Set-Content (Join-Path $stage 'provenance.json') -Encoding utf8
$zip = Join-Path $ArtifactsPath "VRCFT-Enhance-$version-$($targetInfo.suffix).zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
@{source=$source; binaryDirectory=$binaryDirectory; package=$zip; target=$provenance} |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ArtifactsPath 'build-result.json') -Encoding utf8
Write-Host "Built and tested ${Target}: $zip"
Write-Host 'Local verification package only. No installation or publication performed.'
