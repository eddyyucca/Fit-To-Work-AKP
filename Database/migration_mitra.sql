-- ============================================================
-- Mitra kerja (kontraktor / subkon) & karyawan mitra
--   - dbo.Mitras               : data perusahaan mitra, Kode = 3 huruf (mis. GPE)
--   - dbo.Employees.MitraId    : karyawan mitra disimpan di tabel karyawan yang sama supaya
--                                kunjungan klinik & rekam medis langsung bisa dipakai.
--                                NIP karyawan mitra = Kode + '-' + NIP mitra (mis. GPE-3450).
-- Karyawan mitra TIDAK bisa login Fit To Work / Daftar Gym / Riwayat Saya, dan tidak dihitung
-- di Data Karyawan maupun dashboard kepatuhan FTW. Diinput oleh tim medic.
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_pemkes_parameter.sql.
-- ============================================================

IF OBJECT_ID('dbo.Mitras', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Mitras
    (
        Id         INT IDENTITY(1,1) PRIMARY KEY,
        Kode       CHAR(3)       NOT NULL CONSTRAINT UQ_Mitras_Kode UNIQUE,   -- 3 huruf kapital, mis. GPE
        Nama       NVARCHAR(150) NOT NULL,
        Keterangan NVARCHAR(250) NULL,
        IsActive   BIT NOT NULL DEFAULT(1),
        CreatedAt  DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

IF COL_LENGTH('dbo.Employees', 'MitraId') IS NULL
    ALTER TABLE dbo.Employees ADD MitraId INT NULL CONSTRAINT FK_Employees_Mitras REFERENCES dbo.Mitras(Id);
GO
IF COL_LENGTH('dbo.Employees', 'MitraNip') IS NULL
    ALTER TABLE dbo.Employees ADD MitraNip NVARCHAR(16) NULL;      -- NIP dari perusahaan mitra (tanpa kode)
GO
IF COL_LENGTH('dbo.Employees', 'MitraJabatan') IS NULL
    ALTER TABLE dbo.Employees ADD MitraJabatan NVARCHAR(100) NULL; -- jabatan / bagian di perusahaan mitra (teks bebas)
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Employees_MitraId' AND object_id = OBJECT_ID('dbo.Employees'))
    CREATE INDEX IX_Employees_MitraId ON dbo.Employees(MitraId);
GO
