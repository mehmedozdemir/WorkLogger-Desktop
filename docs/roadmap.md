# Yol haritası

Önceliklendirme kullanıcı geri bildirimine göre değişebilir.

## 0.2 — Gün sonu akışı ve kalıcı düzenlemeler
- Worklog düzenlemelerinin kalıcı saklanması (`worklog_drafts` tablosu)
- Sınıflandırılmamış bir bloğu tek tıkla bir issue'ya atama ve "bundan sonra hep böyle" diyerek
  kural oluşturma
- Kurallar ekranı: son 30 güne karşı canlı önizleme
- "Son 2 saat" gibi aralık özetleri (tepsiden)
- Günü "kapatma" ve kapanmamış günler için hatırlatma

## 0.3 — Jira/Tempo entegrasyonu (isteğe bağlı, varsayılan kapalı)
- Kurum içi Jira Server/Data Center: kişisel erişim token'ı ile issue doğrulama ve başlık çekme
- Tempo Timesheets'e worklog gönderme ve güncelleme, mükerrer kayıt koruması
- Token'ların Windows Credential Manager'da saklanması

## 0.4 — Raporlama derinliği
- Issue ekranı: bir issue'nun tüm geçmişi, gün dağılımı, commit zaman çizgisi
- Haftalık timesheet görünümü (gün × issue)
- PDF ve Excel dışa aktarma (açık kaynak kütüphanelerle)
- Kaydedilmiş rapor filtreleri

## Sonra
- İsteğe bağlı yerel model (Ollama) ile serbest anlatımlı özet; sayılar yine kodla hesaplanır
- Ham verinin saklama süresi ve temizleme
- Otomatik güncelleme
- Ekip kullanımı için kurulum paketi
