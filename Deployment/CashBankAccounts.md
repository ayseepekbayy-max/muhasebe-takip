# Nakit ve banka hesapları

## Yayına alma sırası (PostgreSQL / Render)

1. Mevcut uygulamadan güncel ZIP yedeği indirin.
2. Render uygulamasını henüz yeni koda deploy etmeyin. PostgreSQL bağlantınızda `20261020_KasaBanka.sql` dosyasının tamamını **script olarak** çalıştırın (DBeaver: SQL Editor → dosyayı aç → Execute SQL Script). Son sorguda üç alan da `true` olmalıdır. Hata olursa `ROLLBACK;` çalıştırın ve deploy etmeyin; hatayı inceleyin.
3. SQL başarılı olduktan sonra bu değişikliği `main` dalına alın; Render → Manual Deploy → Deploy latest commit. Otomatik migration ayarını değiştirmek gerekmez.
4. Ana sayfa → Kasa/Banka Hesapları'ndan en az bir nakit kasa ve/veya banka hesabı oluşturun. Mevcut para tutarını açılış bakiyesine yazın. Daha sonra aynı parayı tekrar tahsilat olarak kaydetmeyin.
5. Bir tahsilat/ödeme ekleyip seçilen hesabın bakiyesini kontrol edin. İki hesap arasında transfer deneyin: kaynak azalır, hedef artar, genel toplam ve gelir/gider değişmez. Yeni ZIP yedeği indirin.

SQL sadece iki yeni tablo, nullable hesap alanı, indeks ve ilişki kısıtları ekler. Mevcut satırlar, faturalar, cari bakiyeleri, stok ve personel verileri silinmez veya yeniden yazılmaz. Eski hesap atanmamış kasa hareketleri genel toplamda kalır ve hareket düzenleme ekranından bir hesaba atanabilir. SQL tek transaction içinde uygulanır; lock timeout 10 saniye, statement timeout 120 saniyedir. Migration geçmişi sayesinde başarılı çalıştırma tekrarlandığında yeni ekleme yapılmaz. SQL dosyası mevcut uygulama veritabanı içindir; boş veritabanı kurulumu bütün PostgreSQL migration zincirini gerektirir.

Veritabanı hazırlığı eski uygulama ile uyumludur. Yeni sürüm geri alınırsa tabloları ve yeni alanı yerinde bırakın; **Down migration çalıştırmayın**, yeni hesap verilerini kaybedersiniz.

## Davranış

- Hesaplar TL cinsindedir; döviz/kurlu hesap bu adımda bulunmaz.
- Açılış bakiyesi + tahsilatlar − ödemeler + gelen transfer − giden transfer = hesap bakiyesi.
- Transfer tek kayıt olarak tutulur; iki bakiyeye birlikte yansır. Silinirse iki taraf birlikte geri alınır. Cari/fatura ödemesi veya gelir/gider oluşturmaz.
- Hesap silmek yerine pasife alınır; geçmiş ve bakiyesi korunur. Pasif hesap yeni hareket veya transfer kabul etmez.
- Kasa hareketi, fatura tahsilat/ödeme ve müşteri iş geliri için aktif şirket hesabı seçilmesi gerekir. Şirketler arası hesap bağlantıları veritabanı kısıtlarıyla da engellenir.
- Yedek sürüm 4 hesapları ve transferleri kapsar. Eski 2/3 sürümlerindeki kasa hareketleri hesap atanmadan korunur.
- Stok/fatura bağlantısı ve ödeme planlarının kasa hareketine dönüştürülmesi bu değişikliğe dahil değildir.

## Doğrulama

`dotnet build`

`dotnet run --project Tests/AccountingConsistency/AccountingConsistency.csproj`

`dotnet run --project Tests/AccountingBackup/AccountingBackup.csproj`

Testler izole SQLite veritabanları kullanır; canlı PostgreSQL'e bağlanmaz. PostgreSQL migration snapshot eşitliği de kontrol edilir. Bu dağıtım PostgreSQL içindir; eskiden geride kalmış SQLite migration zinciri kapsam dışıdır ve değiştirilmemiştir.
