# SplitWire-Turkey

[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)

## 📋 Özet

SplitWire-Turkey, WireGuard VPN yapılandırmasını kolaylaştırmak için geliştirilmiş açık kaynak bir Windows uygulamasıdır. Bu araç, kullanıcıların WireGuard profillerini hızlıca oluşturmasını ve WireSock servisini yönetmesini sağlar.

**Özellikler:**
- 🚀 **Hızlı Kurulum:** Tek tıkla WireGuard profili oluşturma
- ⚙️ **Özelleştirilmiş Kurulum:** Gelişmiş ayarlarla özel yapılandırma
- 🔧 **Servis Yönetimi:** WireSock servisini kurma/kaldırma
- 📁 **Klasör Yönetimi:** Uygulama klasörlerini kolayca ekleme/çıkarma
- 🎨 **Modern Arayüz:** Material Design ile modern kullanıcı deneyimi

## 🎯 SplitWire-Turkey

SplitWire-Turkey, Türkiye'deki internet kullanıcıları için özel olarak tasarlanmış bir WireGuard yapılandırma aracıdır. Bu uygulama, WireGuard VPN teknolojisini kullanarak güvenli ve hızlı internet bağlantısı sağlamak isteyen kullanıcılar için geliştirilmiştir.

### 🛠️ Teknik Özellikler
- **Platform:** Windows 10/11
- **Framework:** .NET 6.0 WPF
- **UI Framework:** Material Design
- **Dil:** C#
- **Mimari:** MVVM Pattern

## 📖 SplitWire-Turkey Kullanımı

### 🚀 Hızlı Başlangıç

1. **Uygulamayı İndirin**
   ```bash
   # GitHub'dan en son sürümü indirin
   git clone https://github.com/cagritaskn/SplitWire-Turkey.git
   ```

2. **Yönetici Olarak Çalıştırın**
   - `SplitWire-Turkey.exe` dosyasına sağ tıklayın
   - "Yönetici olarak çalıştır" seçeneğini seçin

3. **Standart Kurulum**
   - Ana sayfada "Standart Kurulum" butonuna tıklayın
   - WireSock otomatik olarak indirilip kurulacak
   - WireGuard profili oluşturulacak

### ⚙️ Gelişmiş Kullanım

#### Özelleştirilmiş Kurulum
1. "Gelişmiş Ayarlar" sekmesine geçin
2. "Özelleştirilmiş Kurulum" butonuna tıklayın
3. İstediğiniz klasörleri ekleyin
4. Kurulumu başlatın

#### Klasör Yönetimi
- **Klasör Ekleme:** "Klasör Ekle" butonu ile klasör seçin
- **Klasör Temizleme:** "Klasörleri Temizle" ile tüm klasörleri kaldırın
- **Özel Profil:** "Özelleştirilmiş Profil Dosyası Oluştur" ile manuel yapılandırma

#### Servis Yönetimi
- **Servis Kaldırma:** "WireSock Hizmetini Kaldır" ile servisi kaldırın
- **Otomatik Başlatma:** Kurulum sonrası servis otomatik başlar

## 🔒 Güvenilirlik

### ✅ Güvenlik Özellikleri
- **Açık Kaynak:** Tüm kaynak kod GitHub'da mevcuttur
- **Şeffaf İşlemler:** Tüm işlemler loglanır ve görünür
- **Yönetici İzinleri:** Güvenlik için yönetici hakları gerekli
- **Antivirus Uyumlu:** C# ile yazılmış, güvenilir kod

### 🛡️ Veri Güvenliği
- **Yerel İşlem:** Hiçbir veri dışarı gönderilmez
- **Şifreleme:** WireGuard'ın güçlü şifreleme algoritmaları
- **Gizlilik:** Kullanıcı verileri toplanmaz

## ⚙️ Nasıl Çalışır

### 🔄 İşlem Akışı

1. **WireGuard Profil Oluşturma**
   ```
   wgcf register → wgcf generate → wgcf-profile.conf
   ```

2. **Yapılandırma Modifikasyonu**
   ```
   wgcf-profile.conf + AllowedApps = Uygulama klasörleri
   ```

3. **WireSock Servis Kurulumu**
   ```
   wiresock-client.exe install → net start → sc start
   ```

4. **Profil Aktivasyonu**
   ```
   WireSock + wgcf-profile.conf = Aktif VPN bağlantısı
   ```

