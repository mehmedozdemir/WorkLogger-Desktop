# Değişiklik günlüğü

Bu projedeki tüm önemli değişiklikler bu dosyada tutulur.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) esaslıdır; sürümleme
[Semantic Versioning](https://semver.org/lang/tr/) kurallarına uyar.

## [Yayınlanmadı]

## [0.1.0] - 2026-11-12

İlk sürüm. Python tabanlı Tempo Tracker'ın .NET 10 + Avalonia ile baştan yazılmış hâli.

### Eklendi
- Arka plan izleyici (Agent): ön plan penceresi, boşta kalma, kilit ekranı, uyku/uyanma ve
  gece yarısı ayrımı. Açık segment 30 saniyede bir kaydedilir. Tek exe, `agent` argümanıyla
  ayrı süreç olarak çalışır.
- Issue sınıflandırma: pencere başlığındaki key, kullanıcı kuralları (`desen => KEY`),
  izin verilen projeler, yoksayılan key'ler ve git commit/branch eşleştirmesi.
- Gün analizi: aktif/boşta süre, issue'ya eşlenme oranı, odak blokları (25 dk ve üzeri), iş
  değişimi sayısı, saatlik dağılım, mesai dışı çalışma.
- Kural tabanlı gün sonu özeti: anlatı, öne çıkanlar, standup metni (yapay zekâ kullanılmaz).
- Worklog önerileri: yuvarlama, en kısa süre, günlük hedefe orantılı dengeleme, Excel/Tempo'ya
  yapıştırılabilir tablo.
- Masaüstü arayüzü: Gün, Geçmiş ve raporlar, Ayarlar ekranları; açık/koyu tema; tepsi menüsü;
  gün sonu saatinde otomatik açılma.
- Özel çizilmiş grafikler: zaman çizelgesi, halka grafik, yığılmış çubuk grafik, takvim ısı
  haritası, haftanın günü × saat ısı haritası.
- Geçmiş raporlar ve CSV dışa aktarma (günlük toplamlar, issue toplamları).
- Eski Python Tempo Tracker veritabanını içe aktarma.
- SQLite şema göçleri, ayar doğrulama, bozuk ayar dosyasında varsayılanlarla devam etme.

### Python sürümüne göre düzeltilenler
- Uyku/hazırda bekleme süresi artık çalışma sayılmıyor.
- Boşta eşiğine kadar geçen süre (varsayılan 5 dk) artık aktif sayılmıyor; boşta başlangıcı
  geriye tarihleniyor.
- Yalnızca commit ile görülen issue'lara hayali 15 dakikalık efor yazılmıyor.
- Zaman çizelgesinde uzun segmentler doğru saat dilimlerine bölünüyor.
- 30 saniyeden kısa segmentler atılmıyor; toplam süre kaybolmuyor.
- Tarayıcı tabanlı rapor ve yerel HTTP sunucusu kaldırıldı (XSS ve token sızıntısı riski yok).

[Yayınlanmadı]: https://github.com/mehmedozdemir/WorkLogger/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/mehmedozdemir/WorkLogger/releases/tag/v0.1.0
