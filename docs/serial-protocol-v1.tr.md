# AI Light seri protokol v1

Firmware 0.3.0; ESP32-C3 native USB CDC (115200 baud, 8N1) ve Bluetooth LE GATT aynı v1 komutlarını kullanır. Güncel masaüstü FX1 yerel efekt desteğini zorunlu tutar; tüm ürünler güncel ortak firmware ile satılacaktır. Eski firmware için host animasyon yolu yoktur. ASCII, büyük harfli komut, LF veya CRLF satır sonu. En fazla 63 karakter (satır sonu hariç). Parametre aralarında tek boşluk. Boş satır yok sayılır; tab, NUL, ASCII dışı karakter, fazla parametre ve taşan satır bütün satırı geçersiz kılar. Sonraki LF sonrası alıcı toparlanır. CR tek başına komutu çalıştırmaz.

| İstek | Yanıt | Etki |
|---|---|---|
| `INFO` | `AI_LIGHT PROTOCOL=1 FW=0.3.0 ID=<12-hex> CAPS=RGB8,WATCHDOG,BLE,USB_FIRST,FX1 WATCHDOG_MS=6000 READY=1` | Ürün/protokol/sürüm/efuse kimliği ve PWM sağlığı; ışık/canlılık değiştirmez |
| `SET_RGB 255 0 0` | `OK` | Kırmızı; her kanal 0–255, watchdog başlar/yenilenir |
| `SET_RGB 0 255 0` | `OK` | Yeşil |
| `SET_RGB 0 0 255` | `OK` | Mavi; test veya gelecekteki özelleştirme için |
| `SET_RGB 20 40 0` | `OK` | Düşük parlaklıkta karışım; PC kalibrasyon yapabilir |
| `OFF` | `OK` | Söndürür ve watchdog'u pasifleştirir |
| `PING` | `PONG` | Mevcut aktif rengin watchdog'unu yeniler; sönmüş ışığı yeniden yakmaz |
| `SET_EFFECT BREATHE 0 0 178` | `OK` | FX1: dört saniyelik yerel mavi nefes; verilen RGB efektin tepe rengidir |
| `SET_EFFECT PULSE 0 178 0` | `OK` | FX1: bir kez 900 ms tam → sönük → tam geçiş, ardından sabit renk |
| `SET_EFFECT SOLID 178 0 0` | `OK` | FX1: animasyonu hemen kesip sabit RGB uygular |

Yanıtlar LF ile biter. Hatalı komut `ERR COMMAND`: renk ve watchdog değişmez. PWM kurulum/yazma hatası `ERR PWM`: cihaz kapalı kalır; INFO READY=0 döner. Bu hata için kartı yeniden başlat ve bağlantıyı kontrol et.

Firmware başlangıçta sönüktür. SET_RGB/SET_EFFECT/PING sonrası 6000 ms boyunca geçerli canlılık komutu gelmezse söner. INFO, boş satır veya bozuk veri bu süreyi uzatmaz. Watchdog sonrası PING yalnızca PONG döner; yeniden renk için SET_RGB veya SET_EFFECT gerekir. FX1 kullanan uygulama değişmeyen durumda 2000 ms aralıkla PING gönderir. Bu heartbeat PC-cihaz bağlantısını gösterir, AI oturum canlılığını göstermez.

## Yerel efektler — FX1, firmware 0.3.0

- Masaüstü (Windows 0.1.42 / macOS 0.1.3) doğrulanmış INFO içinde tam `FX1` yeteneğini zorunlu tutar. Protokol sürümü 1 kalır; SET_RGB manuel testler ve kısa bağlantı animasyonu için kullanılabilir.
- BLUE → BREATHE, RED/YELLOW → SOLID, GREEN → sabit yeşil; önceki durum BLUE/YELLOW/RED ise bir kez PULSE. OFF'tan yeşile geçiş pulse yapmaz. Efektler harness adı bilmez; yalnız RGB ve efekt türü alır.
- Kart kendi millis saatinden 10 ms aralıkla kare üretir. 33 noktalı sabit noktalı eğriler eski nefesin geniş tepe/gamma davranışını ve yeşil kosinüs geçişini yaklaşıklar; kayan noktalı hesap gerektirmez. CPU 80 MHz derlemesi de doğrulanır.
- Aynı efektin yeniden gönderilmesi, PING veya aynı efektte parlaklık/RGB değişimi fazı sıfırlamaz ve tamamlanmış PULSE'ı yeniden başlatmaz. Farklı efekt, OFF/SET_RGB ile kesme veya watchdog sonrasındaki yeni etkinleştirme yeni faz başlatabilir. PULSE bittikten sonra efekt türü PULSE olarak kalır, renk sabitlenir.
- Durum/parlaklık değiştiğinde yeni tam komut anında gider; normalde iki saniyede bir yalnız PING/PONG vardır. Uzun (en az 4 saniye) gönderim boşluğundan sonra veya yeniden bağlantıda host tam durumu tekrar uygular; tek PING ile sönmüş ışığın geri gelmesi beklenmez.
- Manuel renk testi SET_RGB ile çalışır ve yerel animasyonu keser. Windows'un iki saniyelik bağlantı gökkuşağı açılış animasyonu bu değişiklik kapsamında host üzerinde kalır; sürekli nefes/yeşil pulse akışına dönüşmez.
- Kullanıcı boşta ışık zaman aşımını etkinleştirmişse süreyi uygulama izler ve OFF gönderir. Bağlantı kaybında firmware watchdog'u altı saniyede animasyonu da kapatır.
- FX1 bulunmazsa cihaz bağlantısı kapatılır, ışık komutu gönderilmez ve firmware güncelleme mesajı gösterilir. Eski firmware için RGB animasyon fallback'i yoktur.

