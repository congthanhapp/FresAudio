<div align="center">

# 🎵 FresAudio

### Phần mềm phát nhạc offline trên máy tính & hỗ trợ tải nhạc

<p align="center">
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8" /></a>
  <a href="https://www.microsoft.com/windows"><img src="https://img.shields.io/badge/Platform-Windows-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Platform Windows" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-success?style=for-the-badge" alt="License MIT" /></a>
  <a href="https://github.com/congthanhapp/FresAudio/releases"><img src="https://img.shields.io/badge/Download-FresAudio__Setup.exe-FF5722?style=for-the-badge&logo=windows-terminal&logoColor=white" alt="Download" /></a>
</p>

<img src="FresAudio.png" alt="FresAudio Logo" width="120" height="120" />

<br/>

**FresAudio** là ứng dụng nghe nhạc cục bộ (offline) trên máy tính Windows, tích hợp bộ cân bằng âm thanh Equalizer và tính năng hỗ trợ tải nhạc từ **YouTube, SoundCloud, Spotify, TikTok** về máy để nghe lại thuận tiện.

[Tính Năng](#-tính-năng-chính) • [Tải Về Cài Đặt](#-tải-về--cài-đặt-1-click) • [Phím Tắt](#-phím-tắt-tiện-lợi) • [Hướng Dẫn Build](#-hướng-dẫn-dành-cho-lập-trình-viên) • [Miễn Trừ Trách Nhiệm](#-tuyên-bố-từ-chối-trách-nhiệm-disclaimer)

</div>

---

## 🌟 Tính Năng Chính

<table>
  <tr>
    <td width="50%">
      <h3>🎧 Phát Nhạc Offline (Cục Bộ)</h3>
      <ul>
        <li>Hỗ trợ phát các định dạng phổ biến: <b>MP3, FLAC, WAV, AAC, OGG, M4A...</b></li>
        <li>Tự động đọc <b>Metadata ID3v2</b>, ảnh bìa album (Album Cover Art) sắc nét.</li>
        <li>Quản lý danh sách phát (Playlist), tạo thư mục bài hát yêu thích không giới hạn.</li>
        <li>Duyệt nhạc theo cấu trúc cây thư mục ổ đĩa cực nhanh.</li>
      </ul>
    </td>
    <td width="50%">
      <h3>🎚️ Studio Equalizer & Hiệu Ứng</h3>
      <ul>
        <li><b>10-Band Graphic Equalizer</b> chuẩn xác từ 31Hz đến 16kHz.</li>
        <li>Các Preset chuyên nghiệp: <i>Bass Boost, Pop, Rock, Jazz, Classical, Vocal...</i></li>
        <li>Công nghệ <b>SoundTouch</b>: Thay đổi tốc độ phát (Tempo) và cao độ (Pitch) mượt mà không vỡ tiếng.</li>
        <li>Cửa sổ hiển thị sóng âm dải tần <b>Spectrogram Visualizer</b> theo thời gian thực.</li>
      </ul>
    </td>
  </tr>
  <tr>
    <td width="50%">
      <h3>⬇️ Hỗ Trợ Tải Nhạc Về Máy</h3>
      <ul>
        <li>🔴 <b>YouTube:</b> Tải bài hát hoặc Playlist về máy, tự động lưu thông tin bài hát.</li>
        <li>🟠 <b>SoundCloud:</b> Tải nhanh các bản nhạc độc quyền, DJ Mix & Remix.</li>
        <li>🟢 <b>Spotify:</b> Tìm kiếm và tải bài hát/playlist yêu thích về máy.</li>
        <li>⚫ <b>TikTok:</b> Tách nhạc nền cực chuẩn từ video TikTok theo link.</li>
      </ul>
    </td>
    <td width="50%">
      <h3>🎨 Giao Diện & Tối Ưu Hệ Thống</h3>
      <ul>
        <li>Phong cách <b>Material Design</b> hiện đại, mượt mà và trực quan.</li>
        <li>Đa dạng chủ đề: <i>Dark, Light, Ghost, Transparent Mica / Glassmorphism</i>.</li>
        <li>Tự động liên kết đuôi file âm thanh (.mp3, .flac, .wav) mở trực tiếp bằng FresAudio.</li>
        <li>Bộ cài đặt Windows tự động, không yêu cầu cài thêm phần mềm phụ trợ.</li>
      </ul>
    </td>
  </tr>
</table>

---

## 🚀 Tải Về & Cài Đặt (1 Click)

Dành cho người dùng trải nghiệm ngay ứng dụng mà không cần biết lập trình:

1. Truy cập vào trang [**Releases của FresAudio**](https://github.com/congthanhapp/FresAudio/releases).
2. Tải về phiên bản mới nhất: **`FresAudio_Setup.exe`**.
3. Khởi chạy file vừa tải, làm theo hướng dẫn tiếng Việt của bộ cài và bắt đầu thưởng thức âm nhạc.

---

## ⌨️ Phím Tắt Tiện Lợi

| Phím Tắt | Chức Năng |
| :--- | :--- |
| <kbd>Space</kbd> | Tạm dừng (Pause) / Tiếp tục phát (Play) |
| <kbd>→</kbd> / <kbd>←</kbd> | Tua nhanh tới / lùi bài hát |
| <kbd>Ctrl</kbd> + <kbd>→</kbd> | Chuyển sang bài tiếp theo (Next Track) |
| <kbd>Ctrl</kbd> + <kbd>←</kbd> | Quay lại bài trước (Previous Track) |
| <kbd>↑</kbd> / <kbd>↓</kbd> | Tăng / giảm âm lượng |
| <kbd>Ctrl</kbd> + <kbd>O</kbd> | Mở file nhạc từ máy tính |
| <kbd>Ctrl</kbd> + <kbd>E</kbd> | Bật cửa sổ 10-Band Equalizer |

---

## 🛠️ Hướng Dẫn Dành Cho Lập Trình Viên

Nếu bạn muốn chỉnh sửa, phát triển thêm tính năng hoặc tự biên dịch ứng dụng từ mã nguồn:

### 1. Yêu Cầu Cài Đặt
- Hệ điều hành: **Windows 10 / 11 (64-bit)**
- **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**
- **Visual Studio 2022** (chọn workload *.NET Desktop Development*) hoặc **JetBrains Rider** / **VS Code**
- **[Inno Setup 6](https://jrsoftware.org/isdl.php)** (dùng để đóng gói file cài đặt `.exe`)

### 2. Tải Mã Nguồn & Chạy Dự Án
```bash
# Clone repository về máy tính
git clone https://github.com/congthanhapp/FresAudio.git
cd FresAudio

# Khôi phục các thư viện NuGet phụ thuộc
dotnet restore

# Chạy ứng dụng ở chế độ Debug
dotnet run --project FresAudio.csproj
```

### 3. Đóng Gói Bộ Cài Đặt (Release Setup)
```powershell
# Bước 1: Publish ứng dụng Single-File độc lập (Self-contained)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o PublishApp

# Bước 2: Biên dịch bộ cài đặt Inno Setup
ISCC.exe installer.iss
```
File cài đặt `FresAudio_Setup.exe` sẽ được tạo ngay trong thư mục gốc.

---

## 🧩 Công Nghệ & Thư Viện Nguồn Mở Sử Dụng

FresAudio trân trọng cảm ơn sự đóng góp của cộng đồng mã nguồn mở và các dự án:
- [NAudio](https://github.com/naudio/NAudio) - Thư viện xử lý và phát âm thanh cốt lõi trên .NET.
- [SoundTouch.Net](https://github.com/haugen/SoundTouch.Net) - Thuật toán xử lý Tempo & Pitch âm thanh chất lượng cao.
- [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode) & [yt-dlp](https://github.com/yt-dlp/yt-dlp) - Công cụ giải mã luồng video và tải âm thanh YouTube mạnh mẽ.
- [SoundCloudExplode](https://github.com/Tyrrrz/SoundCloudExplode) - Thư viện tương tác với hệ thống SoundCloud.
- [TagLibSharp](https://github.com/mono/taglib-sharp) - Đọc và ghi metadata thẻ bài hát.
- [MaterialDesignInXaml](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) - Bộ giao diện Material Design dành cho WPF.

---

## ⚖️ Tuyên Bố Từ Chối Trách Nhiệm (Disclaimer)

- **FresAudio** được phát triển hoàn toàn với mục đích **nghiên cứu kỹ thuật, học tập và phục vụ nhu cầu giải trí cá nhân**.
- Tác giả **không sở hữu, không lưu trữ và không chịu trách nhiệm** về bất kỳ nội dung âm thanh bản quyền nào được tải thông qua các công cụ của bên thứ ba.
- Người dùng tự chịu trách nhiệm về việc tuân thủ Điều khoản sử dụng dịch vụ (Terms of Service) và quyền tác giả của từng nền tảng trực tuyến.

---

## 📄 Bản Quyền (License)

Dự án được phân phối dưới giấy phép mã nguồn mở [**MIT License**](LICENSE).

