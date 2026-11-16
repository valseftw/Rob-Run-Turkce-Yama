# ROB RUN Türkçe Wiki verisini (data.js) uygulamanın Rehber bölümü için temiz bir wiki.json'a çevirir.
#
#  - Eşya, silah ve Larry ağacı adları, oyunun Türkçe yamasındaki adlarla (translations/tr.json) eşlenir;
#    böylece Rehber'de oyuncunun oyunda gördüğü adlar görünür.
#  - Açıklamalar mümkünse yamadaki oyun içi çeviriden alınır; terimler çeviri rehberine uydurulur
#    (fence = tefeci vb.), emoji ve tekrarlanan parantezler temizlenir.
#  - Simgeler 96 piksele, dal görselleri 480 piksele küçültülüp PNG olarak wiki/simgeler/ klasörüne yazılır
#    (webp dosyaları da PNG'ye çevrilir; uygulama her Windows'ta açabilsin diye).
#
# Kullanım (PowerShell 7):  pwsh tools/wiki-donustur.ps1 -Kaynak D:\robgunwiki [-Surum 1.0.0]
param(
    [Parameter(Mandatory)][string]$Kaynak,
    [string]$Surum = '1.0.0'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$repo = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $repo 'wiki'
$iconDir = Join-Path $outDir 'simgeler'
if (Test-Path $iconDir) { Remove-Item -Recurse -Force $iconDir }
New-Item -ItemType Directory -Force $iconDir | Out-Null

$js = [IO.File]::ReadAllText((Join-Path $Kaynak 'data.js'))
$db = $js.Substring($js.IndexOf('{')).TrimEnd().TrimEnd(';') | ConvertFrom-Json -AsHashtable

# --- oyun içi Türkçe adlar (yamanın çevirisi)
$trJson = [IO.File]::ReadAllText((Join-Path $repo 'translations/tr.json')) | ConvertFrom-Json -AsHashtable
$game = @{}
foreach ($t in $trJson.Keys) { if ($t -like '_*') { continue }; foreach ($k in $trJson[$t].Keys) { if ($trJson[$t][$k].tr) { $game[$k] = $trJson[$t][$k].tr } } }
function GameName($id) { $v = $game["$($id)_name"]; if ($v) { return (StripTags $v) } }
function GameDesc($id) { $v = $game["$($id)_description"]; if ($v) { return (StripTags $v) } }
function StripTags($s) { return ($s -replace '<[^<>]+>', '').Trim() }

# --- metin temizliği
$emoji = '[\uD800-\uDBFF][\uDC00-\uDFFF]|[\u2190-\u21FF\u2300-\u23FF\u2460-\u24FF\u25A0-\u27BF\u2B00-\u2BFF\uFE0F\u200D]'
function Tidy([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return $null }
    $s = [regex]::Replace($s, $emoji, '')
    $s = $s -replace 'Fence \(Çalıntı Eşya Tüccarı\)', 'tefeci' -replace 'Fence Satışı', 'tefeciye satış' -replace '\bFence\b', 'tefeci'
    $s = $s -replace '\bLair\b', 'sığınak' -replace '\bNotoriety\b', 'aranma seviyesi'
    $s = $s -replace '\s{2,}', ' ' -replace '\s+([.,;:!?])', '$1' -replace '^[\s:·\-]+', ''
    # "Tefeci" cümle başında büyük harfle
    $s = [regex]::Replace($s, '(^|[.!?]\s+)tefeci', { param($m) $m.Groups[1].Value + 'Tefeci' })
    return $s.Trim()
}
# "Ad (Ad)", "Market (Market (Market (X)))" gibi tekrarları sadeleştirir; İngilizce adı parantezden atar
function CleanName([string]$s, [string]$en) {
    $s = Tidy $s; if (-not $s) { return $null }
    for ($i = 0; $i -lt 4; $i++) { $s = [regex]::Replace($s, '^([^()]+?) \(\1 \((.+)\)\)$', '$1 ($2)') }
    $m = [regex]::Match($s, '^(.+?) \(([^()]+)\)$')
    if ($m.Success) {
        $inner = $m.Groups[2].Value.Trim(); $outer = $m.Groups[1].Value.Trim()
        if ($inner -eq $outer -or ($en -and $inner -ieq $en)) { $s = $outer }
    }
    return $s
}
# Başındaki virgül: tek elemanlı liste JSON'da dizi olarak kalsın (PowerShell diziyi açmasın)
function CleanList($list) { if ($list) { return , @($list | ForEach-Object { CleanName $_ $null } | Where-Object { $_ } | Select-Object -Unique) } else { return , @() } }
# "Gold Vault Heist (Merkez Bankası Altın Kasası)" -> Türkçe kısım öne
function TurkishFirst([string]$s) {
    $s = Tidy $s; if (-not $s) { return $null }
    $m = [regex]::Match($s, '^([\x00-\x7F]+?) \((.*[^\x00-\x7F].*)\)$')
    if ($m.Success) { return $m.Groups[2].Value.Trim() }
    return $s
}
# "Aktif (Available)" -> "Aktif"
function DropEnglish([string]$s) { $s = Tidy $s; if ($s) { return ($s -replace ' \([\x00-\x7F]+\)$', '') } }

# --- simgeler
$icons = @{}
function Icon([string]$url, [int]$max = 96, [string]$name = $null) {
    if (-not $url) { return $null }
    $rel = $url.TrimStart('/').Split('?')[0]
    $src = Join-Path $Kaynak $rel
    if (-not (Test-Path $src)) { return $null }
    if (-not $name) { $name = [IO.Path]::GetFileNameWithoutExtension($rel) }
    $name = ($name -replace '[^A-Za-z0-9_\-]', '_') + '.png'
    if ($icons.ContainsKey($name)) { return $name }
    $dec = [Windows.Media.Imaging.BitmapDecoder]::Create([Uri]$src, 'None', 'OnLoad')
    $f = $dec.Frames[0]
    $scale = [Math]::Min(1.0, $max / [Math]::Max($f.PixelWidth, $f.PixelHeight))
    $bmp = if ($scale -lt 1) { [Windows.Media.Imaging.TransformedBitmap]::new($f, [Windows.Media.ScaleTransform]::new($scale, $scale)) } else { $f }
    $enc = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [IO.File]::Create((Join-Path $iconDir $name)); try { $enc.Save($fs) } finally { $fs.Dispose() }
    $icons[$name] = $true
    return $name
}
# Ağaç düğümleri için önce eşya simgesini (icons/<id>.png) dener
function NodeIcon($n) { if (Test-Path (Join-Path $Kaynak "icons/$($n.id).png")) { Icon "icons/$($n.id).png" } else { Icon $n.iconUrl } }
function IdFromIcon([string]$url) { if ($url) { ([IO.Path]::GetFileNameWithoutExtension($url)) -replace '_cumulative$', '' } }

$money = { param($v) if ($null -eq $v) { $null } else { [long]$v } }

# --- eşyalar
$items = foreach ($it in $db.items) {
    [ordered]@{
        id        = $it.id
        ad        = (GameName $it.id) ?? (CleanName $it.nameTr $it.nameEn)
        en        = $it.nameEn ?? $it.name
        kategori  = Tidy ($it.categoryTr ?? $db.categoriesTr[$it.category])
        nadirlik  = $it.rarityTr
        renk      = $it.rarityColor
        simge     = Icon $it.iconUrl
        fiyat     = & $money $it.price
        tefeci    = @($it.donorPrices | Where-Object { $_ } | ForEach-Object { [ordered]@{ kademe = $_.level; fiyat = [long]$_.price } })
        aciklama  = (GameDesc $it.id) ?? (Tidy $it.descriptionTr)
        nerede    = CleanList $it.whereSummaryTr
        ipuclari  = @($it.hints | Where-Object { $_.labelTr } | ForEach-Object { [ordered]@{ yer = (CleanName $_.labelTr $null); adet = $_.count } })
        elde      = CleanList $it.obtainTr
    }
}
$itemIds = @{}; foreach ($i in $items) { $itemIds[$i.id] = $true }

# --- silahlar
$weapons = foreach ($w in $db.weapons) {
    [ordered]@{
        id = $w.id; ad = (GameName $w.id) ?? $w.name; aile = $w.family; mk = $w.mk
        sinif = Tidy $w.weaponClass; atis = Tidy $w.fireMode
        nadirlik = $w.rarityTr; renk = $w.rarityColor; simge = Icon $w.iconUrl; fiyat = & $money $w.price
        hasar = $w.damage; kafa = $w.headshotDamage; rpm = $w.rpm; sarjor = $w.magazine; menzil = $w.rangeM; dps = $w.dps
        doldurma = $w.reloadSec; hizliDoldurma = $w.fastReloadSec; nisan = $w.aimSec; cekme = $w.drawSec
        nerede = CleanList $w.whereSummaryTr
    }
}

# --- Larry'nin ağacı
$branches = foreach ($b in $db.treeBranches) {
    [ordered]@{
        id = $b.id; ad = Tidy $b.nameTr; alt = Tidy $b.subTr; gorsel = Icon $b.artUrl 480 ("dal_" + $b.id)
        dugumler = @($db.treeNodes | Where-Object branchId -eq $b.id | ForEach-Object {
            [ordered]@{
                id = $_.id; ad = (GameName $_.id) ?? (CleanName $_.nameTr $_.nameEn); en = $_.nameEn
                aciklama = (GameDesc $_.id) ?? (Tidy $_.descTr)
                asama = $_.stage; gerek = @($_.req); acar = @($_.kids)
                arastirma = & $money $_.researchCash; fiyat = & $money $_.buyPrice; yil = $_.years
                nadirlik = $_.rarityTr; renk = $_.rarityColor; simge = NodeIcon $_
                esya = if ($itemIds.ContainsKey($_.id)) { $_.id } else { $null }
            }
        })
    }
}

# --- takaslar
$barters = foreach ($b in $db.barters) {
    $oid = $b.output.id ?? (IdFromIcon $b.output.icon)
    [ordered]@{
        id = $b.id; ad = (GameName $oid) ?? (CleanName $b.titleTr $b.title); simge = Icon $b.output.icon
        esya = if ($itemIds.ContainsKey($oid)) { $oid } else { $null }
        npc = Tidy $b.npc; kullanim = Tidy $b.usageTr
        girdiler = @($b.inputs | ForEach-Object {
            $iid = IdFromIcon $_.icon
            [ordered]@{ ad = (GameName $iid) ?? (CleanName $_.nameTr $_.name); adet = [long]$_.qty; simge = Icon $_.icon; esya = if ($itemIds.ContainsKey($iid)) { $iid } else { $null } }
        })
    }
}

# --- binalar
$buildings = foreach ($b in $db.buildings) {
    [ordered]@{
        id = $b.id; ad = CleanName $b.titleTr $b.title; en = $b.title; tur = Tidy $b.typeTr
        aciklama = Tidy ($b.descTr ?? $b.descEn)
        esyalar = @($b.items | ForEach-Object { $_.id } | Where-Object { $itemIds.ContainsKey($_) } | Select-Object -Unique)
    }
}

# --- soygunlar
$robberies = foreach ($r in $db.robberies) {
    [ordered]@{
        id = $r.id; ad = TurkishFirst $r.title; grup = DropEnglish $r.group; yer = CleanName $r.location $null
        durum = DropEnglish $r.status; durumTuru = $r.statusType; simge = Icon $r.iconUrl
        aletler = CleanList $r.requiredTools; ozet = Tidy $r.summaryTr; adimlar = @($r.steps | ForEach-Object { Tidy $_ })
    }
}

$out = [ordered]@{
    _bilgi = [ordered]@{ surum = $Surum; oyun = 'ROB RUN'; oyunSurumu = ($db.meta.version -replace '\s*\(.*\)$', ''); kaynak = 'ROB RUN Türkçe Wiki (Rob Run: Türkiye Official Server)' }
    esyalar = @($items); silahlar = @($weapons); agac = @($branches); takaslar = @($barters); binalar = @($buildings); soygunlar = @($robberies)
}
$json = $out | ConvertTo-Json -Depth 8 -Compress
[IO.File]::WriteAllText((Join-Path $outDir 'wiki.json'), $json, [Text.UTF8Encoding]::new($false))

$size = (Get-ChildItem $iconDir | Measure-Object Length -Sum).Sum
Write-Host ("wiki.json: {0:N0} KB · eşya {1}, silah {2}, ağaç dalı {3} ({4} düğüm), takas {5}, bina {6}, soygun {7}" -f ((Get-Item (Join-Path $outDir 'wiki.json')).Length / 1KB), $items.Count, $weapons.Count, $branches.Count, ($branches | ForEach-Object { $_.dugumler.Count } | Measure-Object -Sum).Sum, $barters.Count, $buildings.Count, $robberies.Count)
Write-Host ("simgeler: {0} dosya, {1:N1} MB" -f $icons.Count, ($size / 1MB))
