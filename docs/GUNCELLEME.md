# Oyun güncellenince yamayı yenileme

## Oyuncular için

- Oyunu **"ROB RUN Türkçe"** kısayoluyla açıyorsanız bir şey yapmanıza gerek yok. Başlatıcı oyunun güncellendiğini fark eder, Türkçeyi yeniden uygular ve oyunu açar.
- Kısayol kullanmıyorsanız:
  1. Steam güncellemesi bittikten sonra oyunu kapatın.
  2. `RobRunTurkceYama.exe` dosyasını çalıştırın, Kurulum sayfasında **Yeniden Kur** düğmesine basın.

Arka planda olanlar:
- Güncellemeyle oyunun katalog dosyasının adı (`catalog_<tarih>`) değişir. Oyun, önbellekteki eski Türkçe kataloğu artık kullanmaz ve İngilizce açılır. Bu güvenlidir, bir şey bozulmaz.
- "Yeniden Kur", oyunun **yeni** İngilizce metin ve yazı tipi paketlerinden Türkçe kopyaları yeniden üretir. Yeni kataloğu yönlendirir ve önbelleğe yazar. Önceki kurulumdan kalan, artık kullanılmayan dosyaları (ör. eski katalog) da siler.

## Çevirmenler / bakım yapanlar için

1. Oyunu güncelleyin.
2. Oyundaki güncel İngilizce metinleri ve mevcut çevirileri dışa aktarın:
   ```bash
   RobRunTurkceYama.exe --disa-aktar kaynak-en.json
   ```
   Her kayıt `{"h": özet, "en": İngilizce, "tr": çeviri}` biçimindedir. `tr` alanı boşsa metin yeni demektir. `h` alanı `translations/tr.json` dosyasındaki özetten farklıysa İngilizcesi değişmiştir. (`kaynak-en*.json` dosyaları `.gitignore` içindedir; Kefir'in metinlerini depoya koymayın.)
3. Yeni ve değişen metinleri `translations/tr.json` dosyasına ekleyin ya da güncelleyin. `h` alanına dışa aktarmadaki yeni özeti yazın.
4. Denetleyin (oyuna ve önbelleğe dokunmaz):
   ```bash
   RobRunTurkceYama.exe --ceviri translations/tr.json --kopyaya C:\deneme
   ```
   Rapordaki "hatalı" satırlar biçimi bozan çevirilerdir. Bunlar oyuna yazılmaz, yerlerinde İngilizce kalır.
5. `translations/tr.json` içindeki `_bilgi.surum` değerini artırın (ör. `1.0.0` → `1.0.1`). Programda değişiklik yaptıysanız `RobRunTurkce.csproj` içindeki `<Version>` değerini de artırın.
6. `build.ps1` betiğini çalıştırın. Betik `dist/` klasörüne exe'yi, `tr.json` dosyasını ve gizli anahtarla imzalanmış `SHA256SUMS.txt` / `SHA256SUMS.txt.sig` çiftini koyar.
7. GitHub'da yeni bir release yayımlayın ve bu dört dosyayı ekleyin. Oyuncuların programları yeni çeviriyi kendiliğinden indirir (imzası doğrulanınca). Yeni exe ise ana pencerede "Güncelle" kartıyla sunulur, başlatıcıda kendiliğinden kurulur.

Yalnızca çeviri değiştiyse yeni exe yayımlamak gerekmez. Ama release yine de dört dosyayı içermelidir; özet listesindeki program sürümü aynı kaldığı için oyuncular exe güncellemesi görmez.

## Oyun yapısı değişirse

- Yükleyici `StreamingAssets\aa\settings.json` dosyasından önbellek özetinin adresini (`AddressablesMainContentCatalogCacheHash`) okur. Kefir önbellek desteğini kaldırırsa kurulum açık bir hatayla durur ve hiçbir dosya yazmaz.
- Katalogda iki paketin adresi tam olarak bir kez ve beklenen uzunluk alanıyla bulunamazsa kurulum durur. Adres biçimi değiştiyse `Installer.cs` dosyasındaki `OldPrefix` ve `NewPrefix` güncellenmelidir.
- Yazı tipi yaması başarısız olursa yazı tipi paketi yönlendirilmez. Metinler **harf dönüşümüyle** yazılır (ı→i, ş→s, ğ→g), böylece ekranda kutu karakterleri görünmez.
