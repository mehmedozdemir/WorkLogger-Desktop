# ADR-0003: Yorumlama ve özetleme yapay zekâ kullanmaz

- Durum: Kabul edildi
- Tarih: 2026-11-12

## Bağlam

Pencere başlıkları kurum içi bilgi içerebilir. Kullanıcının tercihi, yorumlamada yapay
zekâ kullanılmaması ve gerekirse yalnızca yerel bir model kullanılmasıdır.

## Karar

Gün sonu özeti, öne çıkanlar, standup metni ve worklog açıklamaları **kural tabanlı**
üretilir (`DaySummarizer`, `WorklogPlanner`):

- Bütün sayılar analizden gelir. Cümleler sabit şablonlarla kurulur.
- Konular, pencere başlıklarından uygulama son ekleri ve issue key'ler temizlenerek çıkarılır
  (`TitleNormalizer`).
- Worklog açıklaması varsa commit mesajlarından, yoksa en çok zaman alan konu başlıklarından
  oluşur.
- Türkçe çekim ekleri sayıya göre değiştiği için ("%87'si", "%31'i") ekli kalıplardan
  kaçınılır.

## Sonuçlar

- Çıktı deterministiktir ve test edilebilir; aynı veri her zaman aynı metni üretir.
- Hiçbir veri cihazdan çıkmaz.
- Metinler serbest anlatım kadar akıcı değildir. İleride isteğe bağlı bir **yerel** model
  (ör. Ollama) eklenebilir; o durumda da sayılar yine kodla hesaplanır.
