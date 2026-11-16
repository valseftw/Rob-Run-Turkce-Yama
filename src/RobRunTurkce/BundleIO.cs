using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RobRunTurkce
{
    /// <summary>Unity AssetBundle dosyalarını okuma / değiştirip yeniden yazma yardımcıları.</summary>
    public static class BundleIO
    {
        /// <summary>Paketi belleğe açar (sıkıştırılmışsa açar). Dosya kilitli tutulmaz.</summary>
        public static BundleFileInstance Open(AssetsManager am, string path)
        {
            var ms = new MemoryStream(File.ReadAllBytes(path));
            return am.LoadBundleFile(ms, path, true);
        }

        /// <summary>Değiştirilmiş paketi LZ4 ile sıkıştırarak <paramref name="outPath"/> dosyasına yazar.</summary>
        public static void Save(BundleFileInstance bun, AssetsFileInstance inst, int dirIndex, string outPath)
        {
            bun.file.BlockAndDirInfo.DirectoryInfos[dirIndex].SetNewData(inst.file);

            var raw = new MemoryStream();
            var w1 = new AssetsFileWriter(raw);
            bun.file.Write(w1);

            var unpacked = new AssetBundleFile();
            unpacked.Read(new AssetsFileReader(new MemoryStream(raw.ToArray())));
            using (var fs = File.Create(outPath))
            {
                var w = new AssetsFileWriter(fs);
                // blockDirAtEnd: false -> blok/dizin listesi Unity'nin kendi ürettiği gibi başlığın hemen arkasına yazılır.
                // (true, kütüphanenin varsayılanıdır ama Unity'nin resmi olarak kullanmadığı bir düzendir.)
                unpacked.Pack(w, AssetBundleCompressionType.LZ4, false);
                w.Flush();
            }
            unpacked.Close();
            Verify(outPath, bun.file.BlockAndDirInfo.DirectoryInfos.Count);
        }

        /// <summary>
        /// Yazılan paketi baştan okuyup açar; okunamazsa oyuna hiç yazılmadan hata verir.
        /// </summary>
        public static void Verify(string path, int expectedDirs)
        {
            var am = new AssetsManager();
            try
            {
                var b = Open(am, path);
                if (b.file.BlockAndDirInfo.DirectoryInfos.Count != expectedDirs)
                    throw new InvalidDataException("Yazılan paketin içerik listesi beklenenden farklı.");
                if (b.file.Header.Signature != "UnityFS")
                    throw new InvalidDataException("Yazılan paketin başlığı geçersiz.");
                for (int i = 0; i < expectedDirs; i++)
                    if (b.file.IsAssetsFile(i)) am.LoadAssetsFileFromBundle(b, i, false);
            }
            catch (Exception e) when (!(e is InvalidDataException))
            {
                throw new InvalidDataException("Yazılan paket doğrulanamadı: " + e.Message, e);
            }
            finally { am.UnloadAll(true); }
        }

        /// <summary>Paketteki .resS akış verisinden (ör. doku pikselleri) bir bölümü okur.</summary>
        public static byte[] ReadStreamData(BundleFileInstance bun, string streamPath, long offset, int size)
        {
            string name = Path.GetFileName(streamPath.Replace("archive:/", ""));
            var dirs = bun.file.BlockAndDirInfo.DirectoryInfos;
            for (int i = 0; i < dirs.Count; i++)
            {
                if (!string.Equals(dirs[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                var r = bun.file.DataReader;
                r.Position = dirs[i].Offset + offset;
                return r.ReadBytes(size);
            }
            throw new FileNotFoundException("Paket içinde akış verisi bulunamadı: " + streamPath);
        }
    }
}
