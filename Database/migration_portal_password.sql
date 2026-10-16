-- ============================================================
-- Password "Riwayat Saya" (portal karyawan)
--   - Employees.PortalPasswordHash      : hash BCrypt password Riwayat Saya.
--                                         NULL = masih password default (tanggal lahir ddMMyyyy),
--                                         karyawan wajib ganti password setelah login pertama.
--   - Employees.PortalPasswordChangedAt : kapan terakhir diganti (UTC). NULL = belum pernah / sudah di-reset admin.
-- Tidak perlu mengisi data awal: semua karyawan otomatis mulai dari password default.
-- Reset oleh admin = kedua kolom diset NULL lagi.
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_fitness_activity.sql.
-- ============================================================

IF COL_LENGTH('dbo.Employees', 'PortalPasswordHash') IS NULL
    ALTER TABLE dbo.Employees ADD PortalPasswordHash NVARCHAR(100) NULL;
GO

IF COL_LENGTH('dbo.Employees', 'PortalPasswordChangedAt') IS NULL
    ALTER TABLE dbo.Employees ADD PortalPasswordChangedAt DATETIME2 NULL;
GO
