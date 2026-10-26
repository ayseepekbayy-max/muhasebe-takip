# PostgreSQL kalıcı özel medya — geliştirme ve doğrulama raporu

## Çalışma alanı

- Klasör: C:/Users/aysee/Desktop/muhasebe-takip/muhasebe-takip-pg-media-dev
- Dal: feature/postgresql-private-media
- Salt okunur GitHub main doğrulaması: 5a0dd28738412c871c0043aa4e1b13fb84d31a4f.
- Temiz main klonundan başlandı. R2 dalı, çalışan proje ve yedekler değiştirilmedi.
- R2/Cloudflare istemcisi, bucket, anahtar, depolama sağlayıcısı yönlendirmesi veya harici ücretli hizmet kullanılmıyor. R2 uygulama kodu taşınmadı.

## Depolama ve transaction sınırı

PrivateMediaContents tablosu MessageId birincil anahtarını, aynı GUID-N MediaKey'i, bytea Payload'ı ve UTC oluşturma/bitiş zamanlarını tutar. Mesaj tablosuna yeni kalıcı sütun eklenmez. PrivateMessage.MediaAvailable yalnızca NotMapped sunum bilgisidir.

Yeni mesaj ve içeriği aynı PostgreSQL transaction'ında kaydedilir. Son commit tamamlanmadan başarı dönmez. Kapasite/kota/DB hatası mesajı da geri alır. Ayrı geçici dosya veya yükleme kuyruğu yoktur. Medya satırı mesajın çocuğudur; kullanıcının yetkili mesaj silmesi FK cascade ile baytları da siler. Süre temizliği ise mesajı silmez; içeriği kaldırır ve erişim metadata'sını kapatır. Yazı ve yanıt bağları korunur.

Tarayıcı yalnızca mevcut oturum kontrollü /Panel handler'larını kullanır. Sender/recipient, hidden, cleared-history, oturum ve antiforgery kontrolleri korunmuştur. Payload listelere/JSON'a eklenmez; bytea alanı JsonIgnore ile de korunur. Cache-Control no-store ve nosniff kullanılır. Yeni kayıtlarda disk veya herkese açık medya URL'si yoktur.

Eski MediaKey/mesaj satırları migration sırasında korunur; eski dosyalar otomatik okunmaz, kopyalanmaz veya silinmez. Yeni tabloya aktarılmamış eski disk medyası eksik medya olarak gösterilir. Eski içerik varsa, canlı geçiş öncesi ayrı onaylı envanter ve doğrulanmış aktarım gerekir. Bu görevde gerçek içerik kullanılmadı.

## 100 MiB kota

Kesin sınır 104857600 bayttır; 100000000 bayt değildir. Yalnızca mevcut payload satırlarının octet_length toplamını kapsar. Metadata, indeks, TOAST yönetimi, ölü satır sürümleri, WAL ve yedekler bu sayaca dahil değildir.

PrivateMediaQuotas tek satırlık sayacı 0..104857600 aralığında tutar. PostgreSQL INSERT/DELETE tetikleyicisi aynı sayaç satırını kilitleyerek artırır/azaltır. Bekleyen UPDATE koşulu kilit açılınca yeniden değerlendirilir; iki yükleme birlikte sınırı aşamaz. FK cascade ve doğrudan INSERT de aynı korumadan geçer. Payload değiştirilemez; TRUNCATE sayaç bozulmasını önlemek için engellenir. Kota kaydını/tetikleyicileri elle değiştirmek desteklenmez. Uygulama rolüne gereksiz DDL yetkisi verilmemelidir.

Fotoğraf başına 8 MiB, ses başına 16 MiB sınırları korunur; DB de boyut/kind/mesaj-anahtar/zaman eşleşmesini denetler. Kota dolduğunda HTTP 507 döner. Store bu nedenle eski medyayı veya muhasebe verisini silmez. Yazılı mesaj ve mevcut medya erişimi devam eder. Mantıksal kota, DELETE commit edildiğinde geri kazanılır; işletim sistemi disk alanının aynı anda küçüldüğü anlamına gelmez.

## Genel DB kapasitesi: ek koruma ve sınırı

