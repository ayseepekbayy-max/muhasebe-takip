# 211. Adım — Ayrı yayın adayının PostgreSQL doğrulaması

Çalışma alanı: muhasebe-takip-release-safety-d6742c84
Dal: release/document-maintenance-d6742c84
Temel: 07793b696775ebb30a0d069751f937d5897d49e8

## Sonuç

- Core: 165 PASS, 0 FAIL.
- Documents: 45 PASS, 0 FAIL.
- Deletion: 57 PASS, 0 FAIL.
- CommitNetwork: 25 PASS, 0 FAIL, 0 BLOCKED.
- Toplam: 292 PASS, 0 FAIL, 0 BLOCKED.
- Önceki 232 SQLite testi yeniden çalıştırılmadı. Bu sonuçların tamamı yeni PostgreSQL 18.6 ortamına aittir.
- Bütün Release derlemeleri uyarısız başarılı. Süreler: ana test 90 saniye, dış süreç sınırı 100 saniye;
  ağ testi 60 saniye, dış süreç sınırı 70 saniye; tek işlem/alt süreç başlangıçlarında 5–15 saniye sınırları.

## Test altyapısındaki zorunlu değişiklikler

Yalnızca Tests/PostgresReleaseSafety altında dört yeni test projesi eklendi. Üretim dosyaları,
mevcut SQLite test programları, model ve migration dosyaları değiştirilmedi.

- Core: yalnızca PostgreSQL bağlantıları ve her fixture için yeni benzersiz veritabanı; sentetik
  tarihlerin UTC olması; gerçek ertelenmiş kısıtla COMMIT reddi (23514); iki ayrı Production test
  süreciyle kontrol dosyası kaybı/restart testi.
- Documents: mevcut korumalı belge ve dört gerçek Razor ekranı testinin PostgreSQL adaptörü.
- Deletion: çek silme testinin PostgreSQL adaptörü ve gerçek COMMIT reddi.
- CommitNetwork: test amaçlı TCP rölesi; PostgreSQL'in CommandComplete(COMMIT) mesajını gördükten
  sonra bu yanıtı istemciye aktarmadan TCP bağlantısını kapatır. Fatura, cari, çek ve ZIP restore
  için veritabanı sonucu ayrı doğrudan bağlantıdan, dosyalar bayt karşılaştırmasıyla doğrulanır.
  Bu test yalnızca NpgsqlException fırlatma simülasyonu değildir.

## Doğrulanan davranış

Bakımda dinamik GET/POST/API/Panel 503; fiziksel staging ve kodlanmış URL erişimi reddi; başlamış
istek tamamlanmadan drained=false; PostgreSQL yazımı tamamlandıktan sonra drained=true.
Production startup fallback=true iken geçici kontrol dosyası kaybından sonra yeni gerçek süreç
bakımda başlar; GET/POST 503 ve veritabanına yeni kayıt yok. Önceki sürecin kayıtları korunur.

Fatura/cari için başarılı commit, yazma hatası, gerçek COMMIT reddi, commit sonrası hata,
yabancı firma, sahte firma oturumu, pasif firma ve silinmiş kullanıcı test edildi.
Çek için commit öncesi/sonrası hatalar ve dosya temizliği hatası, firma/yol sahipliği kontrol edildi.
ZIP restore için SHA-256, aynı uploads ağacında staging, kaynak/ hedef kayıtların korunması,
kimlik eşleme, işlem dışı staging dizininin korunması ve belirsiz commit'te dosyaların kalması test edildi.
Korumalı belge, direkt uploads reddi, yol manipülasyonu, gerçek logout ve özel Panel medya sınırı geçti.

Gerçek TCP yanıt kaybında fatura/cari kayıtları commit edilmiş olarak ayrı bağlantıdan bulundu,
belgeler korundu, handler başarı vermedi. Çek satırı silinmiş bulundu ancak belge korunup güvenli
hata gösterildi. ZIP kayıtları ve dosyaları korundu, restore sonucu başarılı gösterilmedi.

## Şema sınırı

Data, Models ve Migrations temel commit ile birebir aynı. Testlerde yalnızca yeni sentetik
veritabanlarına EnsureCreated uygulandı; Migrate, migration komutu veya history işaretleme yapılmadı.
Gerçek COMMIT reddi için test veritabanlarında geçici/test amaçlı ertelenmiş kısıtlar oluşturuldu.
AdminCompanyTransitions tablosu/modeli yok. Bu, canlı PostgreSQL şema/history kontrolünün yerine geçmez.

