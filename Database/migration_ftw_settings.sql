-- ============================================================
-- Pengaturan aplikasi yang bisa diubah admin (key-value)
--   - dbo.AppSettings : dipakai pertama kali oleh menu Fit To Work > Pengaturan Pengisian (key "Ftw.*"):
--       Ftw.ResetMode    : 'Jeda' (N menit sejak pengisian terakhir) | 'JamTetap' (jam reset tertentu)
--       Ftw.JedaMenit    : jeda dalam menit (default 720 = 12 jam)
--       Ftw.JamReset     : daftar jam reset, mis. '06:00,18:00'
--       Ftw.IzinIsiUlang : '1' = tombol "Isi Ulang" tampil, '0' = tidak
--       Ftw.DetikKembali : lama halaman "sudah mengisi" sebelum kembali ke layar NIP
--     Key yang belum ada = aplikasi memakai nilai default (jeda 12 jam, isi ulang boleh, 8 detik).
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_gym_checkout.sql.
-- ============================================================

IF OBJECT_ID('dbo.AppSettings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppSettings
    (
        [Key]     NVARCHAR(100)  NOT NULL PRIMARY KEY,
        Value     NVARCHAR(1000) NOT NULL,
        UpdatedAt DATETIME2      NOT NULL CONSTRAINT DF_AppSettings_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedBy NVARCHAR(150)  NULL
    );
END
GO