PrivateMediaOptions yalnızca sunucu yapılandırmasıdır. Hiçbir canlı ayar değiştirilmedi:

- PrivateMedia__MaxDatabaseBytes: gerçek plan/ölçümden seçilecek toplam veritabanı boyutu tavanı; varsayılan yoktur.
- PrivateMedia__DatabaseReserveBytes: ek güvenlik payı; varsayılan 134217728 (128 MiB).
- PrivateMedia__BlockNewUploads: disk alarmında yeni medya gönderimini durdurma anahtarı.

MaxDatabaseBytes bilinmiyorsa/uygunsuzsa veya BlockNewUploads=true ise yalnızca yeni medya HTTP 503 ile reddedilir. SQLite üzerinden medya yükleme de kapalıdır. Testlerdeki 10 GiB sentetik tavan, canlı önerisi değildir.

Her gönderim öncesi pg_database_size(current_database()) okunur. Mevcut boyuta ek olarak tüm mantıksal kotanın dört katı (400 MiB) ve yapılandırılan reserve için boşluk ayrılabilecek tavan aranır. Bu muhafazakâr bir uygulama korumasıdır, kesin fiziksel üst sınır değildir: pg_database_size sağlayıcının boş diskini, tüm WAL/backup/başka veritabanı kullanımını ölçmez. WAL, replikasyon/backup gecikmesi, yoğun yeniden yükleme ve muhasebe büyümesi disk tüketebilir. Canlı planın fiziksel disk alarmı ve müdahale süreci ayrıca gereklidir. Uygulama SQL ile genel sunucu boş alanını güvenilir biçimde garanti ettiğini iddia etmez.

PostgreSQL bytea büyük değerleri TOAST üzerinden yönetebilir; EF Core/Npgsql byte[] eşlemesi gerçek PostgreSQL testinde bytea olarak doğrulandı. Bütünlük için tablo permanent/WAL-logged kalır; UNLOGGED/TEMP kullanılmaz. Sadece yeni medya tablosu/TOAST için autovacuum eşikleri 1 ve scale_factor 0.05 olarak ayarlanır. Global ayarlar veya muhasebe tablolarının vacuum ayarları değiştirilmez. VACUUM FULL otomatik çalıştırılmaz; ek alan ve güçlü kilit gerektirebilir.

