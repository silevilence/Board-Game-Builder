param([string]$Output = "samples/poker-uno")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path $PSScriptRoot
$target = [IO.Path]::GetFullPath($Output, $repoRoot)
[IO.Directory]::CreateDirectory((Join-Path $target 'assets')) | Out-Null
$sources = @()
foreach ($suit in @('clubs', 'diamonds', 'hearts', 'spades')) {
    foreach ($rank in @('A', '02', '03', '04', '05', '06', '07', '08', '09', '10', 'J', 'Q', 'K')) {
        $sources += Join-Path $repoRoot "samples/freecell/assets/card_${suit}_${rank}.png"
    }
}
# FreeCell has no hidden cards; its sibling sample carries the back from the same Kenney pack.
$sources += Join-Path $repoRoot 'samples/island-settlers/assets/card-back.png'
$cellWidth = 144; $cellHeight = 194
$atlas = [Drawing.Bitmap]::new(13 * $cellWidth, 5 * $cellHeight)
$graphics = [Drawing.Graphics]::FromImage($atlas)
$mapping = @()
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    for ($index = 0; $index -lt $sources.Count; $index++) {
        $source = [Drawing.Bitmap]::new($sources[$index])
        try {
            $x = ($index % 13) * $cellWidth + 2
            $y = [Math]::Floor($index / 13) * $cellHeight + 2
            $graphics.DrawImage($source, [Drawing.Rectangle]::new($x, $y, 140, 190))
            $mapping += "cards#$x,$y,140,190"
        } finally { $source.Dispose() }
    }
    $atlas.Save((Join-Path $target 'assets/cards.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $atlas.Dispose() }
Copy-Item -LiteralPath (Join-Path $repoRoot 'samples/freecell/assets/License.txt') -Destination (Join-Path $target 'assets/License.txt')
$mapping | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'atlas-map.json') -Encoding utf8
python (Join-Path $PSScriptRoot 'build-poker-uno.py') $target
if ($LASTEXITCODE -ne 0) { throw 'Module generation failed' }
