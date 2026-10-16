-- ============================================================
-- Kunci lokasi pengisian (Fit To Work & Gym)
--   - dbo.SiteLocations : master titik lokasi site (lat/long + radius meter), diatur admin lewat peta.
--                         IpPublik (opsional) = IP publik internet Wi-Fi site, dipisah koma.
--                         Kalau TIDAK ADA lokasi aktif -> kunci lokasi mati (karyawan bisa isi dari mana saja).
--   - Kolom lokasi di dbo.FitToWorkSubmissions & dbo.GymAttendances untuk jejak audit:
--       LokasiSiteId/LokasiNama : site yang cocok (snapshot nama)
--       GpsLat/GpsLng/GpsAkurasi: posisi HP saat mengisi (akurasi dalam meter)
--       JarakMeter              : jarak ke titik site
--       LokasiVia               : 'GPS' | 'WIFI' | NULL (kunci lokasi belum aktif / data lama)
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_admin_status.sql.
-- ============================================================

IF OBJECT_ID('dbo.SiteLocations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SiteLocations
    (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        Nama        NVARCHAR(100) NOT NULL,
        Latitude    DECIMAL(9,6)  NOT NULL CHECK (Latitude BETWEEN -90 AND 90),
        Longitude   DECIMAL(9,6)  NOT NULL CHECK (Longitude BETWEEN -180 AND 180),
        RadiusMeter INT           NOT NULL CHECK (RadiusMeter BETWEEN 50 AND 20000),
        IpPublik    NVARCHAR(300) NULL,
        Keterangan  NVARCHAR(250) NULL,
        IsActive    BIT           NOT NULL CONSTRAINT DF_SiteLocations_IsActive DEFAULT(1),
        CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_SiteLocations_CreatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedAt   DATETIME2     NULL
    );
END
GO

-- ---------- Fit To Work ----------
IF COL_LENGTH('dbo.FitToWorkSubmissions', 'LokasiSiteId') IS NULL
    ALTER TABLE dbo.FitToWorkSubmissions ADD
        LokasiSiteId INT           NULL,
        LokasiNama   NVARCHAR(100) NULL,
        GpsLat       DECIMAL(9,6)  NULL,
        GpsLng       DECIMAL(9,6)  NULL,
        GpsAkurasi   INT           NULL,
        JarakMeter   INT           NULL,
        LokasiVia    VARCHAR(10)   NULL;
GO

-- ---------- Gym ----------
IF COL_LENGTH('dbo.GymAttendances', 'LokasiSiteId') IS NULL
    ALTER TABLE dbo.GymAttendances ADD
        LokasiSiteId INT           NULL,
        LokasiNama   NVARCHAR(100) NULL,
        GpsLat       DECIMAL(9,6)  NULL,
        GpsLng       DECIMAL(9,6)  NULL,
        GpsAkurasi   INT           NULL,
        JarakMeter   INT           NULL,
        LokasiVia    VARCHAR(10)   NULL;
GO
