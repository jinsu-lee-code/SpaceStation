# 11-11c VARCO 생성물 내려받기 (다른 PC에서): downloads.json의 이미지 → gen/, GLB → glb/
# 사용: powershell -ExecutionPolicy Bypass -File BlenderWork\AI\fetch_ai_assets.ps1
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$m = Get-Content (Join-Path $here "downloads.json") -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($kind in @(@("gen", "png"), @("glb", "glb"))) {
    $dir = Join-Path $here $kind[0]
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($p in $m.($kind[0]).PSObject.Properties) {
        $f = Join-Path $dir ($p.Name + "." + $kind[1])
        if (-not (Test-Path $f)) { Invoke-WebRequest -Uri $p.Value -OutFile $f -UseBasicParsing }
    }
    "{0}: {1}" -f $kind[0], (Get-ChildItem $dir).Count
}
