<div align="center">
  <img src="https://img.shields.io/badge/Jarvis-AI_Assistant-blue?style=for-the-badge&logo=appveyor" alt="Jarvis AI Logo">
  <h1>Jarvis C# - Advanced Windows AI Assistant</h1>
  <p>Güçlü, bağlamın farkında (context-aware) ve Windows ile tam entegre yapay zeka asistanı.</p>

  <p>
    <a href="#özellikler">Özellikler</a> •
    <a href="#kurulum">Kurulum</a> •
    <a href="#kullanım">Kullanım</a> •
    <a href="#mimari">Mimari</a>
  </p>
</div>

---

## 🤖 Proje Hakkında

**Jarvis C#**, masaüstü deneyiminizi bir üst seviyeye taşıyan, Gemini AI destekli zeki bir Windows asistanıdır. Geleneksel komut-cevap botlarından farklı olarak, bilgisayarınızda olan biteni anlayabilir, ekranınızı görebilir ve sizin yerinize karmaşık Windows işlemlerini otomatikleştirebilir. 

Iron Man'deki "Jarvis" hissiyatını yaratmak için özel olarak tasarlanmıştır.

## ✨ Öne Çıkan Özellikler

* 🔮 **Floating Orb (Sihirli Küre):** Ekranda her zaman üstte (Always-on-top) duran, WebView2 ve şeffaf Win32 pencere mimarisiyle çalışan, sürüklenebilir asistan arayüzü.
* ⌨️ **Global Kısayol (`Win+J`):** Oyun oynarken veya tam ekran bir uygulamadayken bile Jarvis'i anında uyandıran düşük seviyeli (Low-Level) klavye kancası.
* 🎙️ **Enerji Tabanlı Wake Word (VAD):** Sürekli arka planda dinleme yaparak sadece ortamda belli bir enerji eşiği aşıldığında ve "Hey Jarvis" dendiğinde tetiklenen CPU dostu dinleme sistemi.
* 👁️ **Ekran Vizyonu ve OCR:** Jarvis ekranınızı görebilir ve analiz edebilir. OCR teknolojisiyle ekrandaki metinleri okuyup size anında bağlamsal (context-aware) cevaplar sunar.
* 🧠 **Gemini AI Entegrasyonu:** Gücünü Google'ın gelişmiş Gemini modellerinden alır.
* ⚙️ **Windows Sistem Kontrolü:** Uygulama açma, ses/parlaklık kontrolü, pencere yönetimi (küçültme, büyütme, focuslama) ve hatta CMD/PowerShell komutları çalıştırma yeteneği.
* 🖥️ **3D Hologram Arayüz:** WPF destekli, dinamik 3D kafa modellemesi ile görsel geri bildirim.

## 🚀 Kurulum

1. **Depoyu Klonlayın:**
   ```bash
   git clone https://github.com/beradmc/Jarvis.git
   cd Jarvis
   ```

2. **Gereksinimler:**
   * .NET 8.0 SDK
   * Windows 10/11
   * WebView2 Runtime (Genellikle Windows 11'de yüklü gelir)

3. **API Anahtarlarını Ayarlama:**
   `Config/` klasörü içerisinde `api_keys.json` adında bir dosya oluşturun ve Google Gemini API anahtarınızı girin (Bu dosya güvenlik amacıyla Github'a yüklenmez):
   ```json
   {
     "GeminiApiKey": "BURAYA_API_ANAHTARINIZI_GIRIN"
   }
   ```

4. **Projeyi Derleyin ve Çalıştırın:**
   ```bash
   dotnet build
   dotnet run
   ```

## 🎮 Kullanım

* **Uyandırma:** Mikrofona `"Hey Jarvis"` diyerek veya klavyeden `Win + J` tuşlarına basarak asistanı aktif edebilirsiniz.
* **Şeffaf Küre (Orb):** Ekranın köşesindeki küreyi fare ile tutarak istediğiniz yere taşıyabilirsiniz. Küreye tıkladığınızda kontrol paneli açılır.
* **Sesli Komutlar:** Uygulamaları başlatmasını (Örn: "Spotify'ı aç"), sesi kısmasını veya ekranda gördüğünüz bir hata kodu hakkında bilgi vermesini isteyebilirsiniz.

## 🏗️ Mimari ve Kullanılan Teknolojiler

* **Dil/Framework:** C# / WPF / Win32 API
* **Arayüz (UI):** WPF (3D Model Renderer) & Microsoft WebView2 (HTML/CSS tabanlı şeffaf overlay)
* **Yapay Zeka:** `Google.GenAI` SDK
* **Otomasyon & Görüntü İşleme:** `FlaUI` (UI Otomasyonu), `Tesseract` (OCR), `AForge/OpenCV` destekli görüntü motoru.
* **Ses Algılama (VAD & STT):** `NAudio`, `Vosk` (Offline Türkçe Ses Tanıma).

---
<div align="center">
  <i>Geleceğin masaüstü asistan deneyimi için tasarlandı.</i>
</div>
