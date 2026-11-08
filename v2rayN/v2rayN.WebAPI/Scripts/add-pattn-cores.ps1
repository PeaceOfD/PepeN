# PattN: replace the Xray-core of a v2rayN-core-bin Windows payload with patterniha/Xray-core and its geo files with
# Chocolate4U's, as PattN's desktop packages do (package-zip.yml); fetch-core-bundle.sh does the same for Linux.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid,
    [Parameter(Mandatory = $true)]
    [string[]]$BinDirectory
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$xrayAsset = if ($Rid -eq 'win-arm64') { 'Xray-windows-arm64-v8a.zip' } else { 'Xray-windows-64.zip' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('pattn-cores-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $work 'srss') -Force | Out-Null
try {
    $xrayZip = Join-Path $work $xrayAsset
    Invoke-WebRequest -Uri "https://github.com/patterniha/Xray-core/releases/latest/download/$xrayAsset" -OutFile $xrayZip
    Expand-Archive -LiteralPath $xrayZip -DestinationPath (Join-Path $work 'xray') -Force
    if (-not (Test-Path -LiteralPath (Join-Path $work 'xray\xray.exe') -PathType Leaf)) {
        throw "$xrayAsset has no xray.exe"
    }
    foreach ($geoFile in @('geosite.dat', 'geoip.dat')) {
        Invoke-WebRequest -Uri "https://github.com/Chocolate4U/Iran-v2ray-rules/releases/latest/download/$geoFile" `
            -OutFile (Join-Path $work $geoFile)
    }
    foreach ($ruleSet in @('geosite-category-ir.srs', 'geoip-ir.srs')) {
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/chocolate4u/Iran-sing-box-rules/rule-set/$ruleSet" `
            -OutFile (Join-Path $work "srss\$ruleSet")
    }
    foreach ($bin in $BinDirectory) {
        New-Item -ItemType Directory -Path (Join-Path $bin 'xray'), (Join-Path $bin 'srss') -Force | Out-Null
        Copy-Item -Path (Join-Path $work 'xray\xray*.exe') -Destination (Join-Path $bin 'xray') -Force
        Copy-Item -LiteralPath (Join-Path $work 'geosite.dat'), (Join-Path $work 'geoip.dat') -Destination $bin -Force
        Copy-Item -Path (Join-Path $work 'srss\*') -Destination (Join-Path $bin 'srss') -Force
        "Replaced Xray-core with patterniha/Xray-core and the geo files with Chocolate4U's in $bin"
    }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
