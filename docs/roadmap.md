# Yol haritası

Önceliklendirme kullanıcı geri bildirimine göre değişebilir.

## 0.2 — Gün sonu akışı ✔ (yayınlandı)
- Kalıcı worklog taslakları, elle satır ekleme
- Tek tıkla atama, komşu issue önerisi, atamadan kural oluşturma (30 günlük önizleme)
- "Ne yaptım?" aralık özetleri
- Günü kapatma ve kapanmamış gün hatırlatması

## 0.2.x — küçük iyileştirmeler
- Ayarlar'da ayrı bir Kurallar ekranı: listeleme, düzenleme, son 30 güne karşı canlı önizleme
- Geçmiş takviminde kapatılmış günlerin işaretlenmesi

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
