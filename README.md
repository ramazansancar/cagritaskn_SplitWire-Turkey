<p align="center">
  <img width="auto" height="128" src="https://github.com/cagritaskn/SplitWire-Turkey/blob/main/src/SplitWireTurkey/Resources/splitwire-logo-128.png">
</p>

# <p align="center"><strong></b>SplitWire-Turkey</strong></p>

[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)

## Özet

SplitWire-Turkey, birden çok metot ile DPI engelini bertaraf etmek için geliştirilmiştir. WireGuard VPN yapılandırmasını kolaylaştıran, ByeDPI ve Proxifyre ile otomatik kurulum yapan açık kaynak bir Windows uygulamasıdır. Bu araç, kullanıcıların ücretsiz Cloudflare konfigürasyon dosyası oluşturmalarını ([wgcf](https://github.com/ViRb3/wgcf) aracılığı ile) ve [WireSock](https://www.wiresock.net/) sayesinde bu konfigürasyonu **yalnızca** tercih edilen uygulamalar özelinde kullanmalarını, ByeDPI ve ProxiFyre/drover kullanarak ayrık tünelleme yapılabilmelerini sağlar. Ayrıca hizmet kurulumu yaptığı için Windows'u her yeniden başlattığınızda tekrar kurulum yapmanıza gerek kalmaz.

**Özellikler:**
-  **Hızlı Kurulum:** Tek tıkla kullanım
-  **Özelleştirilmiş Kurulum:** Gelişmiş ayarlarla özel yapılandırma
-  **Servis Yönetimi:** Gerekli hizmetleri kurup/kaldırma
-  **Klasör Yönetimi:** Tercih edilen uygulama klasörlerini kolayca ekleme/çıkarma
-  **Ücretsiz:** Programı kullanmak tamamen ücretsiz

## SplitWire-Turkey

SplitWire-Turkey, Türkiye'deki internet kullanıcıları için özel olarak tasarlanmış bir DPI aşımı otomasyonu projesidir. Bu uygulama, WireGuard VPN teknolojisini kullanarak güvenli ve hızlı internet bağlantısı sağlamak, ByeDPI'u kolay yoldan kullanarak ayrık tünelleme yapmak isteyen kullanıcılar için geliştirilmiştir. Hizmet kurulumu yaptığı için bilgisayarınızı yeniden başlattığınızda ilgili uygulamalara erişmek için fazladan bir işlem yapmanıza gerek kalmaz. Tamamen açık kaynak kodlu olan bu uygulamanın kaynak kodları repository'de bulunan /src klasörünün içinde mevcuttur.

> [!NOTE]
> Windows 7, 8, 8.1, 10 veya 11 işletim sistemlerinde **yönetici olarak çalıştırmanız** mecburidir. (Otomatik yönetici izni talep edilir ancak bazı durumlarda bu izin alınamazsa manuel olarak yetki yükseltmesi yapmak gerekebilir)

## SplitWire-Turkey Kullanımı

> [!IMPORTANT]
> GoodbyeDPI veya Cloudflare WARP ya da farklı bir VPN uygulaması kullanıyorsanız bu uygulamayı kaldırmalısınız. SplitWire-Turkey, kurulumlardan herhangi birini başlatmanız halinde GoodbyeDPI hizmetini otomatik olarak kaldırır ancak bu bir sorundan dolayı gerçekleşemez ise; [GoodbyeDPI-Turkey Doğru Şekilde Kaldırma Rehberi](https://github.com/cagritaskn/GoodbyeDPI-Turkey/blob/master/REVERT.md)'ni takip ederek kaldırma işlemini gerçekleştirebilirsiniz.

### Standart Kurulum (Tavsiye Edilen)
- **[SplitWire-Turkey-Setup-Windows.exe](https://github.com/cagritaskn/SplitWire-Turkey/releases/download/1.0.0/SplitWire-Turkey-Setup-Windows.exe)** kurulum paketini indirip SplitWire-Turkey kurulumunu gerçekleştirin.
- SplitWire-Turkey uygulamasını açın. (SmartScreen "Windows kişisel bilgisayarınızı korudu" uyarısı alırsanız "Ek bilgi" yazısına tıkladıktan sonra "Yine de çalıştır" butonuna tıklayın, virüs taraması ve bu uyarı hakkında bilgi aşağıda verilmiştir)
- Standart Kurulum butonuna tıklayın. (Eğer WireSock yüklü değilse sizin için indirip kurulumu başlatacaktır)
- "WireSock hizmeti kuruldu ve başlatıldı" uyarısını aldığınızda program çalışmaya başlamış demektir.
- Sisteminizi yeniden başlatıp seçili uygulamalara erişip erişemediğinizi test edin.

> [!NOTE]
> Standart kurulum yalnızca Discord için WireSock koruması sağlar.

### Alternatif Kurulum (Standart Kurulum İşe Yaramazsa)
- **[SplitWire-Turkey-Setup-Windows.exe](https://github.com/cagritaskn/SplitWire-Turkey/releases/download/1.0.0/SplitWire-Turkey-Setup-Windows.exe)** kurulum paketini indirip SplitWire-Turkey kurulumunu gerçekleştirin.
- SplitWire-Turkey uygulamasını açın. (SmartScreen "Windows kişisel bilgisayarınızı korudu" uyarısı alırsanız "Ek bilgi" yazısına tıkladıktan sonra "Yine de çalıştır" butonuna tıklayın, virüs taraması ve bu uyarı hakkında bilgi aşağıda verilmiştir)
- Alternatif Kurulum butonuna tıklayın. (Eğer WireSock yüklü değilse sizin için indirip kurulumu başlatacaktır)
- "WireSock hizmeti kuruldu ve başlatıldı" uyarısını aldığınızda program çalışmaya başlamış demektir.
- Sisteminizi yeniden başlatıp seçili uygulamalara erişip erişemediğinizi test edin.

> [!NOTE]
> Alternatif kurulum yalnızca Discord için WireSock koruması sağlar.
**[Bal Porsuğu](https://www.technopat.net/sosyal/uye/bal-porsugu.101438/)**'na alternatif sürüm için WireSock kurulum dosyasını bulduğu ve yaptığı testler için teşekkürler.

### ByeDPI ST Kurulum (Split Tunneling)
- **[SplitWire-Turkey-Setup-Windows.exe](https://github.com/cagritaskn/SplitWire-Turkey/releases/download/1.0.0/SplitWire-Turkey-Setup-Windows.exe)** kurulum paketini indirip SplitWire-Turkey kurulumunu gerçekleştirin.
- SplitWire-Turkey uygulamasını açın. (SmartScreen "Windows kişisel bilgisayarınızı korudu" uyarısı alırsanız "Ek bilgi" yazısına tıkladıktan sonra "Yine de çalıştır" butonuna tıklayın, virüs taraması ve bu uyarı hakkında bilgi aşağıda verilmiştir)
- ByeDPI ST Kurulum butonuna tıklayın. (WPF ve C++ Redistributable Paketi kurulumları otomatik olarak gerçekleşecektir)
- "Kurulum başarıyla tamamlandı." uyarısını aldığınızda program çalışmaya başlamış demektir.
- Sisteminizi yeniden başlatıp seçili uygulamalara erişip erişemediğinizi test edin.

> [!NOTE]
> Bu yöntem yalnızca Discord için DPI aşımı yapar. Discord'un ilk açılışı normalden biraz daha uzun sürebilir.

### ByeDPI DLL Kurulum (ByeDPI ve drover Sayesinde)
- **[SplitWire-Turkey-Setup-Windows.exe](https://github.com/cagritaskn/SplitWire-Turkey/releases/download/1.0.0/SplitWire-Turkey-Setup-Windows.exe)** kurulum paketini indirip SplitWire-Turkey kurulumunu gerçekleştirin.
- SplitWire-Turkey uygulamasını açın. (SmartScreen "Windows kişisel bilgisayarınızı korudu" uyarısı alırsanız "Ek bilgi" yazısına tıkladıktan sonra "Yine de çalıştır" butonuna tıklayın, virüs taraması ve bu uyarı hakkında bilgi aşağıda verilmiştir)
- ByeDPI DLL Kurulum butonuna tıklayın. (ByeDPI ve drover kurulumu otomatik olarak gerçekleşecektir)
- "Kurulum başarıyla tamamlandı." uyarısını aldığınızda program çalışmaya başlamış demektir.
- Sisteminizi yeniden başlatıp seçili uygulamalara erişip erişemediğinizi test edin.

> [!NOTE]
> Bu yöntem yalnızca Discord için DPI aşımı yapar. Eğer droveri kaldırmak isterseniz Gelişmiş sekmesindeki Hizmetleri Kaldır butonuna tıklayabilir ya da farklı bir kurulum başlatabilirsiniz.

### Özelleştirilmiş Kurulum (Tercih Edilen Klasörler İçin)
NOT: Bu yöntem WireSock ve wgcf kullanarak çalışır. (ByeDPI değil)
- **[SplitWire-Turkey-Setup-Windows.exe](https://github.com/cagritaskn/SplitWire-Turkey/releases/download/1.0.0/SplitWire-Turkey-Setup-Windows.exe)** kurulum paketini indirip SplitWire-Turkey kurulumunu gerçekleştirin.
- SplitWire-Turkey uygulamasını açın. (SmartScreen "Windows kişisel bilgisayarınızı korudu" uyarısı alırsanız "Ek bilgi" yazısına tıkladıktan sonra "Yine de çalıştır" butonuna tıklayın, virüs taraması ve bu uyarı hakkında bilgi aşağıda verilmiştir)
- Gelişmiş sekmesine gidin.
- "Klasör Ekle" butonu ile tercih ettiğiniz uygulamanın bulunduğu klasörü seçin.
- "Özelleştirilmiş Kurulum" butonuna tıklayın.
- "WireSock hizmeti kuruldu ve başlatıldı" uyarısını aldığınızda program çalışmaya başlamış demektir.

> [!NOTE]
> Yalnızca seçtiğiniz klasörler özelinde düzenlenmiş "wgcf-profile.conf" dosyası oluşturmak istiyorsanız klasör listesini hazırladıktan sonra "Özelleştirilmiş Profil Dosyası Oluştur" butonuna tıklayabilirsiniz.

> [!NOTE]
> SplitWire-Turkey uygulamasını daha önce kullandıysanız, yeni bir kurulum yapmadan önce "Gelişmiş" sekmesinden "Hizmetleri Kaldır" butonuna tıklamanız daha sağlıklı bir kurulum gerçekleştirmenizi sağlar.

> [!NOTE]
> SplitWire-Turkey uygulamasını set-up dosyasını kullanmadan ve sisteminize yerleşik bir şekilde kurmadan, **[SplitWire-Turkey-ZIP-Windows.zip](https://github.com/cagritaskn/SplitWire-Turkey/releases/download/1.0.0/SplitWire-Turkey-ZIP-Windows.zip)** isimli ZIP dosyasını indirip bir klasöre ayıkladıktan sonra **SplitWire-Turkey.exe**'yi çalıştırarak da kullanabilirsiniz. .NET Desktop Runtime hatası alırsanız /Prerequisites klasöründeki kurulum dosyaları ile .NET kurulumu yapabilirsiniz.

## Karşılaşılabilecek Sorunlar ve Hata Bildirimi
- "Register failed" hatası: Bazı internet sağlayıcıları ya da CloudFlare'in kendisi, ücretsiz API'sinin kullanımını çeşitli sebeplerle engelleyebiliyor. Bunun en sık görülen sebebi "abusive usage" olarak tanımlanan bölgesel aşırı kullanma istismarıdır. Bu sebeple wgcf, kayıt gerçekleştiremez ve konfigürasyon dosyası oluşturamaz ve bunun sonucunda "Register işlemi başarısız oldu. Return code: 1" hatası alınır. Böyle bir durumda maalesef Standart Kurulum, Alternatif Kurulum ve Özelleştirilmiş Kurulum yöntemleri işlevini yerine getiremez. Geçici olarak bir VPN ya da proxy kullanılarak bu yasak aşılabilse dahi; Cloudflare API'sinden geçici olarak tünellenmiş şekilde oluşturulan private-key ve konfigürasyon dosyası, yalnızca tünellenilmiş haldeki makine için geçerli olacağından kullanıma yine engel olacaktır. Bu hatayı alıyorsanız ByeDPI ST veya ByeDPI DLL yöntemlerini kullanmayı deneyebilirsiniz.
- Hizmet kurulumları sırasında hata: Hizmetler penceresi açıkken bu uygulamayı kullanmayın.
- "Checking for updates" ekranında kalma: Eğer ByeDPI ST Kurulum butonu ile kurulum yaptıysanız ve "Checking for updates" ekranında kaldıysanız, SplitWire-Turkey'in kurulu olduğu klasörün içindeki (Genellikle Program Files/SplitWire-Turkey klasörüne kurulur) **/logs klasörünün içeriği ile birlikte** **[Issues](https://github.com/cagritaskn/SplitWire-Turkey/issues)** kısmından hata bildiriminde bulunun.


### WireSock ve SplitWire-Turkey'i Sistemden Kaldırmak
- SplitWire-Turkey uygulamasını çalıştırın.
- "Gelişmiş" sekmesindeki "Hizmetleri Kaldır" butonuna tıklayın. 
- Daha sonra Windows'un Program Ekle/Kaldır bölümünden SplitWire-Turkey ve metodların çalışması için gereken ön yüklemelerden tercih ettiklerinizi kaldırın. (Ya da SplitWire-Turkey'in kurulum klasöründe bulunan "unins000.exe" isimli yürütülebilir ile kaldırın)

## Virüs & SmartScreen Uyarısı
Program açık kaynak kodlu olduğundan tüm kodu görüp inceleyebilirsiniz. Tüm program açık kaynak kodludur ve kaynak kodu /src klasörü içerisinden incelenebilir, tercih edilirse tekrar derlenebilir. Programı kullanmak istemeyen ve güvenmeyen kullanıcılar, programı kullanmak zorunda değildir, programı kullanmak kullanıcının inisiyatifindedir.
Dilerseniz tüm klasörü, kurulum dosyasını, .zip dosyasını ya da kaynak kodlarını [VirusTotal](https://www.virustotal.com/gui/home/upload) gibi bir sitede taratıp sonuçları inceleyebilir, dilerseniz C# dili biliyorsanız veya bilen bir tanıdığınız varsa başvurup kodun ne yapmaya çalıştığını anlayabilirsiniz. Programı imzalamadan yayınlamak bu gibi sorunlara yol açabiliyor. Programı imzalamak döviz kuruyla düzenli ödeme yapmayı gerektirdiği ve bu program ücretsiz olduğu, bununla birlikte gelir elde etmeden bakımı yapıldığı için imzalama girişiminde bulunamıyorum.
> [!NOTE]
> **[SplitWire-Turkey-Setup-Windows.exe VirusTotal sonuçlarında](https://www.virustotal.com/gui/file-analysis/NmY1ZDg4MDlmZWE5YmJkMjkwZTZhNjY3M2ViN2IwM2U6MTc1NDQ5ODkxOQ==)** Dosyalarda küçük bir kullanıcı kesimi tarafından kullanılan antivirüs yazılımları tarafından hatalı algılanmış (false positive) virüs ya da zararlı yazılım bildirimleri algılanabilir ancak bunlar az kullanılan ve tespit yöntemleri güvenilir olmayan yazılımlardır. Algılanma sebebi, SplitWire-Turkey'in wgcf.exe ve WireSock Set-up dosyalarını internet üzerinden indirip çalıştırması ve sistem üzerinde birçok değişiklik yapmasıdır. (DNS değişikliği hizmet ve program paketi kurma, kaldırma gibi)

> [!NOTE]
> **[SplitWire-Turkey-ZIP-Windows.zip VirusTotal sonuçlarında](https://www.virustotal.com/gui/file-analysis/ZGM4ZTUxMGRkZTgwNmQwZjFhMDI2NjUxYjUzOGEzNmI6MTc1NDQ5OTE3Ng==)** Dosyalarda küçük bir kullanıcı kesimi tarafından kullanılan antivirüs yazılımları tarafından hatalı algılanmış (false positive) virüs ya da zararlı yazılım bildirimleri algılanabilir ancak bunlar az kullanılan ve tespit yöntemleri güvenilir olmayan yazılımlardır. Algılanma sebebi, SplitWire-Turkey'in wgcf.exe ve WireSock Set-up dosyalarını internet üzerinden indirip çalıştırması ve sistem üzerinde birçok değişiklik yapmasıdır. (DNS değişikliği hizmet ve program paketi kurma, kaldırma gibi)

## Gelişmiş Ayarlar

### Klasör Yönetimi
- **Klasör Yönetimi:** WireSock koruması altında kalacak uygulama klasörlerini ekleyip çıkarmanıza ve gerektiğinde tüm listeyi temizlemenizi sağlar.
- **Özelleştirilmiş Profil Oluşturma:** Oluşturulan ücretsiz Cloudflare konfigürasyon dosyasına, klasör listesine eklediğiniz dizinleri ekleyerek "\res" klasörü içerisinde konfigürasyon dosyasının son halini kaydeder. Bu sayede başka WireGuard uygulamaları ile de bu dosyayı kullanabilirsiniz.
- **WireSock Hizmetini Kaldır:** "WireSock Hizmetini Kaldır" butonu, WireSock hizmetini kaldırıp korumayı durdurmak için veya tekrar yeni bir hizmet kurulumu yapmak için kullanılabilir.

### WireSock, ByeDPI, ProxiFyre ve GoodbyeDPI Hizmetlerini & drover Dosyalarını Kaldırma
- WireSock, ByeDPI, ProxiFyre ve GoodbyeDPI hizmetlerini kaldırmak için Gelişmiş sekmesindeki **Hizmetleri Kaldır** butonuna tıklamanız yeterlidir. Bu sayede bağlantınız eski haline gelir.
- GoodbyeDPI Hizmeti bir sebepten dolayı otomatik olarak kaldırılamazsa **[GoodbyeDPI Doğru Şekilde Kaldırma Rehberi](https://github.com/cagritaskn/GoodbyeDPI-Turkey/blob/master/REVERT.md)**'ni takip ederek kaldırmayı deneyebilirsiniz.

## Teşekkürler ve Atıflar

- Yazılımın geliştirilmesine katkıda bulunan **[Techolay.net](https://techolay.net/sosyal/)** kurucusu **[Recep Baltaş](https://www.youtube.com/@Techolay/)**'a çok teşekkür ederim.
- **[ByeDPI Split Tunneling metodu](https://www.youtube.com/watch?v=rkBL_kHBfm4)** rehberi ve tüm emekleri için **[Bal Porsuğu](https://www.youtube.com/@sauali)**'na çok teşekkür ederim.
- **[wgcf](https://github.com/ViRb3/wgcf)** by **[ViRb3](https://github.com/ViRb3)**
- **[ProxiFyre](https://github.com/wiresock/proxifyre)** by **[Vadim Smirnov](https://github.com/wiresock)**
- **[ByeDPI](https://github.com/hufrea/byedpi)** by **[hufrea](https://github.com/hufrea/)**
- **[WireSock](https://www.wiresock.net/)** by **[Vadim Smirnov](https://github.com/wiresock)**
- **[drover](https://github.com/hdrover/discord-drover)** by **[hdrover](https://github.com/hdrover)**

### El ile Yapılandırma
```xml
...
[Peer]
PublicKey = ...
AllowedIPs = ...
Endpoint = ...
# Eklenebilecek örnek satırlar
AllowedApps = C:\Program Files\Program Z\ProgramZ.exe, C:\Users\kullanici-adi\AppData\Local\ProgramX\, ProgramY.exe
...
```

## Nasıl Çalışır

### Standart, Alternatif ve Özelleştirilmiş Kurulum
Öncelikle wgcf ile profil dosyası oluşturup WireSock istemcisi ile bu profil dosyasını kullanır ve yanlızca Discord için ayrık tünelleme başlatır.

### ByeDPI ST ve ByeDPI DLL Kurulum
Öncelikle ByeDPI hizmeti kurulur ve ST metodunda ProxiFyre kullanarak bu proxy seçili uygulamalar için çalıştırılır, DLL metodunda ise drover dosyaları otomatik DLL enjeksiyonu ile Discord'un localhost'ta ByeDPI tarafından başlatılan proxy'nin kullanılmasını sağlar.

## Tekrar Derleme (Recompiling)

### C# Kullanarak Programı Tekrar Derleme
Gereksinimler:
- **.NET 6.0 SDK** veya üzeri
- **Visual Studio 2022** veya **Visual Studio Code**
- **Windows 10/11** işletim sistemi

### Derleme Adımları

1. **Kaynak Kodu İndirin**
   ```bash
   git clone https://github.com/cagritaskn/SplitWire-Turkey.git
   cd SplitWire-Turkey/src
   ```

2. **Bağımlılıkları Yükleyin**
   ```bash
   cd SplitWireTurkey
   dotnet restore
   ```

3. **Uygulamayı Derleyin**
   ```bash
   # Basit derleme
   dotnet build -c Release
   
   # Veya batch script kullanın (Önerilen)
   ..\build_simple.bat
   ```

### InnoSetup Kullanarak Kurulum Yürütülebilirini Tekrar Derleme
Gereksinimler:
- **InnoSetup 6**
- **Windows 10/11** işletim sistemi

### Derleme Adımları

1. **C# Programını Derleyin ve Çıktı SplitWire-Turkey.exe'nin Bulunduğu Klasörde Gidin**

2. **Prerequisites Klasörü ve İçeriğini Bulunduğunuz Klasöre Kopyalayın** (Desktop Runtime Dosyalarının Prerequisites klasörüne yüklenmesi mümkün değil, çünkü dosya boyut sınırını aşıyor. Bunun yerine manuel olarak windowsdesktop-runtime-6.0.35-win-x64.exe ve windowsdesktop-runtime-6.0.35-win-x86.exe dosyalarını bu klasöre siz yerleştirmelisiniz.)

3. **Bulunduğunuz Klasörde Bir Komut Satırı Açıp Kurulum Yürütülebilirini Derleyin**
   ```bash
   iscc "SplitWire-Turkey-Setup.iss"
   ```

## Telif Hakkı

```
Copyright © 2025 Çağrı Taşkın

Bu proje MIT lisansı altında lisanslanmıştır.
Detaylar için LICENSE dosyasına bakın.
```


## Bağış ve Destek

Bu programı kullanmak tamamen ücretsizdir. Kullanımından herhangi bir gelir elde etmiyorum. Ancak çalışmalarıma devam edebilmem için aşağıda bulunan bağış adreslerinden beni destekleyebilirsiniz. Github üzerinden (bu sayfanın en üstünden) projeye yıldız da bırakabilirsiniz.

**GitHub Sponsor:**

[![Sponsor](https://img.shields.io/static/v1?label=Sponsor&message=%E2%9D%A4&logo=GitHub&color=%23fe8e86)](https://github.com/sponsors/cagritaskn)

**Patreon:**

[![Static Badge](https://img.shields.io/badge/cagritaskn-purple?logo=patreon&label=Patreon)](https://www.patreon.com/cagritaskn/membership)



## Sorumluluk Reddi Beyanı

**Bu yazılım eğitim amaçlı oluşturulmuştur.**

- Bu araç sadece kodlama eğitimi ve kişisel kullanım amaçlıdır
- Ticari kullanım için uygun değildir
- Geliştirici, bu yazılımın kullanımından doğabilecek herhangi bir zarardan sorumlu değildir
- Kullanıcılar bu yazılımı kendi sorumluluklarında kullanırlar
- Discord isimli programın seçilmesi, ilgili yazılımın DPI ile erişilemez kılınan bir program üzerinde denenmesi gerekmesidir
- Yasal düzenlemelere uygun kullanım kullanıcının sorumluluğundadır
> [!IMPORTANT]
> Bu programın kullanımından doğan her türlü yasal sorumluluk kullanan kişiye aittir. Uygulama yalnızca eğitim ve araştırma amaçları ile yazılmış ve düzenlenmiş olup; bu uygulamayı bu şartlar altında kullanmak ya da kullanmamak kullanıcının kendi seçimidir. Açık kaynak kodlarının paylaşıldığı Github isimli platformdaki bu proje, bilgi paylaşımı ve kodlama eğitimi amaçları ile yazılmış ve düzenlenmiştir.



