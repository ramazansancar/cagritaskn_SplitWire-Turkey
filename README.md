<p align="center">
  <img width="auto" height="128" src="https://github.com/cagritaskn/SplitWire-Turkey/blob/main/src/SplitWireTurkey/Resources/splitwire-logo-128.png">
</p>

# <p align="center"><strong></b>SplitWire-Turkey</strong></p>

[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)
[![Downloads](https://img.shields.io/github/downloads/cagritaskn/SplitWire-Turkey/total.svg)](https://github.com/cagritaskn/SplitWire-Turkey/releases/tag/1.0.0)

## Özet

SplitWire-Turkey, WireGuard VPN yapılandırmasını kolaylaştırmak için geliştirilmiş açık kaynak bir Windows uygulamasıdır. Bu araç, kullanıcıların ücretsiz Cloudflare konfigürasyon dosyası oluşturmalarını ([wgcf](https://github.com/ViRb3/wgcf) aracılığı ile) ve [WireSock](https://www.wiresock.net/) sayesinde bu konfigürasyonu **yalnızca** tercih edilen uygulamalar özelinde kullanmalarını sağlar. Ayrıca hizmet kurulumu yaptığı için Windows'u her yeniden başlattığınızda tekrar kurulum yapmanıza gerek kalmaz.

**Özellikler:**
-  **Hızlı Kurulum:** Tek tıkla kullanım
-  **Özelleştirilmiş Kurulum:** Gelişmiş ayarlarla özel yapılandırma
-  **Servis Yönetimi:** WireSock servisini otomatik olarak kurma/kaldırma
-  **Klasör Yönetimi:** Tercih edilen uygulama klasörlerini kolayca ekleme/çıkarma
-  **Ücretsiz:** Programı kullanmak ve Cloudflare konfigürasyon dosyası oluşturmak tamamen ücretsiz

## SplitWire-Turkey

SplitWire-Turkey, Türkiye'deki internet kullanıcıları için özel olarak tasarlanmış bir WireGuard yapılandırma ve ücretsiz Cloudflare profili oluşturup kullanma aracıdır. Bu uygulama, WireGuard VPN teknolojisini kullanarak güvenli ve hızlı internet bağlantısı sağlamak isteyen kullanıcılar için geliştirilmiştir. Hizmet kurulumu yaptığı için bilgisayarınızı yeniden başlattığınızda ilgili uygulamalara erişmek için fazladan bir işlem yapmanıza gerek kalmaz. Tamamen açık kaynak kodlu olan bu uygulamanın kaynak kodları repository'de bulunan /src klasörünün içinde mevcuttur.

> [!NOTE]
> Windows 7, 8, 8.1, 10 veya 11 işletim sistemlerinde **yönetici olarak çalıştırmanız** mecburidir.

## SplitWire-Turkey Kullanımı

> [!IMPORTANT]
> GoodbyeDPI veya Cloudflare WARP ya da farklı bir VPN uygulaması kullanıyorsanız bu uygulamayı kaldırmalısınız. [GoodbyeDPI-Turkey Doğru Şekilde Kaldırma Rehberi](https://github.com/cagritaskn/GoodbyeDPI-Turkey/blob/master/REVERT.md)'ni takip ederek kaldırma işlemini gerçekleştirebilirsiniz.

### Standart Kurulum (Tavsiye Edilen)
- [SplitWire-Turkey-Setup-Windows.exe](https://github.com/cagritaskn/SplitWire-Turkey/releases/download/1.0.0/SplitWire-Turkey-Setup-Windows.exe) kurulum paketini indirip SplitWire-Turkey kurulumunu gerçekleştirin.
- SplitWire-Turkey uygulamasını açın. (SmartScreen "Windows kişisel bilgisayarınızı korudu" uyarısı alırsanız "Ek bilgi" yazısına tıkladıktan sonra "Yine de çalıştır" butonuna tıklayın, virüs taraması ve bu uyarı hakkında bilgi aşağıda verilmiştir)
- Standart Kurulum butonuna tıklayın. (Eğer WireSock yüklü değilse sizin için indirip kurulumu başlatacaktır. WireSock kurulumu tamamlandıktan sonra kurulum penceresini kapatıp tekrar Standart Kurulum butonuna basın)
- "WireSock hizmeti kuruldu ve başlatıldı" uyarısını aldığınızda program çalışmaya başlamış demektir.

> [!NOTE]
> Standart kurulum yalnızca Discord için WireSock koruması sağlar.

### Gelişmiş Kurulum (Özelleştirilmiş Konfigürasyon Dosyasıyla)
- [SplitWire-Turkey-Setup-Windows.exe](https://github.com/cagritaskn/SplitWire-Turkey/releases/download/1.0.0/SplitWire-Turkey-Setup-Windows.exe) kurulum paketini indirip SplitWire-Turkey kurulumunu gerçekleştirin.
- SplitWire-Turkey uygulamasını açın. (SmartScreen "Windows kişisel bilgisayarınızı korudu" uyarısı alırsanız "Ek bilgi" yazısına tıkladıktan sonra "Yine de çalıştır" butonuna tıklayın, virüs taraması ve bu uyarı hakkında bilgi aşağıda verilmiştir)
- Gelişmiş sekmesine gidin.
- "Klasör Ekle" butonu ile tercih ettiğiniz uygulamanın bulunduğu klasörü seçin.
- "Özelleştirilmiş Kurulum" butonuna tıklayın.
- "WireSock hizmeti kuruldu ve başlatıldı" uyarısını aldığınızda program çalışmaya başlamış demektir.

> [!NOTE]
> Yalnızca seçtiğiniz klasörler özelinde düzenlenmiş "wgcf-profile.conf" dosyası oluşturmak istiyorsanız klasör listesini hazırladıktan sonra "Özelleştirilmiş Profil Dosyası Oluştur" butonuna tıklayabilirsiniz.

> [!NOTE]
> SplitWire-Turkey uygulamasını daha önce kullandıysanız, yeni bir kurulum yapmadan önce "Gelişmiş" sekmesinden "WireSock Hizmetini Kaldır" butonuna tıklamanız daha sağlıklı bir kurulum gerçekleştirmenizi sağlar.

> [!NOTE]
> SplitWire-Turkey uygulamasını set-up dosyasını kullanmadan ve sisteminize yerleşik bir şekilde kurmadan, [SplitWire-Turkey-ZIP-Windows.zip](https://github.com/cagritaskn/SplitWire-Turkey/releases/download/1.0.0/SplitWire-Turkey-ZIP-Windows.zip) isimli ZIP dosyasını indirip bir klasöre ayıklayarak da kullanabilirsiniz.

### WireSock ve SplitWire-Turkey'i Sistemden Kaldırmak
- SplitWire-Turkey uygulamasını çalıştırın.
- "Gelişmiş" sekmesindeki "WireSock Hizmetini Kaldır" butonuna tıklayın. 
- Daha sonra Windows'un Program Ekle/Kaldır bölümünden hem WireSock Secure Client'i hem de SplitWire-Turkey'i kaldırın. (Ya da SplitWire-Turkey'in kurulum klasöründe bulunan "unins000.exe" isimli yürütülebilir ile kaldırın)

## Virüs & SmartScreen Uyarısı
Program açık kaynak kodlu olduğundan tüm kodu görüp inceleyebilirsiniz. SplitWire-Turkey.dll için 72 adet antivirüs yazılımından yalnızca MalwareBytes isimli antivirüs uygulaması tehdit algılamıştır. Bu sonuç, SplitWire-Turkey'in wgcf.exe ve WireSock Set-up dosyalarını internet üzerinden indirip çalıştırmasından kaynaklanır. (MalwareBytes makine öğrenme ile tespit edilen bir tehdit uyarısı veriyor, uyarının kaldırılması için rapor gönderildi). Tüm program açık kaynak kodludur ve kaynak kodu /src klasörü içerisinden incelenebilir, tercih edilirse tekrar derlenebilir. Programı kullanmak istemeyen ve güvenmeyen kullanıcılar kullanmak zorunda değildir, programı kullanmak kullanıcının inisiyatifindedir.
Dilerseniz tüm klasörü, kurulum dosyasını, .zip dosyasını ya da kaynak kodlarını [VirusTotal](https://www.virustotal.com/gui/home/upload) gibi bir sitede taratıp sonuçları inceleyebilir, dilerseniz C# dili biliyorsanız veya bilen bir tanıdığınız varsa başvurup kodun ne yapmaya çalıştığını anlayabilirsiniz. Programı imzalamadan yayınlamak bu gibi sorunlara yol açabiliyor. Programı imzalamak döviz kuruyla düzenli ödeme yapmayı gerektirdiği ve bu program ücretsiz olduğu, bununla birlikte gelir elde etmeden bakımı yapıldığı için imzalama girişiminde bulunamıyorum.
> [!NOTE]
> **[SplitWire-Turkey.exe VirusTotal sonuçlarında](https://www.virustotal.com/gui/file/7113da9f8fb88e0e81e3c31a455056a1e05574c9c2f2dc633ab788645ea047b4)** 72 adet antivirüs progamı içerisinde hiçbir antivirüs programı tehdit algılamamıştır.

> [!NOTE]
> **[SplitWire-Turkey.dll VirusTotal sonuçlarında](https://www.virustotal.com/gui/file/9b82ec51f8abea3eed580826c9bb49e4b5b97b706db4915fb76709bf81db6720)** 72 adet antivirüs progamı içerisinde yalnızca bir adet (MalwareBytes) antivirüs uygulaması tehdit algılamıştır. Bu sonuç, SplitWire-Turkey'in wgcf.exe ve WireSock Set-up dosyalarını internet üzerinden indirip çalıştırmasından kaynaklanır. MalwareBytes isimli antivirüs yazılımını kullanıyorsanız, raporumuz sonuçlanana kadar farklı bir antivirüs yazılımı tercih etmeyi düşünebilirsiniz.

## Gelişmiş Ayarlar

### Klasör Yönetimi
- **Klasör Yönetimi:** WireSock koruması altında kalacak uygulama klasörlerini ekleyip çıkarmanıza ve gerektiğinde tüm listeyi temizlemenizi sağlar.
- **Özelleştirilmiş Profil Oluşturma:** Oluşturulan ücretsiz Cloudflare konfigürasyon dosyasına, klasör listesine eklediğiniz dizinleri ekleyerek "\res" klasörü içerisinde konfigürasyon dosyasının son halini kaydeder. Bu sayede başka WireGuard uygulamaları ile de bu dosyayı kullanabilirsiniz.
- **WireSock Hizmetini Kaldır:** "WireSock Hizmetini Kaldır" butonu, WireSock hizmetini kaldırıp korumayı durdurmak için veya tekrar yeni bir hizmet kurulumu yapmak için kullanılabilir.

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

### İşlem Akışı

1. **WireGuard Profil Oluşturma**
   ```
   wgcf register → wgcf generate → wgcf-profile.conf
   ```
2. **Yapılandırma Modifikasyonu**
   ```
   wgcf-profile.conf + AllowedApps (Standart olarak yalnızca Discord) = Uygulama klasörleri eklenmiş konfigürasyon dosyası
   ```
3. **WireSock Servis Kurulumu**
   ```
   wiresock-client.exe install → net start → sc start
   ```
4. **Profil Aktivasyonu**
   ```
   WireSock hizmeti + wgcf-profile.conf = Aktif WireSock bağlantısı
   ```

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

### Kullanılan İşlevsellik Programları ve Yaratıcıları:

- **[wgcf](https://github.com/ViRb3/wgcf)** by **[ViRb3](https://github.com/ViRb3)**
- **[WireSock](https://www.wiresock.net/)** by **[Vadim Smirnov](https://github.com/wiresock)**

## Sorumluluk Reddi Beyanı

**Bu yazılım eğitim amaçlı oluşturulmuştur.**

- Bu araç sadece eğitim ve kişisel kullanım amaçlıdır
- Ticari kullanım için uygun değildir
- Geliştirici, bu yazılımın kullanımından doğabilecek herhangi bir zarardan sorumlu değildir
- Kullanıcılar bu yazılımı kendi sorumluluklarında kullanırlar
- Yasal düzenlemelere uygun kullanım kullanıcının sorumluluğundadır
> [!IMPORTANT]
> Bu programın kullanımından doğan her türlü yasal sorumluluk kullanan kişiye aittir. Uygulama yalnızca eğitim ve araştırma amaçları ile yazılmış ve düzenlenmiş olup; bu uygulamayı bu şartlar altında kullanmak ya da kullanmamak kullanıcının kendi seçimidir. Açık kaynak kodlarının paylaşıldığı Github isimli platformdaki bu proje, bilgi paylaşımı ve kodlama eğitimi amaçları ile yazılmış ve düzenlenmiştir.



