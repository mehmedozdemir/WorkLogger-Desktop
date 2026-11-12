# WorkLogger

Windows'ta gün boyu ne üzerinde çalıştığınızı kendiliğinden kaydeden, gün sonunda bunu
okunur bir özete ve worklog önerilerine dönüştüren masaüstü uygulaması.

- **Arka planda izler.** Ön plandaki pencereyi ve boşta kalma süresini kaydeder. Uyku, kilit
  ekranı ve gece yarısı geçişleri doğru ayrılır.
- **Issue'ları bulur.** Pencere başlığındaki `ABC-123` desenlerini, sizin yazdığınız kuralları ve
  git commit/branch adlarını kullanır.
- **Gün sonunda yorumlar.** Günün anlatısını, öne çıkan noktaları ve standup metnini kurallarla
  üretir. **Yapay zekâ kullanmaz**; her cümle doğrudan bir ölçüme dayanır.
- **Geçmişi raporlar.** Günlük süre grafiği, çalışma takvimi, proje ve issue dağılımı,
  haftalık ritim ısı haritası ve CSV dışa aktarma sunar.
- **Veri sizde kalır.** Her şey `%LOCALAPPDATA%\WorkLogger` altındaki SQLite dosyasında tutulur.
  Uygulama ağa veri göndermez.

> Durum: **v0.1.0**, ilk sürüm. Jira/Tempo'ya doğrudan gönderim henüz yok; worklog tablosu
> panoya kopyalanıp yapıştırılır. Yol haritası için [docs/roadmap.md](docs/roadmap.md).

## Kurulum

1. [Releases](https://github.com/mehmedozdemir/WorkLogger-Desktop/releases) sayfasından
   `WorkLogger-x.y.z-win-x64.zip` dosyasını indirin.
2. Zip'i kalıcı bir klasöre açın (ör. `%LOCALAPPDATA%\Programs\WorkLogger`).
3. `WorkLogger.exe`'yi çalıştırın. .NET kurulumu gerekmez.
4. **Ayarlar → Genel → Oturum açılışında başlat** seçeneğini açın.

Yönetici yetkisi gerekmez. Kaldırmak için: oturum açılışında başlatmayı kapatın, tepsideki
menüden **İzlemeyi durdur ve çık**'ı seçin ve klasörü silin. Verileri de silmek isterseniz
`%LOCALAPPDATA%\WorkLogger` klasörünü silin.

### Eski Tempo Tracker (Python) verisi

Önceki Python sürümünü kullandıysanız **Ayarlar → Veri → İçe aktar** ile
`%USERPROFILE%\TempoTracker\activity.db` dosyasındaki tüm geçmiş aktarılır. İşlem tekrar
çalıştırılabilir; mükerrer kayıt oluşmaz.

## Kullanım

| Ekran | Ne yapar |
| --- | --- |
| **Gün** | Aktif süre, issue'ya eşlenme oranı, odak süresi ve günlük hedef kartları. Zaman çizelgesi, issue ve uygulama dağılımı, gün sonu özeti, worklog önerileri, sınıflandırılmamış işler, saatlik dağılım ve commit'ler. Günler arasında ‹ › ile gezilir. |
| **Geçmiş ve raporlar** | 7/30/90 gün, bu yıl veya tüm geçmiş. Günlük süre grafiği (bir çubuğa tıklayınca o gün açılır), çalışma takvimi, projeler, en çok zaman alan issue'lar, haftanın günü × saat ritmi, uygulamalar ve CSV dışa aktarma. |
| **Ayarlar** | Gün sonu saati, günlük hedef, boşta eşiği, mesai saatleri, sınıflandırma kuralları, yuvarlama, git depoları ve veri. |
| **Tepsi** | Pencereyi açma, bugünün özeti, duraklat/sürdür, kapatma. |

Pencereyi kapatmak uygulamayı kapatmaz, yalnızca tepsiye küçültür. İzleme ayrı bir süreçte
sürer. Gün sonu saatinde (varsayılan 18:00) pencere günün özetiyle kendiliğinden açılır.

### Sınıflandırma kuralları

Başlıkta issue key geçmeyen işler için **Ayarlar → Sınıflandırma → Kurallar** alanına her
satıra bir kural yazılır:

```
Odeme\.sln            => PAY-1042
Teams.*Günlük toplantı => INT-7
# devre dışı kural    => ABC-1
```

Sol taraf, `uygulama.exe pencere başlığı` metninde aranan düzenli ifadedir (büyük/küçük harf
duyarsız). Başlıkta bir issue key varsa kurallardan önce o kullanılır.

## Nasıl çalışır

```
WorkLogger.exe            → Avalonia masaüstü uygulaması (arayüz, raporlar, tepsi)
WorkLogger.exe agent      → arayüzsüz izleyici (ayrı süreç, aynı exe)
            ↘            ↙
       worklogger.db (SQLite, WAL)
```

- **Agent** 2 saniyede bir ön plan penceresini ve son girdi zamanını okur. Pencere değişince bir
  *segment* kapanır. Açık segment 30 saniyede bir kaydedilir. Uyku/uyanma ve kilit olaylarını
  dinler; iki gözlem arasında beklenmedik bir boşluk varsa o süreyi saymaz.
- **Desktop** veriyi okur ve analiz eder. Arayüz çökse ya da kapatılsa bile izleme sürer.
- Tüm hesaplamalar `WorkLogger.Core` içinde, işletim sisteminden bağımsız ve test edilebilir
  şekilde yapılır.

Ayrıntılar: [docs/architecture.md](docs/architecture.md) ve [docs/adr/](docs/adr/).

## Geliştirme

Gereksinimler: Windows 10/11, .NET SDK 10.0.4xx.

```powershell
dotnet build
dotnet test
dotnet run --project src/WorkLogger.Desktop
```

Kendi verinize dokunmadan denemek için veri klasörünü değiştirebilirsiniz:

```powershell
$env:WORKLOGGER_HOME = "$env:TEMP\worklogger-dev"
dotnet run --project src/WorkLogger.Desktop
```

| Proje | Sorumluluk |
| --- | --- |
| `WorkLogger.Core` | Alan modeli, izleme durum makinesi, gün/dönem analizi, worklog önerileri, kural tabanlı özetleyici |
| `WorkLogger.Infrastructure` | SQLite (şema göçleri), Win32 pencere/boşta okuma, git tarayıcı, ayarlar, içe aktarma |
| `WorkLogger.Agent` | Arka plan izleyici (hosted services) |
| `WorkLogger.Desktop` | Avalonia arayüzü, özel çizilmiş grafikler, tepsi |

## Sürümleme

[Semantic Versioning](https://semver.org/lang/tr/) kullanılır. Sürüm numarası tek yerden,
[Directory.Build.props](Directory.Build.props) içindeki `<Version>` alanından yönetilir.
Değişiklikler [CHANGELOG.md](CHANGELOG.md) dosyasında tutulur. `vX.Y.Z` etiketi
gönderildiğinde GitHub Actions paketi üretir ve release'i yayınlar.

## Lisans

[MIT](LICENSE). Kullanılan tüm bileşenler açık kaynaktır: Avalonia (MIT),
CommunityToolkit.Mvvm (MIT), Microsoft.Data.Sqlite (MIT), Dapper (Apache-2.0), Serilog (Apache-2.0).
