<div align="center">

# 🎵 FresAudio

**Trình phát & tải nhạc đa nền tảng hiện đại dành cho Windows**

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

<br />

<img src="FresAudio.png" alt="FresAudio Logo" width="128" height="128" />

</div>

---

## 🌟 Giới thiệu

**FresAudio** là một ứng dụng nghe nhạc và tải nhạc mạnh mẽ, được thiết kế với giao diện hiện đại phong cách Fluent / Material Design trên nền tảng **WPF (.NET 8)**. Ứng dụng mang đến trải nghiệm nghe nhạc offline mượt mà, bộ cân bằng âm thanh (Equalizer) chuẩn phòng thu, cùng khả năng tải nhạc trực tiếp từ các nền tảng phổ biến nhất hiện nay.

---

## ✨ Tính năng nổi bật

### 🎧 Trình phát nhạc Offline chuyên nghiệp
- Hỗ trợ hầu hết các định dạng âm thanh chất lượng cao: **MP3, FLAC, WAV, AAC, OGG, M4A...**
- Tự động đọc và hiển thị metadata: Tên bài hát, ca sĩ, album, ảnh bìa (Album Art) sử dụng **TagLibSharp**.
- Quản lý danh sách phát (Playlist), tạo/sửa/xóa playlist theo sở thích.
- Duyệt nhạc linh hoạt theo thư mục máy tính.
- Hiển thị sóng âm trực quan thời gian thực (**Spectrogram / Audio Visualizer**).

### 🎚️ Equalizer & Hiệu ứng âm thanh cao cấp
- Bộ cân bằng âm thanh **10-Band Equalizer** với các preset cài sẵn (Rock, Pop, Jazz, Bass Boost...).
- Điều chỉnh cao độ (Pitch) và tốc độ phát (Tempo / Speed) chuẩn xác với công nghệ **SoundTouch**.

### ⬇️ Tải nhạc trực tuyến đa nền tảng
Tích hợp bộ công cụ tải nhạc thông minh:
- 🔴 **YouTube**: Hỗ trợ tìm kiếm, tải từng bài hoặc tải toàn bộ Playlist với chất lượng âm thanh cao nhất.
- 🟠 **SoundCloud**: Tải trực tiếp các bản nhạc độc quyền và remix từ SoundCloud.
- 🟢 **Spotify**: Tải và đồng bộ bài hát / danh sách phát nhanh chóng.
- ⚫ **TikTok**: Tách âm thanh từ video TikTok xu hướng chỉ với liên kết.

### 💻 Tối ưu hoá cho Windows
- Bộ cài đặt `.exe` nhỏ gọn, đóng gói với **Inno Setup**.
- Tự động liên kết đuôi file (`.mp3`, `.flac`, `.wav`) mở bằng FresAudio.
- Dung lượng nhẹ, khởi động tức thì, hoạt động êm ái trên Windows 10 & 11 (64-bit).

---

## 🚀 Tải về & Cài đặt

Dành cho người dùng thông thường:

1. Vào mục [**Releases**](https://github.com/congtb/FresAudio/releases) (hoặc trang Release của repo).
2. Tải về file bộ cài đặt mới nhất: `FresAudio_Setup.exe`.
3. Mở file và tiến hành cài đặt theo hướng dẫn tiếng Việt.
4. Thưởng thức âm nhạc!

---

## 🛠️ Hướng dẫn Build từ mã nguồn (Dành cho Lập trình viên)

### Yêu cầu môi trường
- Hệ điều hành: **Windows 10 / 11 (x64)**
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) trở lên
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (với workload *.NET Desktop Development*) hoặc [Rider] / [VS Code]
- (Tùy chọn để tạo file Setup) [Inno Setup 6](https://jrsoftware.org/isdl.php)

### Các bước chạy dự án

1. **Clone repository:**
   ```bash
   git clone https://github.com/<your-username>/FresAudio.git
   cd FresAudio
   ```

2. **Restore dependencies & Build:**
   ```bash
   dotnet restore
   dotnet build -c Debug
   ```

3. **Chạy ứng dụng:**
   ```bash
   dotnet run --project FresAudio.csproj
   ```

### Đóng gói Release & Tạo bộ cài Installer

```powershell
# 1. Publish ứng dụng dưới dạng Single-File tự chứa (Self-contained)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o PublishApp

# 2. Biên dịch bộ cài đặt Inno Setup
ISCC.exe installer.iss
```
File cài đặt `FresAudio_Setup.exe` sẽ được tạo ra ngay tại thư mục gốc của dự án.

---

## ⚖️ Tuyên bố từ chối trách nhiệm (Disclaimer)

Dự án này được phát triển hoàn toàn vì mục đích **học tập, nghiên cứu kỹ thuật và sử dụng cá nhân**. 
- Tác giả không lưu trữ bất kỳ tệp nhạc hay nội dung có bản quyền nào trên máy chủ.
- Tính năng tải nhạc dựa trên các công cụ nguồn mở của bên thứ ba (như `yt-dlp`, `YoutubeExplode`, `SoundCloudExplode`).
- Người dùng tự chịu trách nhiệm về việc tuân thủ Điều khoản Dịch vụ (Terms of Service) và quyền sở hữu trí tuệ của từng nền tảng trực tuyến khi sử dụng phần mềm.

---

## 📄 Bản quyền (License)

Dự án được phân phối dưới giấy phép **MIT License**. Xem chi tiết tại file [LICENSE](LICENSE).

---

<div align="center">
  Được phát triển bởi <b>Công Thành</b> ❤️
</div>
