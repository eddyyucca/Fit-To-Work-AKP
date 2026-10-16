# Deploy Fit To Work dengan Docker

Panduan ini menjalankan **aplikasi** dan **SQL Server** sekaligus lewat
`docker compose` — tidak perlu install .NET SDK atau SQL Server manual di server.

## Prasyarat

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Windows/Mac) atau
  Docker Engine + Docker Compose plugin (Linux)
- Minimal RAM 4GB tersisa untuk SQL Server container (SQL Server cukup berat)

## Struktur yang dipakai

```
FitToWorkApp/
├── Dockerfile           # cara build image aplikasi
├── .dockerignore
├── docker-compose.yml   # orkestrasi: app + db + migrasi schema
├── Database/schema.sql  # dijalankan otomatis oleh service "db-init"
└── ... (source code lainnya)
```

## 1. Sesuaikan password SQL Server

Buka `docker-compose.yml`, ganti **semua** kemunculan `YourStrong!Passw0rd`
(ada di 3 tempat: service `db`, `db-init`, dan `web`) dengan password yang
lebih kuat. Password SQL Server wajib memenuhi syarat kompleksitas (huruf besar,
kecil, angka, simbol, minimal 8 karakter).

## 2. Build & jalankan

Dari folder root project (folder yang ada `docker-compose.yml`-nya):

```bash
docker compose up --build -d
```

Yang terjadi secara otomatis:
1. `db` — container SQL Server dijalankan, ditunggu sampai sehat (healthcheck).
2. `db-init` — begitu `db` siap, otomatis jalankan `Database/schema.sql` untuk
   membuat database `FitToWorkDb` beserta semua tabelnya (aman dijalankan
   berkali-kali karena scriptnya pakai `IF NOT EXISTS`).
3. `web` — image aplikasi di-build dari `Dockerfile`, lalu dijalankan setelah
   `db-init` selesai sukses. Saat aplikasi pertama kali start, akun admin
   default (`admin` / `Admin123!`) otomatis dibuat (lihat `Program.cs`).

## 3. Cek statusnya

```bash
docker compose ps
docker compose logs -f web
```

Tunggu sampai log `web` menunjukkan aplikasi sudah listening (`Now listening on: http://0.0.0.0:8080`).

## 4. Akses aplikasi

Buka **http://localhost:8080** (atau ganti `localhost` dengan IP server kalau
deploy di server remote).

- Karyawan: `http://localhost:8080/Account/EmployeeLogin`
- Admin: `http://localhost:8080/Account/AdminLogin` (`admin` / `Admin123!`,
  segera ganti lewat menu **🔒 Ganti Password Saya**)

## Perintah yang sering dipakai

```bash
# Lihat log real-time
docker compose logs -f web
docker compose logs -f db

# Restart aplikasi saja setelah ubah kode
docker compose up --build -d web

# Hentikan semua container (data database TETAP ada di volume)
docker compose stop

# Hentikan & hapus container (data database TETAP ada di volume)
docker compose down

# Hentikan & hapus SEMUANYA termasuk data database (hati-hati!)
docker compose down -v
```

## Koneksi manual ke database (opsional)

Karena port `1433` di-expose ke host, Anda bisa konek pakai SSMS / Azure Data
Studio ke `localhost,1433` dengan user `sa` dan password yang sudah diset di
`docker-compose.yml`.

## Update Aplikasi ke Versi Baru

Alur umumnya: **ganti file source code → build ulang image `web` → restart
container `web`**. Container/data database **tidak perlu disentuh sama
sekali**, kecuali ada perubahan skema tabel.

### 1. Update file source code

Timpa file-file yang berubah (controller, view, model, dll) ke folder project
Anda seperti biasa — sama seperti setiap kali saya kasih code baru di chat ini.

### 2. Cek apakah ada perubahan skema database

Kalau update-nya menyentuh `Database/schema.sql` (misalnya ada tabel/kolom
baru), jalankan ulang file itu ke database Anda. **Aman dijalankan berkali-kali**
karena semua bagian di dalamnya sudah dibungkus `IF NOT EXISTS` / `IF COL_LENGTH(...) IS NOT NULL`
— jadi tidak akan bikin tabel dobel atau menghapus data yang sudah ada.

