using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RobRunTurkce
{
    /// <summary>
    /// Oyunun yazı tiplerinde (TextMeshPro SDF font atlasları) Türkçeye özgü ı İ ş Ş ğ Ğ harfleri yok.
    /// Bu sınıf eksik harfleri, yazı tipinde zaten bulunan harf parçalarını birleştirerek üretir:
    ///   ı = i'nin noktasız gövdesi            İ = I + i'nin noktası
    ///   ş = s + ç'nin çengeli                 Ş = S + Ç'nin çengeli
    ///   ğ = g + й'nin kısa işareti (breve)    Ğ = G + Й'nin kısa işareti
    /// Kaynak harf yoksa yeni harf, en yakın harfin görüntüsünü kullanır (ğ -> g gibi).
    /// Yalnızca font tablosuna yeni karakter ekler ve atlasın boş bir bölgesine çizer; mevcut harflere dokunmaz.
    /// </summary>
    public static class FontPatcher
    {
        public const string FontBundle = "inbuild_assets_all.bundle";

        public sealed class FontReport
        {
            public readonly List<string> Lines = new List<string>();
            public int Composed, Aliased, FontsPatched;
        }

        sealed class Glyph
        {
            public uint Index;
            public int X, Y, W, H;           // atlas içindeki dikdörtgen (y aşağıdan yukarı)
            public float MW, MH, BX, BY, Adv; // metrikler
            public int Top => (int)Math.Round(BY);
            public int Left => (int)Math.Round(BX);
            public float CenterX => BX + MW / 2f;
        }

        sealed class Part
        {
            public Glyph Src;
            public int Dx, Dy;
            public int RowMin = int.MinValue, RowMax = int.MaxValue; // kaynakta üstten satır aralığı [min, max)
        }

        sealed class Atlas
        {
            public int W, H;
            public byte[] Px;
            public bool[] Used; // font varlığında kayıtlı dolu alanlar (dolgu dahil)
            public byte Get(int x, int y) => (x < 0 || y < 0 || x >= W || y >= H) ? (byte)0 : Px[y * W + x];
        }

        static readonly char[] Needed = { 'ı', 'İ', 'ş', 'Ş', 'ğ', 'Ğ', 'ç', 'Ç', 'ö', 'Ö', 'ü', 'Ü' };

        /// <param name="previewDir">Doluysa, üretilen harflerin önizleme görüntüleri (PGM) bu klasöre yazılır.</param>
        public static FontReport Patch(string srcBundle, string outBundle, string previewDir = null)
        {
            var rep = new FontReport();
            var am = new AssetsManager();
            var bun = BundleIO.Open(am, srcBundle);
            int dirIndex = -1; AssetsFileInstance inst = null;
            for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                if (bun.file.IsAssetsFile(i)) { dirIndex = i; inst = am.LoadAssetsFileFromBundle(bun, i, false); break; }
            if (inst == null) throw new InvalidDataException("Font paketi okunamadı.");

            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour).ToList())
            {
                var bf = am.GetBaseField(inst, info);
                if (bf["m_CharacterTable"].IsDummy || bf["m_GlyphTable"].IsDummy) continue;
                string name = bf["m_Name"].AsString;
                var chars = bf["m_CharacterTable.Array"];
                var glyphsF = bf["m_GlyphTable.Array"];
                var have = new HashSet<int>(chars.Children.Select(c => (int)c["m_Unicode"].AsUInt));
                if (!have.Contains('a') || !have.Contains('i')) continue; // simge fontu vb.
                if (Needed.All(c => have.Contains(c))) { rep.Lines.Add($"{name}: zaten tam"); continue; }

                // atlas dokusu
                var atlasPtr = bf["m_AtlasTextures.Array"].Children[0];
                if (atlasPtr["m_FileID"].AsInt != 0) { rep.Lines.Add($"{name}: atlas başka dosyada, atlandı"); continue; }
                var texInfo = inst.file.GetAssetInfo(atlasPtr["m_PathID"].AsLong);
                var tex = am.GetBaseField(inst, texInfo);
                if (tex["m_TextureFormat"].AsInt != 1) { rep.Lines.Add($"{name}: atlas biçimi Alpha8 değil, atlandı"); continue; }
                var atlas = new Atlas { W = tex["m_Width"].AsInt, H = tex["m_Height"].AsInt };
                var sd = tex["m_StreamData"];
                if (sd["size"].AsUInt > 0)
                    atlas.Px = BundleIO.ReadStreamData(bun, sd["path"].AsString, (long)sd["offset"].AsULong, (int)sd["size"].AsUInt);
                else
                    atlas.Px = tex["image data"].AsByteArray;
                if (atlas.Px.Length < atlas.W * atlas.H) { rep.Lines.Add($"{name}: atlas verisi eksik, atlandı"); continue; }
                atlas.Px = atlas.Px.Take(atlas.W * atlas.H).ToArray();

                int pad = (int)Math.Round(bf["m_AtlasPadding"].AsFloat);
                var glyphs = new Dictionary<uint, Glyph>();
                var glyphField = new Dictionary<uint, AssetTypeValueField>();
                foreach (var g in glyphsF.Children)
                {
                    var gl = new Glyph
                    {
                        Index = g["m_Index"].AsUInt,
                        X = g["m_GlyphRect.m_X"].AsInt, Y = g["m_GlyphRect.m_Y"].AsInt,
                        W = g["m_GlyphRect.m_Width"].AsInt, H = g["m_GlyphRect.m_Height"].AsInt,
                        MW = g["m_Metrics.m_Width"].AsFloat, MH = g["m_Metrics.m_Height"].AsFloat,
                        BX = g["m_Metrics.m_HorizontalBearingX"].AsFloat, BY = g["m_Metrics.m_HorizontalBearingY"].AsFloat,
                        Adv = g["m_Metrics.m_HorizontalAdvance"].AsFloat,
                    };
                    glyphs[gl.Index] = gl; glyphField[gl.Index] = g;
                }
                var byChar = new Dictionary<int, Glyph>();
                foreach (var c in chars.Children)
                    if (glyphs.TryGetValue(c["m_GlyphIndex"].AsUInt, out var gl)) byChar[(int)c["m_Unicode"].AsUInt] = gl;
                Glyph G(char c) => byChar.TryGetValue(c, out var g) ? g : null;

                uint nextIndex = Math.Max(65000u, glyphs.Keys.Max() + 1);
                var usedRects = bf["m_UsedGlyphRects.Array"];
                // Font varlığının kayıtlı "dolu alanlarını" da işaretle: sıfır pikselli ama başka bir harfin
                // dolgusuna ait bir bölgeye yazılmasın.
                atlas.Used = new bool[atlas.W * atlas.H];
                foreach (var u in usedRects.Children)
                    MarkUsed(atlas, u["m_X"].AsInt, u["m_Y"].AsInt, u["m_Width"].AsInt, u["m_Height"].AsInt);
                foreach (var g in glyphs.Values)
                    MarkUsed(atlas, g.X - pad, g.Y - pad, g.W + 2 * pad, g.H + 2 * pad);
                int composed = 0, aliased = 0;
                bool grown = false;
                int origH = atlas.H;

                void AddChar(char c, uint glyphIndex)
                {
                    var ce = ValueBuilder.DefaultValueFieldFromArrayTemplate(chars);
                    ce["m_ElementType"].AsInt = 1;
                    ce["m_Unicode"].AsUInt = c;
                    ce["m_GlyphIndex"].AsUInt = glyphIndex;
                    ce["m_Scale"].AsFloat = 1f;
                    chars.Children.Add(ce);
                    have.Add(c);
                }

                void Alias(char c, char to)
                {
                    if (have.Contains(c)) return;
                    var g = G(to);
                    if (g == null) return;
                    AddChar(c, g.Index);
                    byChar[c] = g;
                    aliased++;
                    rep.Lines.Add($"{name}: '{c}' -> '{to}' görüntüsü (kaynak harf yok)");
                }

                bool Compose(char c, Glyph baseG, List<Part> parts)
                {
                    if (have.Contains(c)) return true;
                    var r = Render(atlas, pad, parts);
                    if (r == null) return false;
                    var spot = FindFree(atlas, r.W + 2 * pad + 2, r.H + 2 * pad + 2);
                    if (spot == null && !grown)
                    {
                        // Atlas dolu: dokuyu yukarı doğru büyüt. Glif koordinatları alttan sayıldığı için
                        // mevcut harflerin konumu değişmez; TMP doku koordinatlarını m_AtlasHeight'a göre hesaplar.
                        int extra = 256;
                        var bigger = new byte[atlas.W * (atlas.H + extra)];
                        Buffer.BlockCopy(atlas.Px, 0, bigger, 0, atlas.Px.Length);
                        var biggerUsed = new bool[bigger.Length];
                        Array.Copy(atlas.Used, biggerUsed, atlas.Used.Length);
                        atlas.Px = bigger; atlas.Used = biggerUsed; atlas.H += extra; grown = true;
                        spot = FindFree(atlas, r.W + 2 * pad + 2, r.H + 2 * pad + 2);
                    }
                    if (spot == null) { rep.Lines.Add($"{name}: atlasta yer kalmadı ('{c}')"); return false; }
                    int ax = spot.Value.x + 1 + pad, ay = spot.Value.y + 1 + pad; // glif dikdörtgeninin sol-alt köşesi
                    // dolgu dahil yaz (satır 0 = üst)
                    for (int row = -pad; row < r.H + pad; row++)
                        for (int col = -pad; col < r.W + pad; col++)
                        {
                            int py = ay + (r.H - 1 - row), px = ax + col;
                            atlas.Px[py * atlas.W + px] = r.Get(col, row);
                        }

                    uint idx = nextIndex++;
                    var ge = ValueBuilder.DefaultValueFieldFromArrayTemplate(glyphsF);
                    var proto = glyphField[baseG.Index];
                    // Atlas pikseli ile font birimi arasındaki ölçek temel harften alınır (çoğunlukla ~0,97-1,0).
                    // Böylece yeni harf, yanındaki harflerle aynı boyda ve aynı hizada çizilir.
                    float kx = baseG.W > 0 ? baseG.MW / baseG.W : 1f, ky = baseG.H > 0 ? baseG.MH / baseG.H : 1f;
                    ge["m_Index"].AsUInt = idx;
                    ge["m_Metrics.m_Width"].AsFloat = r.W * kx;
                    ge["m_Metrics.m_Height"].AsFloat = r.H * ky;
                    ge["m_Metrics.m_HorizontalBearingX"].AsFloat = baseG.BX + (r.Left - baseG.Left) * kx;
                    ge["m_Metrics.m_HorizontalBearingY"].AsFloat = baseG.BY + (r.Top - baseG.Top) * ky;
                    ge["m_Metrics.m_HorizontalAdvance"].AsFloat = baseG.Adv;
                    ge["m_GlyphRect.m_X"].AsInt = ax;
                    ge["m_GlyphRect.m_Y"].AsInt = ay;
                    ge["m_GlyphRect.m_Width"].AsInt = r.W;
                    ge["m_GlyphRect.m_Height"].AsInt = r.H;
                    ge["m_Scale"].AsFloat = proto["m_Scale"].AsFloat;
                    ge["m_AtlasIndex"].AsInt = 0;
                    if (!proto["m_ClassDefinitionType"].IsDummy) ge["m_ClassDefinitionType"].AsInt = proto["m_ClassDefinitionType"].AsInt;
                    glyphsF.Children.Add(ge);

                    var ur = ValueBuilder.DefaultValueFieldFromArrayTemplate(usedRects);
                    ur["m_X"].AsInt = spot.Value.x; ur["m_Y"].AsInt = spot.Value.y;
                    ur["m_Width"].AsInt = r.W + 2 * pad + 2; ur["m_Height"].AsInt = r.H + 2 * pad + 2;
                    usedRects.Children.Add(ur);

                    AddChar(c, idx);
                    var ng = new Glyph { Index = idx, X = ax, Y = ay, W = r.W, H = r.H, MW = r.W * kx, MH = r.H * ky,
                                         BX = baseG.BX + (r.Left - baseG.Left) * kx, BY = baseG.BY + (r.Top - baseG.Top) * ky, Adv = baseG.Adv };
                    MarkUsed(atlas, spot.Value.x, spot.Value.y, r.W + 2 * pad + 2, r.H + 2 * pad + 2);
                    glyphs[idx] = ng; byChar[c] = ng;
                    composed++;
                    if (previewDir != null) WritePreview(Path.Combine(previewDir, $"{San(name)}_{(int)c:x4}.png"), r, pad, baseG, atlas);
                    return true;
                }

                // ı: i'nin noktasız gövdesi
                var gi = G('i');
                var iSplit = gi != null ? SplitAccent(atlas, gi) : null;
                if (gi != null && iSplit != null)
                    Compose('ı', gi, new List<Part> { new Part { Src = gi, RowMin = iSplit.Value.mid } });

                // İ: I + i'nin noktası
                var gI = G('I');
                if (gI != null && gi != null && iSplit != null)
                    Compose('İ', gI, new List<Part> {
                        new Part { Src = gI },
                        new Part { Src = gi, RowMax = iSplit.Value.mid, Dy = gI.Top - iSplit.Value.bodyTopY,
                                   Dx = (int)Math.Round(gI.CenterX - gi.CenterX) } });

                // ş / Ş: s + ç'nin çengeli
                ComposeCedilla('ş', G('s'), G('ç'), G('c'));
                ComposeCedilla('Ş', G('S'), G('Ç'), G('C'));
                void ComposeCedilla(char c, Glyph b, Glyph withCed, Glyph plain)
                {
                    if (b == null || withCed == null || plain == null) return;
                    int plainBottom = (int)Math.Round(plain.BY - plain.MH);
                    int cutY = plainBottom + Math.Max(1, (int)Math.Round(plain.MH * 0.06));
                    int rowCut = withCed.Top - cutY; // bu satırdan aşağısı çengel
                    Compose(c, b, new List<Part> {
                        new Part { Src = b },
                        new Part { Src = withCed, RowMin = rowCut, Dx = (int)Math.Round(b.CenterX - plain.CenterX) } });
                }

                // ğ / Ğ: g + й'nin kısa işareti
                ComposeBreve('ğ', G('g'), G('й'));
                ComposeBreve('Ğ', G('G'), G('Й'));
                void ComposeBreve(char c, Glyph b, Glyph src)
                {
                    if (b == null || src == null) return;
                    var sp = SplitAccent(atlas, src);
                    if (sp == null) return;
                    Compose(c, b, new List<Part> {
                        new Part { Src = b },
                        new Part { Src = src, RowMax = sp.Value.mid, Dy = b.Top - sp.Value.bodyTopY,
                                   Dx = (int)Math.Round(b.CenterX - src.CenterX) } });
                }

                // Üretilemeyenler için en yakın harf
                Alias('ı', 'i'); Alias('İ', 'I'); Alias('ş', 's'); Alias('Ş', 'S'); Alias('ğ', 'g'); Alias('Ğ', 'G');
                Alias('ç', 'c'); Alias('Ç', 'C'); Alias('ö', 'o'); Alias('Ö', 'O'); Alias('ü', 'u'); Alias('Ü', 'U');

                if (composed == 0 && aliased == 0) continue;
                rep.Composed += composed; rep.Aliased += aliased; rep.FontsPatched++;
                rep.Lines.Add($"{name}: {composed} harf üretildi, {aliased} harf benzerine yönlendirildi");

                // Unity çalışma anında karakter tablosundan sözlük kurar; sıralama gerekmez.
                info.SetNewData(bf);

                // Doku: veriyi .resS yerine doğrudan varlığın içine yaz
                if (grown)
                {
                    tex["m_Height"].AsInt = atlas.H;
                    bf["m_AtlasHeight"].AsInt = atlas.H;
                    info.SetNewData(bf);
                    int mats = UpdateMaterials(am, inst, texInfo.PathId, atlas.H);
                    rep.Lines.Add($"{name}: atlas {atlas.W}x{origH} -> {atlas.W}x{atlas.H} büyütüldü ({mats} materyal güncellendi)");
                }
                if (!tex["m_MipCount"].IsDummy) tex["m_MipCount"].AsInt = 1;
                tex["image data"].AsByteArray = atlas.Px;
                sd["offset"].AsULong = 0; sd["size"].AsUInt = 0; sd["path"].AsString = "";
                if (!tex["m_CompleteImageSize"].IsDummy) tex["m_CompleteImageSize"].AsUInt = (uint)atlas.Px.Length;
                texInfo.SetNewData(tex);
            }

            if (rep.FontsPatched > 0) BundleIO.Save(bun, inst, dirIndex, outBundle);
            am.UnloadAll(true);
            return rep;
        }

        /// <summary>Paketteki herhangi bir fontta 'ş' karakteri var mı (yani bu yama daha önce uygulanmış mı)?</summary>
        public static bool HasTurkishGlyphs(string bundlePath)
        {
            var am = new AssetsManager();
            try
            {
                var bun = BundleIO.Open(am, bundlePath);
                for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                {
                    if (!bun.file.IsAssetsFile(i)) continue;
                    var inst = am.LoadAssetsFileFromBundle(bun, i, false);
                    foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                    {
                        var bf = am.GetBaseField(inst, info);
                        if (bf["m_CharacterTable"].IsDummy) continue;
                        if (bf["m_CharacterTable.Array"].Children.Any(c => c["m_Unicode"].AsUInt == 'ş')) return true;
                    }
                }
                return false;
            }
            finally { am.UnloadAll(true); }
        }

        static void MarkUsed(Atlas a, int x, int y, int w, int h)
        {
            for (int yy = Math.Max(0, y); yy < Math.Min(a.H, y + h); yy++)
                for (int xx = Math.Max(0, x); xx < Math.Min(a.W, x + w); xx++)
                    a.Used[yy * a.W + xx] = true;
        }

        /// <summary>
        /// Atlası kullanan TMP materyallerindeki _TextureHeight değerini günceller (eğim/kabartma efektleri bu değeri kullanır).
        /// </summary>
        static int UpdateMaterials(AssetsManager am, AssetsFileInstance inst, long texPathId, int newHeight)
        {
            int n = 0;
            foreach (var mi in inst.file.GetAssetsOfType(AssetClassID.Material).ToList())
            {
                AssetTypeValueField m;
                try { m = am.GetBaseField(inst, mi); } catch { continue; }
                var texEnvs = m["m_SavedProperties.m_TexEnvs.Array"];
                var floats = m["m_SavedProperties.m_Floats.Array"];
                if (texEnvs.IsDummy || floats.IsDummy) continue;
                bool usesAtlas = texEnvs.Children.Any(t => t["first"].AsString == "_MainTex"
                    && t["second.m_Texture.m_FileID"].AsInt == 0 && t["second.m_Texture.m_PathID"].AsLong == texPathId);
                if (!usesAtlas) continue;
                bool changed = false;
                foreach (var f in floats.Children)
                    if (f["first"].AsString == "_TextureHeight") { f["second"].AsFloat = newHeight; changed = true; }
                if (changed) { mi.SetNewData(m); n++; }
            }
            return n;
        }

        static string San(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

        /// <summary>
        /// Üstünde ayrı bir işaret (nokta, breve) olan harfte, işaret ile gövde arasındaki boşluğu bulur.
        /// mid: boşluğun ortasındaki satır (üstten), bodyTopY: gövdenin üst kenarının glif uzayındaki y'si.
        /// </summary>
        static (int mid, int bodyTopY)? SplitAccent(Atlas a, Glyph g)
        {
            int state = 0, gapStart = -1;
            for (int row = 0; row < g.H; row++)
            {
                int ay = g.Y + g.H - 1 - row;
                bool ink = false;
                for (int col = 0; col < g.W && !ink; col++) if (a.Get(g.X + col, ay) >= 128) ink = true;
                if (state == 0 && ink) state = 1;               // işaret
                else if (state == 1 && !ink) { state = 2; gapStart = row; } // boşluk
                else if (state == 2 && ink)                       // gövde başladı
                {
                    int mid = (gapStart + row) / 2;
                    return (mid, g.Top - row);
                }
            }
            return null;
        }

        sealed class Rendered
        {
            public int W, H, Left, Top, Pad;
            public byte[] V; // (W+2pad) x (H+2pad), satır 0 = üst dolgu
            public byte Get(int col, int row) => V[(row + Pad) * (W + 2 * Pad) + col + Pad];
        }

        /// <summary>Parçaları SDF birleşimi (en büyük değer) ile tek bir glife çizer.</summary>
        static Rendered Render(Atlas a, int pad, List<Part> parts)
        {
            // Tuval: tüm parçaların dolgulu kutularını kapsar (glif uzayında: x sağa, y yukarı)
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var p in parts)
            {
                int l = p.Src.Left + p.Dx - pad, r = p.Src.Left + p.Dx + p.Src.W + pad;
                int t = p.Src.Top + p.Dy + pad, b = p.Src.Top + p.Dy - p.Src.H - pad;
                minX = Math.Min(minX, l); maxX = Math.Max(maxX, r); maxY = Math.Max(maxY, t); minY = Math.Min(minY, b);
            }
            int cw = maxX - minX, ch = maxY - minY;
            var canvas = new byte[cw * ch];
            for (int row = 0; row < ch; row++)
            {
                int gy = maxY - row; // hücrenin üst kenarı
                for (int col = 0; col < cw; col++)
                {
                    int gx = minX + col;
                    byte best = 0;
                    foreach (var p in parts)
                    {
                        int srow = p.Src.Top - (gy - p.Dy);
                        int scol = gx - p.Dx - p.Src.Left;
                        if (srow < -pad || srow >= p.Src.H + pad || scol < -pad || scol >= p.Src.W + pad) continue;
                        if (srow < p.RowMin || srow >= p.RowMax) continue;
                        byte v = a.Get(p.Src.X + scol, p.Src.Y + p.Src.H - 1 - srow);
                        if (v > best) best = v;
                    }
                    canvas[row * cw + col] = best;
                }
            }
            // Mürekkep sınırları (>= 128) = yeni glif dikdörtgeni
            int iMinC = int.MaxValue, iMaxC = -1, iMinR = int.MaxValue, iMaxR = -1;
            for (int row = 0; row < ch; row++)
                for (int col = 0; col < cw; col++)
                    if (canvas[row * cw + col] >= 128)
                    {
                        iMinC = Math.Min(iMinC, col); iMaxC = Math.Max(iMaxC, col);
                        iMinR = Math.Min(iMinR, row); iMaxR = Math.Max(iMaxR, row);
                    }
            if (iMaxC < 0) return null;
            var res = new Rendered { W = iMaxC - iMinC + 1, H = iMaxR - iMinR + 1, Pad = pad, Left = minX + iMinC, Top = maxY - iMinR };
            int rw = res.W + 2 * pad, rh = res.H + 2 * pad;
            res.V = new byte[rw * rh];
            for (int row = 0; row < rh; row++)
                for (int col = 0; col < rw; col++)
                {
                    int cr = iMinR - pad + row, cc = iMinC - pad + col;
                    res.V[row * rw + col] = (cr < 0 || cc < 0 || cr >= ch || cc >= cw) ? (byte)0 : canvas[cr * cw + cc];
                }
            return res;
        }

        /// <summary>Atlasta tamamen boş (sıfır) w x h büyüklüğünde bir alan arar (aşağıdan yukarı).</summary>
        static (int x, int y)? FindFree(Atlas a, int w, int h)
        {
            // Satır başına "sağa doğru kaç boş piksel var" yerine basit tümlev tablosu
            int W = a.W, H = a.H;
            var sum = new int[(W + 1) * (H + 1)];
            for (int y = 0; y < H; y++)
            {
                int rowSum = 0;
                for (int x = 0; x < W; x++)
                {
                    rowSum += (a.Px[y * W + x] != 0 || (a.Used != null && a.Used[y * W + x])) ? 1 : 0;
                    sum[(y + 1) * (W + 1) + x + 1] = sum[y * (W + 1) + x + 1] + rowSum;
                }
            }
            int Count(int x, int y) =>
                sum[(y + h) * (W + 1) + x + w] - sum[y * (W + 1) + x + w] - sum[(y + h) * (W + 1) + x] + sum[y * (W + 1) + x];
            for (int y = 0; y + h <= H; y += 2)
                for (int x = 0; x + w <= W; x += 2)
                    if (Count(x, y) == 0) return (x, y);
            return null;
        }

        /// <summary>Önizleme: solda temel harf (atlas'tan), sağda üretilen harf; taban çizgisi kırmızı.</summary>
        static void WritePreview(string path, Rendered r, int pad, Glyph baseG, Atlas a)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            int top = Math.Max(r.Top, baseG.Top) + 4, bottom = Math.Min(r.Top - r.H, baseG.Top - baseG.H) - 4;
            int left = Math.Min(r.Left, baseG.Left) - 4;
            int cellW = Math.Max(r.Left + r.W, baseG.Left + baseG.W) - left + 4;
            int H = top - bottom, S = Math.Max(1, 160 / Math.Max(1, H));
            using (var bmp = new System.Drawing.Bitmap(cellW * 2 * S, H * S))
            {
                int Shade(int v) { int t = (v - 112) * 255 / 32; return 255 - Math.Max(0, Math.Min(255, t)); }
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < cellW; x++)
                    {
                        int gx = left + x, gy = top - y;
                        // temel harf
                        int bc = gx - baseG.Left, br = baseG.Top - gy;
                        int bv = (bc >= 0 && br >= 0 && bc < baseG.W && br < baseG.H) ? a.Get(baseG.X + bc, baseG.Y + baseG.H - 1 - br) : 0;
                        int rc = gx - r.Left, rr = r.Top - gy;
                        int rv = (rc >= -pad && rr >= -pad && rc < r.W + pad && rr < r.H + pad) ? r.Get(rc, rr) : 0;
                        for (int sy = 0; sy < S; sy++)
                            for (int sx = 0; sx < S; sx++)
                            {
                                var baseline = gy == 0;
                                int s1 = Shade(bv), s2 = Shade(rv);
                                bmp.SetPixel(x * S + sx, y * S + sy, baseline && s1 > 128 ? System.Drawing.Color.FromArgb(255, 180, 180) : System.Drawing.Color.FromArgb(s1, s1, s1));
                                bmp.SetPixel((cellW + x) * S + sx, y * S + sy, baseline && s2 > 128 ? System.Drawing.Color.FromArgb(255, 180, 180) : System.Drawing.Color.FromArgb(s2, s2, s2));
                            }
                    }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
