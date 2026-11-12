# ADR-0002: İzleyici ayrı süreç, aynı exe

- Durum: Kabul edildi
- Tarih: 2026-11-12

## Bağlam

Ürünün asıl değeri kesintisiz veri toplamaktır. Arayüz çökerse, güncellenirse ya da
kapatılırsa izleme durmamalı. Öte yandan iki ayrı exe kurulumu, otomatik başlatmayı ve
sürüm yönetimini karmaşıklaştırır.

Windows Service seçeneği yok: Session 0'da çalışan bir servis kullanıcının ön plan
penceresini göremez.

## Karar

- Tek `WorkLogger.exe` iki rolde çalışır. `WorkLogger.exe agent` arayüzsüz izleyicidir; sade
  `WorkLogger.exe` masaüstü uygulamasıdır.
- Desktop açılırken Agent çalışmıyorsa onu ayrı bir süreç olarak başlatır. Her rol kendi
  isimli mutex'iyle tek örnek çalışır.
- İki süreç aynı SQLite dosyasını WAL modunda paylaşır. İzleme tablolarına yalnızca Agent
  yazar.
- Süreçler arası iletişim `agent_state` tablosu üzerinden yürür: durum ve
  duraklat/kapat komutları. Named pipe gibi ek bir IPC kurulmaz.

## Sonuçlar

- Arayüz sorunları veri toplamayı etkilemez.
- Tek exe tek sürüm demektir; Agent ile Desktop arasında sürüm uyumsuzluğu oluşmaz.
- Komutlar en geç bir örnekleme aralığında (2 sn) işlenir; bu gecikme bu kullanım için yeterli.
