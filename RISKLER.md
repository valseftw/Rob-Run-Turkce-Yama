# Kurmadan önce okuyun: riskler

Bu yama **resmi değildir**. Rob Run: Türkiye Official Server oyuncu topluluğu tarafından hazırlanmıştır; ROB RUN'ın geliştiricisi Kefir tarafından yapılmamıştır ve onaylanmamıştır.

## 1. Hesap kapatma (ban) riski var
- ROB RUN, çekirdek seviyesinde çalışan **Easy Anti-Cheat** kullanır.
- Yama **oyun klasöründeki hiçbir dosyayı değiştirmez**. Türkçe dosyaları, oyunun zaten kullandığı Unity önbellek klasörüne koyar ve oyun açılışta bunları yükler. Bu nedenle "değiştirilmiş oyun dosyası" denetimine takılma ihtimali, dosya değiştiren yamalara göre daha düşüktür.
- Ama risk **sıfır değildir**. Oyun yine de değiştirilmiş içerik yükler. Anti-cheat'in neyi denetlediğini dışarıdan kimse bilemez; "%0 ban riski" diyen hiçbir yamaya inanmayın.
- Oyunun kullanım koşulları (EULA, madde 1(a)) yazılımı **değiştirmeyi ve çevirmeyi** yasaklar. Kefir bu yüzden hesabı uyarı vermeden askıya alabilir veya kapatabilir (madde 3 ve 9). Bu durumda oyundaki eşyalar ve para da kaybedilebilir (madde 4).
- Bu yama hile değildir. Anti-cheat'i kapatmaz veya atlatmaz, oyun koduna ya da belleğe dokunmaz. Ama bunun ban'e karşı **hiçbir garantisi yoktur**. Riski kabul eden kişi siz olursunuz.

## 2. Yama ne yapar?
Oyunun önbellek klasörüne (`%USERPROFILE%\AppData\LocalLow\Kefir\ROB RUN\com.unity.addressables\`) dört dosya yazar:
- **Türkçe metin paketi:** Oyunun İngilizce metin paketinin, metinleri Türkçeye çevrilmiş kopyası.
- **Türkçe yazı tipi paketi:** Oyunun yazı tiplerinde ı İ ş Ş ğ Ğ harfleri yoktur. Bu harfler, yazı tipindeki mevcut harf parçaları birleştirilerek eklenir; mevcut harflere dokunulmaz.
- **Yönlendirme kataloğu ve özeti:** Oyunun kendi dosya kataloğunun bir kopyasıdır. Bu kopyada yalnızca iki paketin adresi, önbellekteki Türkçe kopyaları gösterecek şekilde değiştirilir. Oyunun kendi özet dosyası da yanına kopyalanır.

Oyun klasörü (Steam), oyun programı, anti-cheat dosyaları ve kayıtlarınız **değişmez**. Program yönetici izni istemez.

**İnternet:** Program yalnızca **GitHub**'a bağlanır, yeni çeviri ve program sürümü olup olmadığına bakar. Hiçbir veri göndermez; telemetri yoktur. İndirilen her dosyanın **dijital imzası** doğrulanır, doğrulanamayan dosya hiç kullanılmaz.

## 3. Geri alma
- Programdaki **"Yamayı Kaldır"** düğmesi bu dört dosyayı ve "ROB RUN Türkçe" kısayollarını siler; oyun bir sonraki açılışta İngilizce başlar.
- Elle kaldırmak için `%USERPROFILE%\AppData\LocalLow\Kefir\ROB RUN\com.unity.addressables` klasörünü silebilirsiniz. Oyun bu klasörü gerekirse kendisi yeniden oluşturur.
- Not: Steam'in "Oyun dosyalarının bütünlüğünü doğrula" özelliği bu klasöre bakmaz; yamayı kaldırmaz.

## 4. Oyun güncellenince
Güncellemeyle oyunun katalog adı değişir. Oyun önbellekteki eski Türkçe kataloğu kullanmaz ve kendiliğinden İngilizce başlar; bozulma olmaz.
- Oyunu masaüstündeki **"ROB RUN Türkçe"** kısayoluyla açarsanız Türkçe kendiliğinden yeniden uygulanır ve oyun Steam üzerinden başlar.
- Kısayol kullanmıyorsanız programı açıp **"Yeniden Kur"** demeniz yeterlidir.
- Yeni eklenen metinler, çevirileri gelene kadar İngilizce kalır.

## 5. Dil ayarı
Türkçe metinler İngilizcenin yerine geçer. Oyunda dil olarak **English** seçili olmalıdır.
