# Fit To Work — PT. Adhi Kartiko Pratama, Tbk

Aplikasi web untuk pengisian formulir *Fit To Work* harian karyawan, dibangun dengan:

- **ASP.NET Core 8 MVC** (C#)
- **Bootstrap 5** (via CDN) untuk UI
- **Dapper** untuk akses database (SQL Server)
- **Cookie Authentication** bawaan ASP.NET Core untuk 2 peran: **Karyawan** dan **Admin**

## Fitur

1. **Halaman publik — Absen dengan NIP** (`/Account/EmployeeLogin`)
   Karyawan cukup mengetik atau *scan* **NIP** saja (tanpa password) lalu langsung
   diarahkan ke form pengisian. Cocok dipakai di kios/tablet bersama di lokasi kerja.

2. **Halaman publik — Form Fit To Work** (`/Form`, hanya bisa diakses setelah login karyawan)
   - **Nama** dan **Section** otomatis terisi (read-only) dari data karyawan hasil login — tidak bisa diubah lewat form, diambil ulang dari database saat submit demi keamanan.
   - Sisanya (nomor HP, jadwal kerja, area kerja, jam tidur, kualitas tidur, gejala kesehatan, kondisi fokus, konsumsi obat, kesiapan kerja) diisi sesuai form asli.

3. **Halaman Admin** (login terpisah di `/Account/AdminLogin`)
   - **Dashboard** (`/Admin/Dashboard`) — ringkasan jumlah karyawan aktif, jumlah pengisian hari ini, jumlah fit/tidak fit.
   - **Data Karyawan** (`/Admin/Employees`) — tambah, edit, cari, aktif/nonaktifkan karyawan.
   - **Riwayat Pengisian** (`/Admin/History`) — lihat semua histori pengisian Fit To Work dengan filter NIP/nama/section/kesiapan/tanggal, dan halaman detail per pengisian.
   - **Manajemen User Admin** (`/Admin/Users`) — tambah akun admin baru, ganti password (baik password sendiri maupun reset password admin lain), dan hapus akun admin. Semua dilakukan lewat aplikasi, tidak perlu query manual ke database.

## Struktur Proyek

```
FitToWorkApp/
├── Controllers/
│   ├── AccountController.cs        # Login/logout karyawan & admin
│   ├── FormController.cs           # Form publik (khusus role Employee)
│   ├── AdminController.cs          # Dashboard admin
│   └── Admin/
│       ├── EmployeesController.cs  # CRUD karyawan (khusus role Admin)
│       └── HistoryController.cs    # Riwayat pengisian (khusus role Admin)
├── Models/                         # Entity + ViewModel
├── Data/                           # Koneksi database (Dapper)
├── Repositories/                   # Query Dapper per entity
├── Services/PasswordHasher.cs      # Hashing password (BCrypt)
├── Views/
├── Database/schema.sql             # Skrip pembuatan tabel SQL Server
└── Program.cs
```

## Menjalankan Aplikasi

> **Deploy pakai Docker?** Lihat [`README-Docker.md`](./README-Docker.md) — cukup `docker compose up --build -d`, tidak perlu install .NET SDK/SQL Server manual.


> Catatan: proyek ini dibuat di lingkungan sandbox tanpa akses ke NuGet, sehingga
> `dotnet restore` **belum pernah dijalankan/diverifikasi** di sini. Jalankan langkah
> di bawah ini di komputer/server Anda yang punya akses internet ke nuget.org.

### 1. Prasyarat
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- SQL Server (LocalDB / Express / instance biasa)

### 2. Siapkan database
Jalankan `Database/schema.sql` di SQL Server Management Studio / `sqlcmd` /
Azure Data Studio. Script ini akan membuat database `FitToWorkDb` beserta
tabel `Employees`, `AdminUsers`, dan `FitToWorkSubmissions`.

### 3. Atur connection string
Sunting `appsettings.json` (atau lebih baik gunakan `dotnet user-secrets` /
`appsettings.Development.json` supaya tidak ter-commit) bagian:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=FitToWorkDb;User Id=sa;Password=...;TrustServerCertificate=True;"
}
```

### 4. Restore & jalankan

```bash
cd FitToWorkApp
dotnet restore
dotnet run
```

Aplikasi akan berjalan di `https://localhost:5001` (atau port yang ditampilkan di terminal).

### 5. Login pertama kali

- **Admin default** dibuat otomatis saat aplikasi pertama kali start (jika tabel
  `AdminUsers` masih kosong): `username: admin` / `password: Admin123!`.
  **Segera ganti password ini** setelah login pertama lewat menu
  **🔒 Ganti Password Saya** di navbar admin, atau tambahkan admin baru lewat
  menu **Manajemen User Admin** lalu nonaktifkan/hapus akun default ini.
- **Karyawan** cukup masuk dengan NIP yang didaftarkan Admin lewat menu
  **Data Karyawan → Tambah Karyawan** — tidak perlu password.

## Catatan Desain & Asumsi

- **Login karyawan hanya berdasarkan NIP** (tanpa password), sesuai permintaan —
  cocok untuk skenario kios/tablet bersama di lapangan dengan scan kartu ID.
  **Konsekuensi keamanan**: siapa pun yang tahu/bisa menebak NIP orang lain bisa
  mengisi form atas nama karyawan tersebut. Jika perangkat dipakai bersama
  (shared device) dan bukan milik pribadi karyawan, ini biasanya dianggap wajar
  untuk kasus penggunaan seperti ini — tapi kalau ke depan butuh verifikasi lebih
  ketat, tinggal tambahkan lagi PIN/password atau integrasi kartu RFID.
- **Gejala kesehatan** disimpan sebagai teks gabungan dipisah koma pada satu
  kolom (`GejalaKesehatan`) untuk kesederhanaan, bukan tabel relasi terpisah.
- Karyawan **boleh mengisi form lebih dari sekali dalam sehari** (misalnya jika
  kondisi berubah di tengah shift) — sistem hanya memberi info jika sudah
  pernah mengisi hari itu, tidak memblokir.
- Data diri (**Nama**, **Section**) di form pengisian selalu diambil ulang dari
  database berdasarkan sesi login, bukan dari input form, untuk mencegah
  manipulasi data pada saat submit.
- **Manajemen User Admin**: admin tidak bisa menghapus akun miliknya sendiri
  atau menghapus admin terakhir yang tersisa (supaya sistem tidak pernah
  kehilangan akses admin sama sekali). Reset password admin lain tidak perlu
  tahu password lama (wewenang admin), tapi ganti password milik sendiri tetap
  wajib memasukkan password saat ini.
