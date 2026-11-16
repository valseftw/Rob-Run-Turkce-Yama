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
$emoji = '[\uD800-\uDBFF][\uDC00-\uDFFF]|[\u2300-\u23FF\u2460-\u24FF\u25A0-\u27BF\u2B00-\u2BFF\uFE0F\u200D]'
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
    # Yalnızca wiki'nin kendi icons/ klasörü kullanılır (planner/ ve map/ görselleri robrunwiki kaynaklı)
    if (-not $rel.StartsWith('icons/')) { return $null }
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
function NodeIcon($n) {
    # Önce eşyanın kendi simgesi, sonra aynı ailenin ilk kademesi (backpack_2 → backpack_1, dp9_m2 → dp9_m1)
    $cands = @($n.id, ($n.id -replace '_(\d+)(_cumulative)?$', '_1$2'), ($n.id -replace '_m\d+$', '_m1'))
    foreach ($c in $cands | Select-Object -Unique) { if (Test-Path (Join-Path $Kaynak "icons/$c.png")) { return (Icon "icons/$c.png") } }
    return $null
}
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
# --- veri düzeltmeleri (wiki verisindeki bilinen eksik/hatalar)
$items = [System.Collections.Generic.List[object]]@($items)
# 1) Kademe 2 (mor) Şifre Çözücü: oyunda var (decoder_2; oyun simge olarak decoder_1'i kullanır) ama wiki eşya listesinde yok.
if (-not ($items | Where-Object { $_.id -eq 'decoder_2' })) {
    $d1 = $items | Where-Object { $_.id -eq 'decoder_1' } | Select-Object -First 1
    if ($d1) {
        $items.Add([ordered]@{
            id = 'decoder_2'; ad = $d1.ad; en = 'Decoder'; kategori = $d1.kategori; nadirlik = 'Epik'; renk = '#e375ff'; simge = $d1.simge
            fiyat = $null; tefeci = @(); aciklama = $d1.aciklama
            nerede = @('Lenny takası: 3× Kademe 1 Şifre Çözücü + $10.000', 'Lenny takası: 1× dolu Aksiyon Kamerası (Kademe 1)')
            ipuclari = @(); elde = @()
        })
    }
}
# 2) Takas #05 ("3× Kademe 1 + $10.000 → Kademe 2") çıktısı yanlışlıkla decoder_1 yazılmış
foreach ($b in $db.barters) { if ($b.output.id -eq 'decoder_1' -and $b.output.nameTr -match 'Kademe 2') { $b.output.id = 'decoder_2'; $b.id = 'decoder_2_from_t1' } }
# 3) Aynı adı taşıyan kademeli eşyalar (ör. üç Şifre Çözücü) ayırt edilsin: "(Kademe N)"
foreach ($grp in ($items | Group-Object { $_.ad } | Where-Object Count -gt 1)) {
    if (@($grp.Group | Where-Object { $_.id -notmatch '_(\d+)$' }).Count -eq 0) {
        foreach ($it in $grp.Group) { $it.ad = $it.ad + ' (Kademe ' + ([regex]::Match($it.id, '_(\d+)$').Groups[1].Value) + ')' }
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
        id = $b.id; ad = ($items | Where-Object { $_.id -eq $oid } | Select-Object -First 1).ad ?? (GameName $oid) ?? (CleanName $b.titleTr $b.title); simge = Icon $b.output.icon
        esya = if ($itemIds.ContainsKey($oid)) { $oid } else { $null }
        npc = Tidy $b.npc; kullanim = Tidy $b.usageTr
        girdiler = @($b.inputs | ForEach-Object {
            $full = if ($_.icon) { [IO.Path]::GetFileNameWithoutExtension($_.icon) }
            $iid = if ($full -and $itemIds.ContainsKey($full)) { $full } else { IdFromIcon $_.icon }
            [ordered]@{ ad = ($items | Where-Object { $_.id -eq $iid } | Select-Object -First 1).ad ?? (GameName $iid) ?? (CleanName $_.nameTr $_.name); adet = [long]$_.qty; simge = Icon $_.icon; esya = if ($itemIds.ContainsKey($iid)) { $iid } else { $null } }
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

# --- Looty Bay haritası (wiki'nin kendi map_data_tr.js verisi; yalnızca şehir haritası)
$harita = $null
$mapJs = Join-Path $Kaynak 'map_data_tr.js'
if (Test-Path $mapJs) {
    $mt = [IO.File]::ReadAllText($mapJs)
    $city = ($mt.Substring($mt.IndexOf('{')).TrimEnd().TrimEnd(';') | ConvertFrom-Json -AsHashtable).city
    $bg = Join-Path $Kaynak ($city.bgUrl.TrimStart('/'))
    Copy-Item $bg (Join-Path $iconDir 'harita_sehir.png') -Force
    function LootRef($x) { if ($x.id) { [ordered]@{ id = $x.id; ad = (GameName $x.id) ?? (CleanName $x.nameTr $null); simge = Icon "icons/$($x.id).png"; esya = if ($itemIds.ContainsKey($x.id)) { $x.id } else { $null } } } }
    $kindGroup = @{}; foreach ($g in $city.poiGroups) { foreach ($k in $g.kinds) { $kindGroup[$k.kind] = $g.key } }
    $harita = [ordered]@{
        gorsel = 'harita_sehir.png'; genislik = $city.width; yukseklik = $city.height
        bolgeler = @($city.districts | ForEach-Object { [ordered]@{ ad = $_.nameTr; x = $_.px; y = $_.py } })
        gruplar = @($city.poiGroups | ForEach-Object { [ordered]@{ id = $_.key; ad = Tidy $_.labelTr; turler = @($_.kinds | ForEach-Object { [ordered]@{ id = $_.kind; ad = (DropEnglish $_.labelTr) } }) } })
        katmanlar = @($city.objectGroups | ForEach-Object { $_.layers } | ForEach-Object { [ordered]@{ id = $_.cls; ad = Tidy $_.labelTr; renk = $_.color } })
        noktalar = @($city.pois | ForEach-Object {
            $poi = $_
            [ordered]@{
                id = $poi.id; tur = $poi.kind; grup = $kindGroup[$poi.kind]; turAd = DropEnglish $poi.kindTr
                ad = Tidy $poi.nameTr; en = $poi.nameEn; aciklama = Tidy $poi.descTr; ozel = Tidy $poi.specialJobTr
                x = $poi.px; y = $poi.py; bolge = $poi.district
                ganimet = @($poi.loot | ForEach-Object { LootRef $_ })
                sarj = @($poi.refills | ForEach-Object { $r = LootRef $_; $r.puan = $_.points; $r })
                anahtarlar = @($poi.keys | ForEach-Object {
                    [ordered]@{
                        ad = Tidy $_.nameTr; acar = Tidy $_.opensTr; kasa = $_.safes; dolap = $_.cabinets; kutu = $_.boxes; bilgisayar = $_.computers
                        matkap = if ($_.miniDrill) { $r = LootRef $_.miniDrill; $r.adet = $_.miniDrill.count; $r } else { $null }
                        usb = if ($_.usb) { $r = LootRef $_.usb; $r.adet = $_.usb.count; $r } else { $null }
                        icerik = @($_.inside | ForEach-Object { LootRef $_ })
                    }
                })
                gorevler = @($poi.activities | ForEach-Object {
                    [ordered]@{ ad = Tidy $_.titleTr; gerek = @($_.need | ForEach-Object { LootRef $_ }); sonuc = (LootRef $_.get); adimlar = @($_.stepsTr | ForEach-Object { Tidy $_ }); not = Tidy $_.noteTr }
                })
            }
        })
        nesneler = @($city.objects | ForEach-Object {
            $loot = @($_.loot | ForEach-Object { [ordered]@{ id = $_.id; min = $_.min; max = $_.max } })
            # "Car Parts · Arama Noktası" -> oyun içi Türkçe ad
            $name = $_.nameTr
            if ($loot.Count -eq 1 -and (GameName $loot[0].id)) { $name = (GameName $loot[0].id) + ' · ' + (($name -split ' · ', 2)[1] ?? 'Arama Noktası') }
            [ordered]@{ s = $_['class']; x = [Math]::Round($_.px, 1); y = [Math]::Round($_.py, 1); ad = Tidy $name; bina = (Tidy $_.buildingTr); bolge = Tidy $_.regionTr; g = $loot }
        })
    }
    foreach ($o in $harita.nesneler) { foreach ($l in $o.g) { if ($l.id) { [void](Icon "icons/$($l.id).png") } } }
}

# --- rehberler (wiki'nin app.js içindeki başlangıç / birikimli rehber içerikleri)
$rehber = $null
$appJs = Join-Path $Kaynak 'app.js'
if ((Test-Path $appJs) -and (Get-Command node -ErrorAction SilentlyContinue)) {
    $tmp = [IO.Path]::GetTempFileName()
    node (Join-Path $PSScriptRoot 'wiki-rehber-cikar.mjs') $appJs $tmp | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Rehber içerikleri çıkarılamadı.' }
    $g = [IO.File]::ReadAllText($tmp) | ConvertFrom-Json -AsHashtable; Remove-Item $tmp
    function Act($x) { if ($x.actionType -in 'poi_kind', 'starter_tree') { [ordered]@{ tur = $x.actionType; hedef = $x.actionTarget; etiket = (Tidy ($x.actionLabel -replace '\s*→\s*$', '')) } } }
    $rehber = [ordered]@{
        adimlar = @($g.baslangicAdimlari | ForEach-Object { [ordered]@{ no = $_.step; rozet = Tidy $_.badge; baslik = Tidy $_.title; metin = Tidy $_.desc; maddeler = @($_.points | ForEach-Object { Tidy $_ }); ipuclari = @($_.tips | ForEach-Object { Tidy $_ }); eylem = (Act $_) } })
        para = @($g.paraYontemleri | ForEach-Object { [ordered]@{ sira = Tidy $_.rank; baslik = Tidy $_.title; zorluk = $_.difficulty; sure = $_.time; kazanc = $_.payout; gerekenler = Tidy $_.requirements; nasil = Tidy $_.howTo; simge = Icon "icons/$($_.itemId).png" } })
        hatalar = @($g.hatalar | ForEach-Object { [ordered]@{ no = $_.num; baslik = Tidy $_.title; metin = Tidy $_.desc } })
        cihazlar = @($g.birikimliCihazlar | ForEach-Object {
            [ordered]@{
                id = $_.id; ad = Tidy $_.titleTr; rozet = Tidy $_.badgeTr; renk = $_.rarityColor; puan = $_.pointsRequired
                arastirma = $_.researchCost; fiyat = $_.buyCost; onsart = Tidy $_.prereqTr; doldurma = Tidy $_.howToFillTr; dolunca = Tidy $_.whenFullTr
                takas = @($_.barterUse | ForEach-Object { Tidy ($_ -replace '➔', '→') }); simge = Icon "icons/$($_.id).png"
                esya = if ($itemIds.ContainsKey($_.id)) { $_.id } else { $null }
            }
        })
        baslangicPaketi = @($g.baslangicPaketi)
    }
}
# --- Yerleşim ve Üs rehberi: metinler oyunun kendi Türkçe açıklamalarından (yama çevirisi), bedeller Larry ağacından
$gameText = @{}
foreach ($t in $trJson.Keys) { if ($t -like '_*') { continue }; foreach ($k in $trJson[$t].Keys) { if ($trJson[$t][$k].tr) { $gameText[$k] = ($trJson[$t][$k].tr -replace '<[^<>]+>', '' -replace '\\n', "`n").Trim() } } }
function G([string[]]$keys) { @($keys | ForEach-Object { $gameText[$_] } | Where-Object { $_ -and $_ -notmatch '\{[A-Za-z0-9:]+\}' }) }
$nodeById = @{}; foreach ($b in $branches) { foreach ($n in $b.dugumler) { $nodeById[$n.id] = $n } }
function Costs([string[]]$ids) { @($ids | Where-Object { $nodeById.ContainsKey($_) } | ForEach-Object { $n = $nodeById[$_]; $ia = ($items | Where-Object { $_.id -eq $n.id } | Select-Object -First 1).ad; [ordered]@{ id = $n.id; ad = ($ia ?? $n.ad); arastirma = $n.arastirma; fiyat = $n.fiyat } }) }
function Rel([string[]]$ids) { @($ids | Where-Object { $itemIds.ContainsKey($_) -or $nodeById.ContainsKey($_) }) }
$houseBranch = @($branches | Where-Object id -eq 'custom_node_houses' | ForEach-Object { $_.dugumler } | Sort-Object asama | ForEach-Object id)
$yerlesim = @(
    [ordered]@{ baslik = 'Ev bölgesi (üs) nedir?'; rozet = 'Temel'; paragraflar = G 'codex_buff_base_description', 'info_panel_player_plot_description', 'codex_buff_outside_law_description'
        maddeler = G 'rent_land_plot_benefit_black_market', 'rent_land_plot_benefit_buildings', 'rent_land_plot_benefit_own_cars_bench'; ilgili = Rel 'home_1', 'home_2', 'home_3', 'money_workbench', 'storage_building_1'; dal = $null; maliyet = @() }
    [ordered]@{ baslik = 'Arsa nasıl alınır?'; rozet = 'Arsa Tapusu'; paragraflar = G 'codex_buy_plot_description', 'plot_sale_1_description', 'rent_dialog_description'
        maddeler = @('1. Larry''nin sevkiyatlarında Arsa Tapusu''nu araştır, sonra Karaborsa''dan satın al.', '2. Tapunun seviyesi yaşamak istediğin bölgenin seviyesiyle (1, 2 ya da 3) aynı olmalı.', '3. Genel haritadan sahibi olan bir oyuncu yerleşimine git ve tapuyu boş bir arsada kullan.', '4. Yeni bir arsa kiralarsan oraya taşınırsın; üssündeki her şey yeni arsaya aktarılır.')
        ilgili = Rel 'plot_sale_1', 'plot_sale_2', 'plot_sale_3'; dal = 'custom_node_documents'; maliyet = Costs 'plot_sale_1', 'plot_sale_2', 'plot_sale_3' }
    [ordered]@{ baslik = 'Evini kur ve geliştir'; rozet = 'Arsa Planı'; paragraflar = G 'hints_plot_builder_description', 'hints_set_furniture_description', 'codex_buff_base_description'
        maddeler = @('Ev türleri sırasıyla açılır: Karavan → Küçük Ev → Malikâne. Büyük ev daha fazla yapı ve mobilya yeri demektir.', 'Çitler (Çiftlik, Tel, Metal, Beton) arsanı çevreler; raflar ve kasalar ganimet saklama yerini büyütür.', 'Tüm ev, çit, sigorta ve alarm geliştirmeleri Larry''nin "İnşaat, Ev & Güvenlik" dalındadır.')
        ilgili = Rel 'home_1', 'home_2', 'home_3', 'wall_1', 'wall_2', 'wall_3', 'wall_4', 'shelving_1', 'storage_1', 'storage_building_1'; dal = 'custom_node_houses'; maliyet = Costs $houseBranch }
    [ordered]@{ baslik = 'Para aklama ve banka'; rozet = 'Aklama Masası'; paragraflar = G 'hints_money_workbench_description', 'codex_cash_withdrawal_description'
        maddeler = @('Cebindeki kirli nakit ölürsen gider; aklayıp banka hesabına yatırınca güvende olur.', 'Banknot Sayma Makinesi aklamayı hızlandırır.', 'Bir yerleşimde akladığın paradan yerleşim sahibi vergi alır.')
        ilgili = Rel 'money_workbench', 'money_workbench_speedup_1', 'atm'; dal = 'custom_node_furniture'; maliyet = Costs 'money_workbench_speedup_1', 'money_workbench_speedup_2', 'money_workbench_speedup_3' }
    [ordered]@{ baslik = 'Üretim: kargo, laboratuvar, yetiştirme'; rozet = 'Tezgâhlar'; paragraflar = G 'hints_cargo_open_workbench_description', 'hints_cargo_buyer_description', 'hints_lab_workbench_description', 'codex_garden_bed_description', 'hints_craft_workbench_description'
        maddeler = G 'hints_stolen_cargo_description', 'hints_cargo_seller_description'
        ilgili = Rel 'skill_cargo_open_workbench_0', 'skill_cargo_exchange_workbench_0', 'meth_lab_1', 'narco_farm_1', 'collection_table', 'cargo_workbench_1', 'cargo_production_workbench_1'; dal = 'custom_node_farm'; maliyet = @() }
    [ordered]@{ baslik = 'Kaçak mal paketleme'; rozet = 'Kaçak Mal Tezgâhı'
        paragraflar = G 'cargo_exchange_workbench_1_description', 'hints_cargo_buyer_description', 'marker_buy_cargo_description', 'tsd_u_hub_lenny_npc_a_5', 'hints_cargo_seller_description', 'codex_buff_selling_cargo_bonus_description', 'tsd_u_hub_lenny_npc_c_3'
        maddeler = @(
            '1. Kaçak Kargo''yu Looty Bay''den, Lenny''nin Moteli''nden ya da bölgendeki Satış Merkezi''nden satın al. (Satış Merkezi''ne yalnızca o bölgede arsa kiralayanlar girebilir.)',
            '2. Kargoyu üssündeki Kaçak Mal Tezgâhı''na koy; yeniden paketleme zaman alır. İşlem sürerken kargoyu geri alırsan paketleme durur ve baştan başlaman gerekir.',
            '3. Paketlenen kargoyu Kaçakçı Kamyoneti''ne sat. Satış bonusu bulunduğun konuma ve aranma seviyene bağlıdır; aranma seviyen yüksekken satmak daha kârlıdır.',
            '4. Wiki verisine göre $10.000''lık ham paket yaklaşık 30 dakikada $35.000–$50.000 değerinde kargoya dönüşür (oyunda doğrula).',
            '5. Etiket Yazıcı paketlemeyi hızlandırır; Alet Kutusu ve Ek Yuvalar aynı anda daha çok kargo paketlemeni sağlar.',
            '6. Kaçakçıya yaptığın her teslimat Kargo Listesi''ni doldurur; dolunca iyi bir fiyata satılabilir.',
            '7. Dikkat: hapis süren yasa dışı varlıklarına göre belirlenir; kaçak mal dolu depolar cezanı uzatır.')
        ilgili = Rel 'cargo_alcohol_grade_1', 'cargo_exchange_workbench_1', 'cargo_exchange_workbench_speedup_upgrade_1', 'cargo_exchange_workbench_additional_slot', 'cargo_manifest_1_cumulative', 'cargo_manifest_2_cumulative', 'cargo_contraband_bag_1', 'cargo_contraband_bag_2'
        dal = 'custom_node_furniture'
        maliyet = Costs 'cargo_exchange_workbench_1', 'cargo_exchange_workbench_speedup_upgrade_1', 'cargo_exchange_workbench_speedup_upgrade_2', 'cargo_exchange_workbench_additional_slot_upgrade', 'cargo_exchange_workbench_additional_double_slot_upgrade', 'cargo_manifest_2_cumulative' }
    [ordered]@{ baslik = 'Araç, garaj ve bagaj'; rozet = 'Garaj'; paragraflar = G 'hints_vehicle_keyholder_description', 'hints_rent_vehicle_description', 'hints_vehicle_trunk_description', 'hints_vehicle_hood_description'
        maddeler = @(); ilgili = Rel 'car_garage_1', 'car_garage_2', 'watchtower_1', 'helicopter_garage_1', 'fukurou_tensa'; dal = 'custom_node_cars'; maliyet = @() }
    [ordered]@{ baslik = 'Üs savunması ve baskın'; rozet = 'Güvenlik'; paragraflar = G 'hints_builder_defense_panel_description', 'codex_buff_enemy_plot_description', 'evict_plot_1_description', 'hints_first_aid_cabinet_description'
        maddeler = @('Kendi üssünü Sigorta ve Alarm Sistemleriyle koru; başkasının üssüne Şifre Çözücü ile girilir.', 'Şifre Çözücü yapının savunma seviyesini kırma değeri kadar düşürür; kademesi yükseldikçe daha güçlüdür.', 'Bir oyuncuyu arsadan atmak için Tahliye Hakkı gerekir ve üssündeki tüm güvenlik sistemleri hacklenmelidir.')
        ilgili = Rel 'decoder_1', 'decoder_2', 'decoder_3', 'evict_plot_1', 'personal_alarm_1', 'first_aid_cabinet_1'; dal = 'custom_node_houses'; maliyet = @() }
    [ordered]@{ baslik = 'Yerleşim sahibi olmak ve vergi'; rozet = 'Yerleşim Tapusu'; paragraflar = G 'codex_buy_settlement_description', 'buy_settelment_dialog_description_text', 'hints_tax_point_description', 'settlement_ui_description'
        maddeler = @('Gelir, sakinlerin akladığı paradan ve Karaborsa alışverişlerinden alınan vergidir; sakin sayısı ve sakinlerin harcaması arttıkça artar.', 'Vergiyi Tahsildar Kamyoneti''nde topla. Toplanmamış bakiye varken yeni vergi hesaba geçmez; düzenli topla.', 'Sahip kendine ücretsiz arsa ayırabilir. Bölgeyi bir klan ele geçirdiyse sahip o klana haraç öder.', 'Vergi oranı oyunda açıklanmaz; kesin tutarı oyun içinde gözlemleyerek öğrenebilirsin.')
        ilgili = Rel 'settlement_sale_1', 'settlement_sale_2', 'settlement_sale_3'; dal = 'custom_node_documents'; maliyet = Costs 'settlement_sale_1', 'settlement_sale_2', 'settlement_sale_3' }
    [ordered]@{ baslik = 'Klanlar, bölgeler ve Gölge Vali'; rozet = 'Klan'; paragraflar = G 'clan_wars_mail_description', 'hints_business_description', 'conquest_central_sector_dialog_description_text'
        maddeler = G 'realm_conquest_sector_dialog_description_1', 'realm_conquest_central_sector_dialog_description_2', 'clan_banner_description_1', 'clan_banner_description_3'
        ilgili = @(); dal = $null; maliyet = @() }
)
if ($rehber) { $rehber.yerlesim = $yerlesim }
$out = [ordered]@{
    _bilgi = [ordered]@{ surum = $Surum; oyun = 'ROB RUN'; oyunSurumu = ($db.meta.version -replace '\s*\(.*\)$', ''); kaynak = 'ROB RUN Türkçe Wiki (Rob Run: Türkiye Official Server)' }
    esyalar = @($items); silahlar = @($weapons); agac = @($branches); takaslar = @($barters); binalar = @($buildings); soygunlar = @($robberies)
    harita = $harita; rehberler = $rehber
}
$json = $out | ConvertTo-Json -Depth 8 -Compress
[IO.File]::WriteAllText((Join-Path $outDir 'wiki.json'), $json, [Text.UTF8Encoding]::new($false))

$size = (Get-ChildItem $iconDir | Measure-Object Length -Sum).Sum
Write-Host ("wiki.json: {0:N0} KB · eşya {1}, silah {2}, ağaç dalı {3} ({4} düğüm), takas {5}, bina {6}, soygun {7}" -f ((Get-Item (Join-Path $outDir 'wiki.json')).Length / 1KB), $items.Count, $weapons.Count, $branches.Count, ($branches | ForEach-Object { $_.dugumler.Count } | Measure-Object -Sum).Sum, $barters.Count, $buildings.Count, $robberies.Count)
Write-Host ("simgeler: {0} dosya, {1:N1} MB" -f $icons.Count, ($size / 1MB))