### 📁 Dosya Yapısı
```
SplitWire-Turkey/
├── src/
│   ├── SplitWireTurkey/
│   │   ├── MainWindow.xaml
│   │   ├── MainWindow.xaml.cs
│   │   ├── Services/
│   │   │   ├── WireGuardService.cs
│   │   │   └── WireSockService.cs
│   │   └── SplitWireTurkey.csproj
│   ├── build_csharp.bat
│   ├── build_simple.bat
│   └── README.md
├── resources/
│   ├── splitwire.ico
│   ├── splitwire-logo-128.png
│   └── splitwireturkeytext.png
└── releases/
    └── SplitWire-Turkey.exe
```

## 🎛️ Gelişmiş Ayarlar

### 📋 Klasör Yönetimi
- **Uygulama Klasörleri:** VPN dışında kalacak uygulamalar
- **Sistem Klasörleri:** Windows sistem dosyaları
- **Özel Klasörler:** Kullanıcı tanımlı klasörler

### 🔧 Manuel Yapılandırma
```xml
<!-- wgcf-profile.conf örneği -->
[Interface]
PrivateKey = ...
Address = ...
DNS = ...

[Peer]
PublicKey = ...
AllowedIPs = ...
Endpoint = ...

# SplitWire-Turkey eklenen satırlar
AllowedApps = C:\Program Files\Chrome\chrome.exe
AllowedApps = C:\Users\Kullanici\AppData\Local\Discord\Discord.exe
```

## 📄 Telif Hakkı

```
Copyright © 2025 Çağrı Taşkın

Bu proje MIT lisansı altında lisanslanmıştır.
Detaylar için LICENSE dosyasına bakın.
```

## ⚠️ Sorumluluk Reddi Beyanı

**Bu yazılım eğitim amaçlı oluşturulmuştur.**

- Bu araç sadece eğitim ve kişisel kullanım amaçlıdır
- Ticari kullanım için uygun değildir
- Geliştirici, bu yazılımın kullanımından doğabilecek herhangi bir zarardan sorumlu değildir
- Kullanıcılar bu yazılımı kendi sorumluluklarında kullanırlar
- Yasal düzenlemelere uygun kullanım kullanıcının sorumluluğundadır

## 🔨 Tekrar Derleme

### 📋 Gereksinimler
- **.NET 6.0 SDK** veya üzeri
- **Visual Studio 2022** veya **Visual Studio Code**
- **Windows 10/11** işletim sistemi

### 🚀 Derleme Adımları

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
   
   # Veya batch script kullanın
   ..\build_simple.bat
   ```

4. **Yayınlama (Opsiyonel)**
   ```bash
   # Tek dosya oluşturma
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
   
   # Veya batch script kullanın
   ..\build_csharp.bat
   ```

### 📁 Çıktı Dosyaları
```
bin/Release/net6.0-windows/
├── SplitWire-Turkey.exe
├── SplitWire-Turkey.dll
└── res/
    ├── splitwire.ico
    ├── splitwire-logo-128.png
    └── splitwireturkeytext.png
```

## 🤝 Katkıda Bulunma

### 🐛 Hata Bildirimi
1. GitHub Issues bölümünde yeni issue oluşturun
2. Hata detaylarını ve adımları açıklayın
3. Sistem bilgilerini paylaşın

### 💡 Özellik Önerisi
1. Feature request issue oluşturun
2. Özelliğin faydalarını açıklayın
3. Kullanım senaryolarını belirtin

### 🔧 Kod Katkısı
1. Fork yapın
2. Feature branch oluşturun
3. Değişikliklerinizi commit edin
4. Pull request gönderin

## 📞 İletişim

- **GitHub:** [@cagritaskn](https://github.com/cagritaskn)
- **Repository:** [SplitWire-Turkey](https://github.com/cagritaskn/SplitWire-Turkey)
- **Issues:** [GitHub Issues](https://github.com/cagritaskn/SplitWire-Turkey/issues)

## 📈 Sürüm Geçmişi

### v1.0.0 (2025-01-XX)
- ✅ İlk sürüm
- ✅ WireGuard profil oluşturma
- ✅ WireSock servis yönetimi
- ✅ Modern WPF arayüzü
- ✅ Material Design teması
- ✅ Klasör yönetimi
- ✅ Otomatik wgcf indirme

---

**⭐ Bu projeyi beğendiyseniz yıldız vermeyi unutmayın!** 