- **Kalau pakai `docker-compose.yml`** (SQL Server dikelola Docker): jalankan
  ulang service `db-init` saja:
  ```bash
  docker compose up db-init
  ```
- **Kalau pakai `docker-compose.external-db.yml`** (SQL Server existing):
  jalankan manual lewat SSMS/Azure Data Studio, atau lewat sqlcmd sekali pakai:
  ```bash
  docker run --rm -v "$(pwd)/Database:/scripts" mcr.microsoft.com/mssql/server:2022-latest \
    /opt/mssql-tools18/bin/sqlcmd -S YOUR_SQL_SERVER_HOST,1433 -U YOUR_DB_USER -P YOUR_DB_PASSWORD -C -d master -i /scripts/schema.sql
  ```

Kalau perubahannya bukan `CREATE TABLE` tapi `ALTER TABLE` untuk migrasi
kolom lama (seperti contoh drop kolom `PasswordHash` yang pernah saya kasih),
itu juga sudah otomatis ikut ke-cover kalau ada di `schema.sql`, karena
script-nya saya desain idempotent dari awal.

### 3. Build ulang & restart aplikasi

```bash
# Kalau pakai docker-compose.yml (db dikelola Docker)
docker compose up --build -d web

# Kalau pakai docker-compose.external-db.yml (db existing)
docker compose -f docker-compose.external-db.yml up --build -d web
```

