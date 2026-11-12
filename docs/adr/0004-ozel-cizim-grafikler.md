# ADR-0004: Grafikler özel çizilir

- Durum: Kabul edildi
- Tarih: 2026-11-12

## Bağlam

Yaygın Avalonia grafik kütüphanesi LiveCharts2'nin güncel sürümü (2.0.x) Avalonia 11'e
bağlıdır. Zaman çizelgesi ve takvim ısı haritası gibi ihtiyaçlar zaten standart grafik
tiplerinin dışında kalıyor.

## Karar

Grafikler `ChartBase` üzerinden `DrawingContext` ile çizilir: `TimelineBar`, `DonutChart`,
`BarChart`, `CalendarHeatmap`, `MatrixHeatmap` ve `ShareBar`. Renkler tema kaynaklarından
gelir. Her issue'ya FNV-1a hash ile kalıcı bir renk atanır.

## Sonuçlar

- Dış bağımlılık ve sürüm uyumsuzluğu riski yoktur. Görsel dil tamamen bizim kontrolümüzdedir.
- Fareyle üzerine gelince ipucu ve tıklama (ör. güne git) ortak tabanda çözülür.
- Yakınlaştırma ve kaydırma gibi gelişmiş etkileşimler gerektiğinde ayrıca yazılmalıdır.
