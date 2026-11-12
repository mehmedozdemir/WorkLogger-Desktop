# ADR-0005: SQLite + Dapper, elle yazılmış göçler

- Durum: Kabul edildi
- Tarih: 2026-11-12

## Karar

Veri katmanında Microsoft.Data.Sqlite ve Dapper kullanılır. Şema göçleri
`PRAGMA user_version` ile sürümlenen SQL betikleridir. EF Core kullanılmaz.

## Gerekçe

Şema küçük, sorgular basit ve okunur kalıyor. İki süreç aynı dosyayı paylaştığı için kısa
ömürlü bağlantılar ve açık SQL tercih edildi. EF Core'un model, göç ve araç yükü bu
ölçekte gereksiz.
