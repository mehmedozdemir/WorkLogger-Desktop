# Mimari

## Hedef

Tek bir kullanıcının Windows'taki çalışmasını kesintisiz kaydetmek. Gün sonunda bunu
güvenilir sayılara, okunur bir özete ve worklog önerilerine dönüştürmek. Geçmişin tamamını
raporlanabilir tutmak.

Kapsam dışı (bilinçli kararlar):
- Yapay zekâ ile yorumlama ([ADR-0003](adr/0003-yapay-zekasiz-yorumlama.md))
- Takvim/toplantı entegrasyonu
- Ekip paneli ve merkezi sunucu

## Süreçler

```
┌─────────────────────────────┐        ┌─────────────────────────────┐
│ WorkLogger.exe (Desktop)    │        │ WorkLogger.exe agent        │
│ Avalonia UI, tepsi, raporlar│        │ arayüzsüz izleyici          │
│  • okur: segments, commits  │        │  • yazar: segments, commits │
│  • yazar: agent_state komut │◀──────▶│  • yazar: agent_state durum │
└──────────────┬──────────────┘ SQLite └──────────────┬──────────────┘
               └───────────── worklogger.db (WAL) ────┘
```

İzleme ve arayüz neden ayrı süreçlerde çalışıyor, ve neden ikisi de aynı exe: bkz.
[ADR-0002](adr/0002-surec-modeli.md).

Süreçler arası iletişim yalnızca `agent_state` tablosu üzerinden yürür:

| Anahtar | Yazan | Anlamı |
| --- | --- | --- |
| `heartbeat` | Agent | 10 sn'de bir; 40 sn eskiyse Desktop "izleyici çalışmıyor" gösterir |
| `current_app`, `current_title`, `current_since_ms`, `current_idle` | Agent | Canlı durum |
| `pause_requested` | Desktop | `1` ise Agent açık segmenti kapatır ve beklemeye geçer |
| `shutdown_requested` | Desktop | `1` ise Agent segmenti kapatıp çıkar |

## Katmanlar

```
WorkLogger.Desktop ──▶ WorkLogger.Agent ──▶ WorkLogger.Infrastructure ──▶ WorkLogger.Core
     (Avalonia)          (hosted services)      (SQLite, Win32, git)         (saf mantık)
```

`WorkLogger.Core` hiçbir dış bağımlılık taşımaz. İzleme durum makinesi, analiz ve özetleme
burada, birim testlerle doğrulanır.

## İzleme durum makinesi

`TrackingStateMachine` her gözlemi (zaman, uygulama, başlık, boşta süresi, kilit) alır ve en
fazla bir segment kapatıp bir segment açar:

| Durum | Davranış |
| --- | --- |
| Pencere/başlık değişti | Önceki segment şimdi kapanır |
| Boşta eşiği aşıldı | Aktif segment **son girdi anına** geri tarihlenerek kapanır |
| İki gözlem arası > 3 × aralık + 15 sn (uyku, donma) | Segment son görülme anında kapanır; aradaki süre yazılmaz |
| Gece yarısı | Segment ikiye bölünür |
| Kilit ekranı / yoksayılan uygulama | Boşta segmenti |

Ayrıca Agent, `SystemEvents.PowerModeChanged` (uyku) ve `SessionSwitch` (kilit) olaylarını dinler.

## Analiz

`DayAnalyzer` bir günün segmentlerini ve commit'lerini şunlara çevirir:

| Ölçüm | Tanım |
| --- | --- |
| Aktif süre | Boşta olmayan segmentlerin toplamı |
| Boşta süre | İlk ve son aktivite arasındaki boşta segmentler (gece ve mesai sonrası sayılmaz) |
| Eşlenme oranı | Issue'ya bağlanan süre / aktif süre |
| İş birimi | Aynı etiketin yakın parçaları birleşir; 3 dk'dan kısa araya girmeler ayıklanır |
| Odak bloğu | 25 dk ve üzeri iş birimi |
| İş değişimi | İş birimi sayısı − 1 |
| Mesai dışı | Ayarlanan mesai saatleri dışında ve hafta sonu geçen aktif süre |

`PeriodAnalyzer` aynı analizi her gün için çalıştırıp dönem toplamlarını çıkarır. 37 günlük
gerçek veri (6 bin segment) yaklaşık 0,2 saniyede işlenir.

## Veri

SQLite, `PRAGMA user_version` ile sürümlenen ve sırayla uygulanan göçler
([Database.cs](../src/WorkLogger.Infrastructure/Data/Database.cs)). Zamanlar Unix milisaniye,
gün anahtarı yerel tarih olarak saklanır.

| Tablo | İçerik |
| --- | --- |
| `segments` | Ham izleme verisi (`source`: `agent` / `python-import`) |
| `commits` | Git commit'leri, sha ile tekil; dosya, ekleme ve silme sayıları |
| `agent_state` | Canlı durum ve komutlar |

Ayarlar `settings.json` dosyasında durur. Kaydetmeden önce doğrulanır; bozuk dosya varsa
varsayılanlarla devam edilir ve kullanıcı uyarılır.