Host bir seferde bir istek göndersin, yanıtı okusun; önerilen yanıt zaman aşımı 1 saniye. Yanıt alınmazsa INFO ile yeniden tanıma ve güncel RGB/OFF ile eşitleme yapabilir. Komutlar tekrar uygulanabilir. Çıkışta OFF gönderilir. DTR host tarafında etkin tutulabilir; RTS/otomatik reset davranışı gerçek kartta test edilmelidir. Portu her hook için yeniden açma.

Cihaz alımı/yazımı sınırlı süre ve tamponlarla yürütülür. Host yanıt okumuyorsa cevap düşebilir; sınırsız cevap kuyruğu yoktur. Sürekli veri akışı watchdog kontrolünü engellemez. Yanıt kaybını bağlantı sağlığına dahil etmek host'un sorumluluğudur.

Otomatik keşifte USB kimliği yalnızca aday daraltır; ürün kimliği doğrulanmadan SET_RGB gönderilmez. INFO kimliği kriptografik doğrulama değildir. Birden fazla doğrulanmış üründe kullanıcı seçim yapmalıdır.

v1 firmware AI/harness adı bilmez. Kırmızı/yeşil/sönük önceliği PC'dedir. Gelecekteki harness desteği bu protokolü değiştirmemelidir. Ekran gibi yeni yetenekler ayrı yetenek/protokol tasarımı gerektirir.

## USB önceliği ve BLE taşıması — firmware 0.2.0

- **Bilgisayarın USB veri bağlantısı her zaman önceliklidir.** Native USB Serial/JTAG SOF sinyali `HWCDC::isPlugged()` ile izlenir; yalnız 5 V bulunması USB veri bağlantısı sayılmaz. 100 ms bağlanma / 750 ms kopma filtresi kısa SOF kesintilerinde BLE'yi tekrar tekrar başlatmayı önler. USB host bağlıyken seri port açılmasa bile BLE kapalı kalır. Uyuyan/suspend olan bir USB host SOF üretmeyebilir; bu durum fiziksel test kapsamındadır.
- USB host varken BLE sunucusu ve radyo `BLEDevice::deinit(false)` ile kapatılır. USB yokken BLE tekrar başlatılır; yeniden başlatmayı engelleyen kalıcı Bluetooth bellek serbest bırakma çağrısı kullanılmaz. Açılışta USB algılanabilsin diye BLE en erken 1 saniye sonra başlar.
- Powerbank/USB adaptörü yalnız güç verir; bu durumda BLE aktiftir. Gücün tamamen kesilip yeniden gelmesi normal yeniden başlatmadır; önceki RGB bellekte tutulmaz.
- Kanal/bağlantı nesli değişince LED kapatılır, iki ayrıştırıcının kısmi satırları ve watchdog durumu temizlenir. Eski BLE bağlantısının kuyruktaki paketleri atılır. Host INFO doğrulamasından sonra tam güncel RGB/OFF gönderir; geçmiş renkler oynatılmaz.
- BLE hizmet UUID: `8b7e0001-5c8a-4c8e-9f24-367b495d7a11`.
- RX UUID: `8b7e0002-5c8a-4c8e-9f24-367b495d7a11`, **write with response**. Her yazı en fazla 20 bayta bölünür; satır sonu son parçada gönderilir.
- TX UUID: `8b7e0003-5c8a-4c8e-9f24-367b495d7a11`, **notify**. İstemci INFO göndermeden önce bildirim aboneliği kurar. Yanıtlar 20 baytlık bildirimlere bölünebilir; host LF'ye kadar birleştirir. GATT yazma onayı komutun `OK` yanıtı yerine geçmez.
- Reklam adı `VibeCodingLight`; keşif isimle değil hizmet UUID'siyle yapılır. Bağlantı sonrası INFO ürün/protokol/READY/ID doğrulaması zorunludur. INFO kimliği kriptografik cihaz doğrulaması değildir; bu LED kontrol protokolü kullanıcı eşleştirme/PIN veya şifreli eşleştirme garantisi vermez.
- Masaüstü USB'yi önce tarar, USB yoksa BLE'ye geçer; BLE açıkken yaklaşık 3 saniyede bir aynı robotun USB'de görünmesini kontrol eder. Bağlantı kimliği bir çalışma süresince sabitlenir; farklı robotun USB'si seçili BLE robotunu devralamaz. Otomatik modda birden fazla aday varsa seçim istenir.
- Komut onayları USB'de 700 ms, BLE'de 1500 ms ile sınırlıdır; BLE kurulum denemesi 3500 ms. İşletim sisteminin alttaki radyo işlemleri daha uzun sürebilir. Yerel zaman aşımı sonrası bağlantı bırakılıp yeniden denenir.
- Windows: yerel WinRT GATT. macOS: paket içindeki Swift CoreBluetooth yardımcısı; sistem Bluetooth izni gerekir. USB doğrulanmışsa masaüstü BLE taraması durur; Mac yardımcısı yalnız USB bulunamadığında başlatılır, USB kullanımında Bluetooth izni gereksiz yere istenmez. İki platform ortak `RobotConnection` ve komut/yanıt doğrulamasını kullanır. Python, Node veya harici BLE servisi son kullanıcı bağımlılığı değildir.

Derleme ve otomatik kontroller fiziksel USB/BLE testinin yerine geçmez. Test adımları: [Hardware validation](HARDWARE-VALIDATION.md).