Docker akan:
- Build image baru dari `Dockerfile` (layer `dotnet restore` di-skip kalau
  `.csproj` tidak berubah, jadi build lebih cepat kalau cuma ganti kode C#/Razor)
- Stop container `web` yang lama, ganti dengan yang baru
- Container `db` (kalau ada) **tidak ikut di-restart**, tetap jalan terus

Ada jeda beberapa detik saat container lama diganti yang baru (bukan
zero-downtime) — untuk aplikasi internal seperti ini biasanya tidak masalah,
tapi kalau butuh update tanpa downtime sama sekali, itu perlu setup tambahan
(load balancer + rolling deployment) di luar cakupan panduan ini.

### 4. Verifikasi

```bash
docker compose logs -f web
```

Pastikan tidak ada error saat startup, lalu coba akses
`http://localhost:8080` dan cek fitur yang baru diupdate.

### Kalau update ternyata bermasalah (rollback)

- Kalau Anda simpan kode di Git, paling mudah `git checkout` ke commit
  sebelumnya lalu `docker compose up --build -d web` lagi.
- Kalau tidak pakai Git, pastikan simpan salinan folder project versi lama
  sebelum menimpa dengan yang baru, supaya bisa dikembalikan dengan cepat
  kalau perlu.

## Konek ke SQL Server yang sudah ada (existing)

Kalau Anda **sudah punya SQL Server** (server terpisah, VM, atau instance yang
sudah berjalan di jaringan kantor) dan tidak mau Docker bikin SQL Server baru,
pakai `docker-compose.external-db.yml` — ini cuma menjalankan container
aplikasinya saja, tidak ada container database sama sekali.

### 1. Pastikan SQL Server Anda siap menerima koneksi dari luar

- **TCP/IP protocol aktif** di SQL Server Configuration Manager (secara default
  kadang hanya Shared Memory yang aktif).
- **SQL Server Authentication (Mixed Mode)** aktif, dan ada satu login
  SQL (username + password) yang bisa dipakai aplikasi — Windows
  Authentication **tidak bisa** dipakai dari container Linux dengan mudah.
- **Firewall** di server tersebut mengizinkan koneksi masuk ke port `1433`
  dari IP mesin/server tempat Docker Anda jalan.

### 2. Tentukan alamat server di connection string

Ada 3 skenario umum:

| Skenario | Nilai `Server=` di connection string |
|---|---|
| SQL Server ada di **server/VM lain** di jaringan | `Server=192.168.1.50,1433;` (pakai IP/hostname server itu langsung) |
| SQL Server ada di **mesin host yang sama**, di luar Docker | `Server=host.docker.internal,1433;` |
| SQL Server ada di **instance bernama** (bukan default) | `Server=192.168.1.50\SQLEXPRESS;` |

Edit bagian `ConnectionStrings__DefaultConnection` di
`docker-compose.external-db.yml` sesuai skenario Anda.

### 3. Jalankan `schema.sql` sekali ke database tersebut

Karena tidak ada container `db-init` di setup ini, migrasi tabel harus
dijalankan manual (cukup sekali). Pilih salah satu cara:

**Cara A — pakai SSMS / Azure Data Studio**
Buka `Database/schema.sql`, jalankan langsung ke server Anda seperti biasa.

**Cara B — pakai container sqlcmd sekali pakai (tanpa install apa-apa di host)**
```bash
docker run --rm -v "$(pwd)/Database:/scripts" mcr.microsoft.com/mssql/server:2022-latest \
  /opt/mssql-tools18/bin/sqlcmd -S YOUR_SQL_SERVER_HOST,1433 -U YOUR_DB_USER -P YOUR_DB_PASSWORD -C -d master -i /scripts/schema.sql
```
Ganti `YOUR_SQL_SERVER_HOST`, `YOUR_DB_USER`, `YOUR_DB_PASSWORD` sesuai server Anda.

### 4. Build & jalankan aplikasi

```bash
docker compose -f docker-compose.external-db.yml up --build -d
docker compose -f docker-compose.external-db.yml logs -f web
```

Buka **http://localhost:8080** seperti biasa.

### Kalau SQL Server ada di dalam Docker juga, tapi container terpisah (bukan dari compose ini)

Pakai file `docker-compose.docker-db.yml` yang sudah disiapkan. Langkahnya:

**1. Cari tahu nama container & network SQL Server yang sudah ada**

```bash
docker ps
docker inspect <nama_container_sql> --format '{{json .NetworkSettings.Networks}}'
```

**2. Perhatikan nama network-nya**

- Kalau hasilnya nama network **custom** (misal `mynetwork`, bukan `bridge`) →
  lanjut ke langkah 3.
- Kalau ternyata network-nya `bridge` (default Docker) → **container tidak
  bisa saling kenal lewat nama container** di network default ini. Buat
  network baru dan gabungkan SQL Server ke situ dulu:
  ```bash
  docker network create fittowork-net
  docker network connect fittowork-net <nama_container_sql>
  ```
  lalu pakai `fittowork-net` sebagai nama network di langkah berikutnya.

**3. Edit `docker-compose.docker-db.yml`**

Ganti 4 hal:
- `mynetwork` (2 tempat) → nama network dari langkah 1/2
- `mssql-existing` → nama container SQL Server Anda yang sebenarnya
- `YOUR_DB_USER` dan `YOUR_DB_PASSWORD` → kredensial SQL Server Anda
- `Database=FitToWorkDb` → sesuaikan kalau nama database-nya beda

**4. Jalankan `schema.sql` ke database itu (sekali saja)**

```bash
docker run --rm --network <nama_network> -v "$(pwd)/Database:/scripts" mcr.microsoft.com/mssql/server:2022-latest \
  /opt/mssql-tools18/bin/sqlcmd -S <nama_container_sql>,1433 -U YOUR_DB_USER -P YOUR_DB_PASSWORD -C -d master -i /scripts/schema.sql
```

**5. Build & jalankan aplikasinya**

```bash
docker compose -f docker-compose.docker-db.yml up --build -d
docker compose -f docker-compose.docker-db.yml logs -f web
```



- **Reverse proxy & HTTPS**: setup di atas menjalankan aplikasi di HTTP polos
  port 8080. Untuk production, taruh di belakang reverse proxy (Nginx, Caddy,
  Traefik, atau Azure/AWS Load Balancer) yang menangani sertifikat HTTPS,
  lalu proxy ke `http://<server>:8080`.
- Baris `app.UseHttpsRedirection();` di `Program.cs` akan mencatat warning di
  log kalau tidak ada port HTTPS yang dikonfigurasi (tidak bikin error, cuma
  warning) — aman diabaikan kalau HTTPS memang ditangani reverse proxy di
  depannya. Kalau ingin menghilangkan warning-nya, boleh hapus baris tersebut.
- **Backup database**: karena data SQL Server disimpan di Docker volume
  `fittowork-db-data`, pastikan ada strategi backup terpisah (mis. `sqlcmd`
  scheduled backup, atau backup volume-nya) — volume Docker bukan pengganti
  backup.
- **Password & secrets**: jangan commit password SQL Server asli ke Git.
  Untuk production, pertimbangkan pakai `.env` file (di-gitignore) atau
  Docker secrets, lalu reference di `docker-compose.yml` dengan `${...}`.
- **Resource SQL Server**: image `mcr.microsoft.com/mssql/server` butuh
  minimal ~2GB RAM. Kalau server terbatas, pertimbangkan pakai SQL Server
  instance terpisah (bukan di container) dan cukup jalankan `web` saja di
  Docker, dengan connection string mengarah ke server database eksternal.
## Catatan Production

- **Reverse proxy & HTTPS**: setup di atas menjalankan aplikasi di HTTP polos
  port 8080. Untuk production, taruh di belakang reverse proxy (Nginx, Caddy,
  Traefik, atau Azure/AWS Load Balancer) yang menangani sertifikat HTTPS,
  lalu proxy ke `http://<server>:8080`.
- Baris `app.UseHttpsRedirection();` di `Program.cs` akan mencatat warning di
  log kalau tidak ada port HTTPS yang dikonfigurasi (tidak bikin error, cuma
  warning) — aman diabaikan kalau HTTPS memang ditangani reverse proxy di
  depannya. Kalau ingin menghilangkan warning-nya, boleh hapus baris tersebut.
- **Backup database**: kalau pakai SQL Server yang dikelola Docker (bukan
  existing), datanya disimpan di volume `fittowork-db-data` — pastikan ada
  strategi backup terpisah, volume Docker bukan pengganti backup. Kalau pakai
  SQL Server existing, backup mengikuti kebijakan backup server tersebut.
- **Password & secrets**: jangan commit password SQL Server asli ke Git.
  Untuk production, pertimbangkan pakai `.env` file (di-gitignore) atau
  Docker secrets, lalu reference di `docker-compose.yml` dengan `${...}`.
- **Resource SQL Server**: image `mcr.microsoft.com/mssql/server` butuh
  minimal ~2GB RAM. Kalau server terbatas, pertimbangkan pakai
  `docker-compose.external-db.yml` dan konek ke SQL Server yang sudah ada
  di luar Docker.

## Deploy Production (`docker-compose.production.yml`)

Untuk deploy production yang konek ke SQL Server existing (`mssql2022` di
network `mssql_default`), pakai `docker-compose.production.yml` — bedanya
dengan compose biasa:

- **Password & konfigurasi lewat file `.env`**, tidak hardcode di compose file
- **`restart: always`** — otomatis nyala lagi kalau server reboot atau
  container crash
- **Healthcheck** — Docker otomatis cek `http://localhost:8080/Account/EmployeeLogin`
  tiap 30 detik, kalau aplikasi hang/tidak respons, statusnya kelihatan
  "unhealthy" di `docker compose ps`
- **Log rotation** — log dibatasi maks 10MB x 5 file, supaya tidak
  memenuhi disk server dalam jangka panjang
- **Batas resource** — CPU maks 1 core, memory maks 512MB (sesuaikan di
  file compose kalau perlu lebih/kurang)

### Cara pakai

```bash
# 1. Copy contoh .env, lalu isi passwordnya
cp .env.production.example .env
# edit .env, isi DB_PASSWORD dengan password sa yang sebenarnya

# 2. Build & jalankan
docker compose -f docker-compose.production.yml up --build -d

# 3. Cek statusnya (kolom STATUS akan tampil "healthy" kalau lolos healthcheck)
docker compose -f docker-compose.production.yml ps

# 4. Lihat log
docker compose -f docker-compose.production.yml logs -f web
```

### Update ke versi baru (production)

```bash
docker compose -f docker-compose.production.yml up --build -d
```

Perintah yang sama dipakai untuk deploy pertama kali maupun update
berikutnya — Docker otomatis build ulang image kalau ada perubahan kode,
dan restart container `web` saja tanpa mengganggu `mssql2022`.
