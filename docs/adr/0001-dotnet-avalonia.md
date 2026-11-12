# ADR-0001: .NET 10 ve Avalonia ile yeniden yazım

- Durum: Kabul edildi
- Tarih: 2026-11-12

## Bağlam

Önceki sürüm Python ile yazılmıştı: tepsi ikonu ve tarayıcıda açılan bir HTML rapordan
oluşuyordu. Hedef ise zengin raporlama ve görselleştirme sunan, profesyonel bir masaüstü
uygulaması. Kurulum Python gerektirmemeli, tüm işlemler uygulamanın içinde yapılmalı.

## Karar

Uygulama tamamen .NET 10 (LTS) ile yazılır. Arayüz için Avalonia 12 kullanılır. Python kodu
taşınmaz; yalnızca verisi içe aktarılır.

## Sonuçlar

- Tek dil ve tek araç zinciri kullanılır. Win32 API'lerine doğrudan P/Invoke ile erişilir.
- Bağımsız (self-contained) tek exe dağıtılır; .NET kurulumu gerekmez.
- Avalonia çapraz platformdur, ancak izleme katmanı Win32'ye bağlıdır. Uygulama yalnızca
  Windows hedefler.
- .NET 11 bir STS sürümü olduğu için LTS olan .NET 10 seçildi.
