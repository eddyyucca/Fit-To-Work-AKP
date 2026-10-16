# Changelog

Semua perubahan penting pada **AKP Sehat** (dulu: Fit To Work) dicatat di file ini.
Format mengikuti [Keep a Changelog](https://keepachangelog.com/id-ID/1.1.0/). Versi ditandai dengan tanggal rilis.

Kategori: **Baru** (fitur baru) · **Ubah** (perubahan perilaku) · **Perbaikan** (bug fix) · **Teknis** (internal, tidak terlihat pengguna).

> **Catatan deploy umum:** backup database → jalankan migrasi SQL baru (urut) → `git pull` → `docker compose -f docker-compose.production.yml up --build -d` → centang hak akses baru di Role → smoke test.

---

## 2026-10-17 — Sinkron HRIS otomatis tiap jam

### Baru
- **Sinkron karyawan dari HRIS otomatis** (bawaan tiap 60 menit, atur lewat `HRIS_SYNC_INTERVAL_MINUTES`, 0 = mati). Tombol **Sinkron Sekarang** dan Dry-run tetap ada.
- Halaman Sinkron HRIS menampilkan kapan sinkron terakhir berjalan, hasilnya, dan galat terakhir.

### Ubah
- Batas waktu permintaan ke HRIS 60 -> 300 detik; sinkron manual dan otomatis tidak bisa berjalan bersamaan (bergantian).
- Kontainer web dapat menjangkau HRIS di server yang sama lewat `host.docker.internal`.

### Deploy
- Tanpa migrasi.
- Isi `HRIS_API_URL` dan `HRIS_API_TOKEN` di `.env` server (token sama dengan `HRIS_API_TOKEN` di HRIS), lalu `up --build -d`.

---

## 2026-10-08 — Password Riwayat Saya & Pengisian FTW per Section

### Baru
- Riwayat Saya memakai **NIP + password**. Password awal = tanggal lahir (`ddmmyyyy`) dan wajib diganti saat login pertama.
- Menu **Ganti Password** di Riwayat Saya (password lama wajib diisi).
- **Reset Password Riwayat Saya** oleh admin di Detail Karyawan, beserta status password.
- Dashboard FTW: kartu **Pengisian FTW per Section** (Hari / Minggu / Bulan, achievement berwarna ≥ 90 / 70–89,9 / < 70, daftar karyawan belum isi). Ikut masuk ke export PDF.

### Ubah
- Log Akses mendapat jenis event "Ganti password Riwayat Saya".

### Deploy
- Migrasi: `migration_portal_password.sql` (**wajib sebelum rebuild**).
- Umumkan ke karyawan cara login baru Riwayat Saya.

---

## 2026-10-04 — Tes Urin & Alkohol untuk Karyawan Mitra

### Baru
- Tes urin & alkohol untuk **karyawan mitra**: input, daftar, Excel, detail, rekam medis, dan tombol "+ Tes" di Karyawan Mitra.
- Filter **Karyawan (AKP / Mitra)** dan pilihan nama mitra di daftar Tes Urin & Alkohol.
- Excel Tes Urin: kolom Karyawan (AKP / Mitra) dan Jabatan.

### Ubah
- Reset Data modul Karyawan Mitra ikut mereset Tes Urin & Alkohol.

### Deploy
- Tanpa migrasi.

---

## 2026-10-04 — Aktivitas Kebugaran

### Baru
- **Aktivitas Kebugaran**: Gym, Lari, Jalan Cepat, Hiking, Bersepeda, Basket, Sepak Bola, Badminton (jenis bisa ditambah admin).
- Rekam **jarak GPS, pace, dan peta rute** untuk aktivitas luar ruang (layar dijaga menyala, rekaman tidak hilang saat halaman dimuat ulang).
- Menu admin **Jenis Aktivitas** (GPS, wajib berat badan, batas jam sesi, kunci lokasi, batas kecepatan, urutan).
- Peta rute di Riwayat Aktivitas dan Riwayat Saya.
- Dashboard: tabel per jenis aktivitas dan Top 10 paling aktif.

### Ubah
- Gym → Aktivitas Kebugaran di semua menu.
- Batas sesi dan kunci lokasi diatur per jenis aktivitas (Gym tetap 3 jam, mulai & selesai di site).
- Berat badan hanya untuk jenis yang mewajibkan.
- Status "Tidak check-out" → "Tidak diselesaikan".
- Nama file Excel: `Aktivitas_Kebugaran_*.xlsx` (+ kolom Aktivitas, Jarak, Pace; ringkasan per aktivitas).

### Teknis
- `FitnessTrack` (C#) dan `fitness-track.js` memakai aturan perhitungan jarak yang identik; server selalu menghitung ulang dari titik mentah.

### Deploy
- Migrasi: `migration_fitness_activity.sql`.
- Hak akses baru: `fitness.types` (Jenis Aktivitas).

---

## 2026-10-04 — NIP Diingat di HP

### Baru
- Login karyawan cukup sekali per HP; NIP diingat 90 hari (diperpanjang otomatis).
- Tombol **"Bukan Anda? Keluar"** menghapus ingatan di HP tersebut.

### Ubah
- Auto-logout kiosk 30 detik dihapus; layar selesai FTW / aktivitas kembali ke Menu.
- Karyawan yang dinonaktifkan atau dipindah ke mitra otomatis keluar dalam ≤ 60 detik.

### Perbaikan
- Kunci Data Protection disimpan di volume `fittowork-keys`, sehingga deploy ulang tidak lagi membuat semua pengguna ter-logout.

### Deploy
- Tanpa migrasi. Deploy pertama fitur ini membuat semua sesi lama logout sekali.

---

## 2026-10-02 — Import Pemeriksaan Kesehatan dari Excel

### Baru
- Import hasil pemeriksaan kesehatan dari Excel dengan **pratinjau**; skor & kategori risiko dihitung otomatis.
- Template import dibuat otomatis dari Parameter & Skor Pemkes (dropdown pilihan, catatan per kolom, petunjuk).

### Teknis
- Simpan pemkes memakai `InsertAsync` bersama; tambah `EmployeeRepository.GetByNIPsAsync`.

### Deploy
- Tanpa migrasi.

---

## 2026-10-01 — Import Data Karyawan dengan Pratinjau

### Baru
- Pratinjau sebelum import data karyawan disimpan.
- Jabatan / Division / Department yang belum ada bisa **ditambahkan**, **diperbaiki** ke yang sudah ada (usulan otomatis paling mirip), **diaktifkan lagi**, atau **dikosongkan**.

### Ubah
- Import disimpan dalam 1 transaksi; NIP dobel di file ditandai gagal; spasi ganda di nama organisasi diabaikan.

### Deploy
- Tanpa migrasi.

---

## 2026-10-01 — Pengaturan Pengisian FTW

### Baru
- Menu **Fit To Work › Pengaturan Pengisian**: jeda isi ulang (default 12 jam) atau jam reset tetap per shift, saklar tombol Isi Ulang, lama layar "sudah mengisi", pratinjau.

### Ubah
- Halaman "Sudah mengisi" menampilkan kapan pengisian berikutnya dibuka.

### Perbaikan
- Batas "sudah mengisi hari ini" tidak lagi memakai tanggal UTC (dulu berganti pukul 08:00 WITA).

### Deploy
- Migrasi: `migration_ftw_settings.sql`.
- Hak akses baru: `ftw.settings` (Pengaturan Pengisian).

---

## 2026-09-30 — Kunci Lokasi, Gym Check-out, Mitra Kerja, User Admin

### Baru
- Master **Lokasi Site** dengan peta (satelit / peta jalan), radius, dan IP Wi-Fi site.
- **Kunci lokasi** pengisian Fit To Work dan Gym (GPS dalam radius atau Wi-Fi site); jejak lokasi di riwayat & Excel.
- **Gym check-in / check-out**, durasi, auto "Tidak check-out" setelah 3 jam, beberapa sesi per hari.
- **Mitra Kerja**: Data Mitra dan Karyawan Mitra (ID = kode mitra + NIP, mis. `GPE-3450`).
- **Edit User Admin** (nama lengkap + tautan karyawan AKP) dan status **Aktif / Nonaktif** akun admin.

### Ubah
- Riwayat Gym, Dashboard Gym, Riwayat Saya, dan detail karyawan menampilkan check-in/out & durasi. Menu karyawan "Daftar Gym" → "Gym".
- Reset Data: modul Data Karyawan AKP / Karyawan Mitra / Data Mitra.

### Perbaikan
- `Permissions-Policy` mengizinkan geolocation untuk situs sendiri.
- Cookie auth & antiforgery memakai `SecurePolicy.SameAsRequest` (sebelumnya error "not an SSL request" di belakang Cloudflare).

### Deploy
- Migrasi (urut): `migration_mitra.sql` → `migration_admin_status.sql` → `migration_site_location.sql` → `migration_gym_checkout.sql`.
- Hak akses baru: `master.locations` (Lokasi Site).

---

## Urutan migrasi lengkap (production)

Semua skrip di folder `Database/`, idempotent, dijalankan berurutan:

1. `migration_gym.sql`
2. `migration_rbac.sql`
3. `migration_accesslog.sql`
4. `migration_medical.sql`
5. `migration_master_klinik.sql`
6. `migration_pemkes_update.sql`
7. `migration_pemkes_parameter.sql`
8. `migration_mitra.sql`
9. `migration_admin_status.sql`
10. `migration_site_location.sql`
11. `migration_gym_checkout.sql`
12. `migration_ftw_settings.sql`
13. `migration_fitness_activity.sql`
14. `migration_portal_password.sql`

`schema.sql` = gabungan lengkap untuk instalasi baru.