## İzolasyon ve kaynaklar

PostgreSQL: firmova-release211-pg-69298a670baa
Linux SDK: firmova-release211-tests-69298a670baa
PostgreSQL sürümü: 18.6 (Debian 18.6-1.pgdg13+2)
Yeni PostgreSQL network=none, host portu yok, volume/bind mount yok; /var/lib/postgresql tmpfs.
Linux test konteyneri yalnızca yeni PostgreSQL'in ağ ad alanını paylaşıyor; yeni Docker ağı yok.
211. adımda 40 yeni sentetik veritabanı oluşturuldu; o adım sonunda kaynaklar silinmeden bırakıldı.
228. adımda Docker kaynaklarına bağlanılmadı; mevcut çalışma durumları yeniden sorgulanmadı.
Tmpfs kalıcı yedek değildir; PostgreSQL konteyneri durursa veri kaybolabilir.

firmova-yedek-dogrulama ve firmova-yedek-dogrulama-data kullanılmadı/değiştirilmedi. Önceki test
konteynerleri de kullanılmadı. Konteynere yalnızca DLL/deps/runtimeconfig dosyaları aktarıldı;
appsettings, belge, gerçek yedek, bağlantı sırrı veya production ortam değişkeni aktarılmadı.
Testte TLS/GSS kapalı bağlantı yalnızca bu ağsız sentetik ortamın loopback erişimidir; üretim
TLS veya güvenlik ilkeleri değiştirilmedi. Canlı Render/veritabanı erişimi olmadı.

## Yayın kararı

Bu adayın istenen PostgreSQL uygulama test kapsamı yeterli ve geçti. Bu sonuç doğrudan disk
bağlama/deploy onayı değildir. En küçük sonraki adım: canlıya hiçbir değişiklik yapmadan etkin
başlangıç/pre-deploy/bakım ayarları ile ilk geçişte eski sürecin durdurulduğunu nasıl doğrulayacağımızı
kesinleştirmek. Fiziksel Render mount/izinleri, dosya aktarımı ve son JPEG hash'i operasyon sırasında
ayrı onayla doğrulanmalı. Bu tek süreçli gate diğer instance'ların drain durumunu kanıtlamaz.

Üretim dosyalarının başlangıç hash'leriyle aynı kaldığı doğrulandı. Commit/push/deploy/migration yok.
## 227 kaynak eşleşmesi ve 228 paketleme düzenlemesi

227. adımda PostgreSQL koşumunda kaydedilmiş üretim kaynak hash'leriyle mevcut kaynaklar eşleşti.
Dört PostgreSQL test paketindeki uygulama DLL'i mevcut Release DLL'iyle aynı bulundu; test
programlarının PDB kaynak checksum'ları da eşleşti. 292 PASS, 0 FAIL, 0 BLOCKED bu adaya aittir.

Tarihsel 232 SQLite kontrolü tek bir son-binary koşumu değildir. ReleaseSafety 137 ve
ChequeDeletion 50 paketlerinin uygulama PDB'lerinde yalnızca dört Razor bağlantı dosyası farklıydı;
diğer mevcut üretim kaynak checksum'ları eşleşti. AccountingDocuments 45 paketindeki uygulama
DLL'i günceldir; dört gerçek ekranın güvenli bağlantıları kapsanır. PostgreSQL sonuçları bu eski
DLL farkından etkilenmez. Eski Admin Portal sürümünün 115 testi bu sayılara eklenmez.

228. adımda yalnızca .gitignore ve iki rapor düzenlendi. Üretim/test kodu, model ve migration
aynı kaldığından testler tekrarlanmadı. Ham sonuçlar yerelde saklanır; Git gönderim listesine alınmaz.

Git dışı yerel dosyalar:
- /Tests/PostgresReleaseSafety/Artifacts/ (128 derleme/paket dosyası ve 4 ham log)
- /Tests/PostgresReleaseSafety/run-state.json
- /Tests/PostgresReleaseSafety/production-hashes.xml

Toplam 134 çıktı silinmeden dışlanır. Core, Documents, Deletion ve CommitNetwork kaynak projeleri,
Program.cs dosyaları, CommitReplyLossProxy.cs ve bu README gönderime uygundur. Önceden Git'in
izlediği dosyalara git rm/cached veya dosya silme işlemi uygulanmaz. Bu paketleme düzenlemesi
commit/push/deploy yetkisi değildir; canlı şema, ilk eski süreç kapanışı ve disk aktarımı kontrolleri
ayrı onay ve doğrulama gerektirir.