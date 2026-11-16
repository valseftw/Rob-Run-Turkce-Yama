# Çeviri rehberi

Hedef: oyuncu dilinde, doğal, kısa ve net bir Türkçe. Hitapta "sen" kullanılır.

## Terim sözlüğü

| İngilizce | Türkçe | Not |
|---|---|---|
| Fence | Tefeci | Çalıntı mal alan NPC. "Fence" çit anlamına geliyorsa (Farm Fence) → çit |
| Lair | Sığınak | |
| Bunker | Bunker | Sığınak ile karışmasın |
| Wipe | wipe | Çevrilmez |
| Settlement | Yerleşim | |
| Tier / Level (district) | Kademe / Seviye | Arayüzde "1. Seviye Bölge" |
| Notoriety | Aranma seviyesi | |
| Wanted | Aranıyor | Sivil öldürünce artan ayrı bir gösterge; "En Çok Aranan" |
| Laundering | Para aklama | Laundering Desk → Aklama Masası |
| Dirty cash / Cash | Kara para / Nakit | |
| Credits | Kredi | Banka hesabındaki temiz para |
| District | Bölge | |
| State (realm) | Eyalet | |
| Clan | Klan | |
| Party | Grup | |
| Claim / Sit-down | Talep / Masa | Klan çatışması öncesi aşama |
| Conflict / Showdown | Çatışma / Hesaplaşma | |
| Shipments | Sevkiyatlar | Açılabilir ürün ağacı |
| Black Market | Karaborsa | |
| Smuggler | Kaçakçı | |
| Contraband | Kaçak mal | |
| Cargo | Kargo | |
| Workbench | Tezgâh | |
| Barter | Takas | |
| Loot | Ganimet | |
| Breach | Kır / Kırma | |
| Decoder | Şifre Çözücü | |
| Fuse | Sigorta | |
| Raid block | Baskın kilidi | |
| Body Armor | Çelik Yelek | |
| Criminal / Civilian / Police | Suçlu / Sivil / Polis | |
| Authority / Reputation | İtibar | |
| Jail time | Hapis süresi | |
| Confiscation | El koyma | |
| Feds | Federaller | |
| Skin | Kostüm / Kaplama | Karakter → kostüm, araç ve silah → kaplama |
| Emote | İfade | |

**Çevrilmeyenler:** özel isimler (Looty Bay, Lenny, Larry, Highlands, Loraine Hills, Palmside...), marka ve model adları (Buletti Soltempra, KL-545 Mk1, POLEX Saat, AIRHEADS Kulaklık), kostüm stil adları (Hornet Cook, Snow Cartel...). Genel kelimeler çevrilir: "Rosefield Bottle" → "Rosefield Şişesi", "Cobra Paint" → "Cobra Boyası".

## Biçim kuralları (yükleyici bunları denetler)

- `<color=#{:palette(Main)}>...</color>`, `<link=...>`, `<nobr>`, `<size>`, `<uppercase>` gibi etiketler aynı sayıda ve aynı türde kalmalıdır. Yerleri cümleye göre değişebilir.
- `{0}`, `{1}`, `{0:time:}`, `{0:N0}`, `{distance:N0}` gibi yer tutucular aynen korunmalıdır.
- SmartFormat ifadelerinde `|` ayırıcı sayısı aynı kalmalıdır. Türkçede tekil ve çoğul aynıdır: `{0:plural:{} yıl|{} yıl|{} yıl}`.
- `\n` satır sonudur, korunmalıdır. `\u00A0` gibi kaçış dizileri aynen kalır.
- Yüzde işareti Türkçe kullanımda sayıdan önce gelir: `%{0}`.
- Bu kurallara uymayan bir çeviri oyuna yazılmaz. Yerine İngilizce metin kalır ve kurulum raporunda listelenir.

## Uzunluk

Arayüz düğmeleri ve başlıklar dar alanlara sığmalıdır. Türkçe çeviri, İngilizce metinden en fazla yaklaşık %30 uzun olmalıdır. Düğmelerde emir kipi kullanılır ve metin kısa tutulur: "Kilidi Aç", "Satın Al", "Hepsini Al".