Kaynaklar: [PostgreSQL disk kullanımı](https://www.postgresql.org/docs/17/diskusage.html), [TOAST](https://www.postgresql.org/docs/16/storage-toast.html), [normal vacuum ve alan geri kazanımı](https://www.postgresql.org/docs/17/routine-vacuuming.html), [WAL](https://www.postgresql.org/docs/17/wal-intro.html).

## Saklama ve otomatik temizlik

- Ses: gönderimden 24 saat sonra yeni erişim reddedilir.
- Tek görüntüleme: yalnızca bir alıcı isteği atomik hakkı kazanır; baytların silinmesi ve kotanın azaltılması aynı transaction'dadır.
- 1/3/5 saat: ilk başarılı açılışta UTC süre başlar; tekrar açma süreyi uzatmaz. Deadline = min(ilk açılış + seçili süre, gönderim + 24 saat).
- Açılmamış fotoğraf: 24 saat; hiçbir fotoğraf aktif timer olsa bile mutlak 24 saat sınırını geçemez.
- Süresi dolmuş erişim, temizlik çalışmamış veya başarısız olsa da handler tarafından engellenir. Fotoğraf görüntüleyicisinin mevcut kalan-süre kapatması korunur. Eksik/süresi dolmuş ses için oynatıcı kurulmaz, durum metni gösterilir.

BackgroundService açılışta hemen ve sonra 30 saniyede bir kalıcı mesaj/deadline kayıtlarından en fazla 100 aday işler. Erişim kapatma, içerik silme ve kota geri kazanımı transaction içindedir. Hata transaction'ı geri alır; kayıt sonraki taramada yeniden aday olur. Bellekte saklanan kaybolabilir iş listesi veya uygulama diskine bağlı kuyruk yoktur. İki işçi aynı içeriği temizlese de kota iki kez düşmez.

Bu aktif veritabanından mantıksal silmedir. PostgreSQL MVCC, WAL ve alınmış yedeklerde geçmiş baytların anında fiziksel imhası garanti edilemez; backup saklama/erişim politikası buna göre belirlenmelidir. Daha önce tarayıcıya teslim edilmiş baytların geri çağrılması da garanti edilemez. Tam PostgreSQL yedeği artık bytea medyayı da içerir ve özel veri olarak korunmalıdır; muhasebe export'u özel sohbeti/medyayı içermez.

## Migration

20261024104204_AddPostgresPrivateMedia:

- Yalnızca iki yeni tablo, indeksler, yeni tablodan PrivateMessages'a FK, kota başlangıç satırı ve bu tabloların fonksiyon/tetikleyicileri.
- PrivateMessages ve muhasebe tablolarının mevcut sütunları değiştirilmez; önceki kayıtlar silinmez.
- Önceki migration dosyaları ve SQLite snapshot'ı değiştirilmez; PostgreSQL snapshot yalnızca yeni medya entity'lerini içerir.
- Eski R2 outbox/tombstone migration'ları BU DALDA YOKTUR ve uygulanmamalıdır.
- Down yeni bytea içeriğini siler; canlıda operasyonel geri dönüş yöntemi değildir.

## İzole PostgreSQL test yöntemi

24 Ekim yayın öncesi güncel sonuçları ve açık engeller için PRE-RELEASE-CHECK.md dosyasına bakın. Aşağıdaki önceki sonuçlar tarihseldir; güncel sentetik test yardımcısı PostgreSQL 18 kullanır.

Tüm C# medya, sohbet, yedekleme ve muhasebe regresyonları kendi geçici postgres:17 Docker veritabanında sentetik kayıtlarla çalışır. Harici bağlantı parametresi kabul edilmez; loopback portu ve rastgele geçici parola kullanılır, bağlantı bilgisi yazdırılmaz/dosyaya kaydedilmez. Test sonunda yalnızca kendi konteyneri kaldırılır. Test uygulaması restart'ları sadece TestServer içindir; gerçek servis yeniden başlatılmaz.

Eski main migration zinciri önceki tabloları varsaydığından, test ön şeması 20261020103737_AddCashBankAccounts TargetModel'inden kurulur. 30 bilinen ve üç bilinmeyen sentetik eski history kaydı fixture olarak işaretlenir. Önce muhasebe/sohbet/yanıt/legacy-media metadata kayıtları eklenir, sonra yalnızca yeni migration uygulanır. Eski zincirin boş DB'ye baştan uygulanabildiği iddia edilmez. Önceki/bilinmeyen geçmiş ve sentetik veri korunması kontrol edilir.

Son test sonuçları:

| Süit | Başarılı kontrol |
|---|---:|
| Yeni PostgreSQL medya, gerçek 100 MiB kota, TTL, HTTP, migration, restart | 144 |
| Mevcut sohbet/fotoğraf, yeni mutlak 24 saat beklentisiyle PostgreSQL | 62 |
| Muhasebe yedekleme, 25 tablo; PostgreSQL | 118 |
| Muhasebe tutarlılığı; PostgreSQL | 48 |
| Toplam | 372 |

Son süitlerde başarısız kontrol: 0. Ayrıca mevcut görüntüleyici ve eksik/süresi dolmuş medya JavaScript kontrolleri başarılı. Release build başarılı. İlk PostgreSQL uyarlamasında bazı sentetik fixture'ların DateTime.Now varsayılanları reddedildi; yalnızca fixture tarihleri UTC yapıldı. Npgsql UTC/güvenlik kontrolleri gevşetilmedi, muhasebe üretim kodu değiştirilmedi.

Mevcut NU1903 SQLitePCLRaw.lib.e_sqlite3 2.1.11 ve Program.cs CS8629 uyarıları devam ediyor; ilgisiz bağımlılık yükseltmesi yapılmadı.

Komutlar (bu geliştirme klasöründen; Docker ve yerel postgres:17 imajı gerekir):

    dotnet build Tests/PostgresMedia/PostgresMedia.csproj -c Release
    dotnet Tests/PostgresMedia/bin/Release/net10.0/PostgresMedia.dll
    dotnet build Tests/PrivatePhotos/PrivatePhotos.csproj -c Release
    dotnet Tests/PrivatePhotos/bin/Release/net10.0/PrivatePhotos.dll
    dotnet build Tests/AccountingBackup/AccountingBackup.csproj -c Release
    dotnet Tests/AccountingBackup/bin/Release/net10.0/AccountingBackup.dll
    dotnet build Tests/AccountingConsistency/AccountingConsistency.csproj -c Release
    dotnet Tests/AccountingConsistency/bin/Release/net10.0/AccountingConsistency.dll
    node Tests/PrivatePhotos/photo-viewer.cjs

## Canlıya geçmeden önce gerekenler

1. Bu yeni dalı/diff'i onayla. R2 dalıyla birleştirme; R2 migration'larını bu özellik için kullanma.
2. Canlıya bağlanmadan burada giderilemeyen kapasite bilgisini sağlayıcının planı ve gerçek disk/WAL/backup ölçümleriyle belirle; uygun MaxDatabaseBytes/reserve ve disk alarmını seç. Bilinmeyen tavanla yeni medya gönderimi bilerek kapalıdır.
3. Ayrı onayla güncel tam DB yedeği ve salt okunur history/şema kontrolü yap. Mevcut AddCashBankAccounts öncülünü doğrula; bilinmeyen eski history kayıtlarını koru. Database__AutoMigrate=false kalsın.
4. Eski yerel medya varsa ayrı onaylı envanter/yedek/aktarım planı hazırla. Bu görev eski dosyaları yeni tabloya taşımadı; anahtarları veya var olmayan baytları yeniden üretemez.
5. Yeni migration'ın tam SQL'ini, tetikleyiciler/fonksiyonlar dahil, inceleyip ayrı onayla uygula. Tabloları tek başına elle oluşturarak kota korumasını atlama. Runtime ve migration rolü izinlerini kontrol et.
6. Ayrı commit/push/deploy onayı sonrası yalnızca bu dalın değişikliklerini yayınla; bytea saklama, sayaç-sum eşitliği, 24 saat temizliği ve DB büyümesini izle. Geri dönüşte eski dosya tabanlı kodun yeni bytea medyayı okuyamayacağını dikkate al; Down çalıştırma.

100 MiB mantıksal sınır veri kaybını veya paylaşılan veritabanında sıfır I/O etkisini garanti etmez. Muhasebe şeması/kodu korunmuş ve testleri geçmiş olsa da ortak DB I/O'su, WAL ve yedek hacmi artabilir. Tam yedekleri ve log erişimini koru; SensitiveDataLogging/IncludeErrorDetail açma.

Canlı Render/DB, gerçek medya, Cloudflare, ücretli hizmet, commit/push/deploy veya gerçek servis restart'ı kullanılmadı. Migration uygulaması yalnızca izole test PostgreSQL örneklerindedir.

## Tam dosya listesi

Değiştirilen:

- `Data/AppDbContext.cs`
- `Migrations/AppDbContextModelSnapshot.cs`
- `Models/PrivateMessage.cs`
- `Pages/Panel.cshtml`
- `Pages/Panel.cshtml.cs`
- `Program.cs`
- `Services/PrivateMediaCleanupService.cs`
- `Services/PrivateMediaStore.cs`
- `Tests/AccountingBackup/AccountingBackup.csproj`
- `Tests/AccountingBackup/Program.cs`
- `Tests/AccountingConsistency/AccountingConsistency.csproj`
- `Tests/AccountingConsistency/Program.cs`
- `Tests/PrivatePhotos/PrivatePhotos.csproj`
- `Tests/PrivatePhotos/Program.cs`
- `Tests/PrivatePhotos/photo-viewer.cjs`

Eklenen:

- `Docs/POSTGRES-PRIVATE-MEDIA.md`
- `Migrations/20261024104204_AddPostgresPrivateMedia.Designer.cs`
- `Migrations/20261024104204_AddPostgresPrivateMedia.cs`
- `Models/PostgresPrivateMedia.cs`
- `Tests/PostgresMedia/PostgresMedia.csproj`
- `Tests/PostgresMedia/Program.cs`
- `Tests/PostgresMedia/TemporaryPostgres.cs`
