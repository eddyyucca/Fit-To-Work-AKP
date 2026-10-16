-- ============================================================
-- Aktivitas Kebugaran (dulu: Gym) - gym menjadi salah satu jenis aktivitas
--   - dbo.ActivityTypes : master jenis aktivitas, diatur admin (menu Aktivitas Kebugaran > Jenis Aktivitas)
--       PakaiGps          : 1 = jarak direkam lewat GPS HP (lari, jalan cepat, hiking, sepeda)
--       WajibBerat        : 1 = berat badan wajib diisi saat mulai (gym)
--       MaksJam           : sesi yang tidak diselesaikan dalam N jam ditutup otomatis "Tidak diselesaikan"
--       KunciLokasi       : 'MulaiSelesai' = mulai & selesai wajib di area site (gym)
--                           'Mulai'        = hanya mulai di area site, selesai boleh di mana saja (lari, hiking)
--                           'Tidak'        = tanpa kunci lokasi
--       MaksKecepatanKmh  : titik GPS yang "melompat" lebih cepat dari ini dibuang (anti lonjakan GPS)
--   - dbo.GymAttendances (tabel lama dipakai terus, 1 baris = 1 sesi aktivitas) + kolom:
--       ActivityTypeId, AktivitasNama, AktivitasIkon, PakaiGps, KunciLokasi : snapshot jenis aktivitas saat mulai
--       BatasAt           : batas waktu sesi (UTC) = mulai + MaksJam. Lewat batas & belum selesai = "Tidak diselesaikan"
--       JarakTempuhMeter  : jarak tempuh hasil GPS (dihitung ulang di server)
--       RutePolyline      : rute ringkas (encoded polyline) untuk peta
--       GpsTitik          : jumlah titik GPS yang dipakai
--       GpsCelahDetik     : total jeda GPS (mis. layar HP terkunci)
--   - BeratBadan jadi boleh kosong (hanya wajib untuk jenis aktivitas dengan WajibBerat = 1)
--   - Data lama otomatis menjadi jenis "Gym".
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_ftw_settings.sql.
-- File ini UTF-8 dengan BOM (berisi emoji ikon).
-- ============================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.ActivityTypes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ActivityTypes
    (
        Id               INT IDENTITY(1,1) PRIMARY KEY,
        Kode             VARCHAR(20)   NOT NULL CONSTRAINT UX_ActivityTypes_Kode UNIQUE,
        Nama             NVARCHAR(60)  NOT NULL,
        Ikon             NVARCHAR(16)  NOT NULL CONSTRAINT DF_ActivityTypes_Ikon DEFAULT(N'🏅'),
        PakaiGps         BIT           NOT NULL CONSTRAINT DF_ActivityTypes_PakaiGps DEFAULT(0),
        WajibBerat       BIT           NOT NULL CONSTRAINT DF_ActivityTypes_WajibBerat DEFAULT(0),
        MaksJam          INT           NOT NULL CONSTRAINT DF_ActivityTypes_MaksJam DEFAULT(3)
                                       CONSTRAINT CK_ActivityTypes_MaksJam CHECK (MaksJam BETWEEN 1 AND 12),
        KunciLokasi      VARCHAR(12)   NOT NULL CONSTRAINT DF_ActivityTypes_KunciLokasi DEFAULT('Mulai')
                                       CONSTRAINT CK_ActivityTypes_KunciLokasi CHECK (KunciLokasi IN ('MulaiSelesai', 'Mulai', 'Tidak')),
        MaksKecepatanKmh DECIMAL(5,1)  NULL CONSTRAINT CK_ActivityTypes_MaksKecepatan CHECK (MaksKecepatanKmh BETWEEN 5 AND 120),
        SortOrder        INT           NOT NULL CONSTRAINT DF_ActivityTypes_SortOrder DEFAULT(0),
        IsActive         BIT           NOT NULL CONSTRAINT DF_ActivityTypes_IsActive DEFAULT(1),
        CreatedAt        DATETIME2     NOT NULL CONSTRAINT DF_ActivityTypes_CreatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedAt        DATETIME2     NULL
    );
END
GO

-- Daftar awal (hanya ditambahkan kalau kodenya belum ada; admin bebas mengubah lewat menu)
MERGE dbo.ActivityTypes AS t
USING (VALUES
    ('GYM',       N'Gym',         N'🏋️', 0, 1, 3, 'MulaiSelesai', NULL,  1),
    ('LARI',      N'Lari',        N'🏃', 1, 0, 4, 'Mulai',        25.0,  2),
    ('JALAN',     N'Jalan Cepat', N'🚶', 1, 0, 4, 'Mulai',        12.0,  3),
    ('HIKING',    N'Hiking',      N'🥾', 1, 0, 8, 'Mulai',        12.0,  4),
    ('SEPEDA',    N'Bersepeda',   N'🚴', 1, 0, 6, 'Mulai',        60.0,  5),
    ('BASKET',    N'Basket',      N'🏀', 0, 0, 3, 'Mulai',        NULL,  6),
    ('SEPAKBOLA', N'Sepak Bola',  N'⚽', 0, 0, 3, 'Mulai',        NULL,  7),
    ('BADMINTON', N'Badminton',   N'🏸', 0, 0, 3, 'Mulai',        NULL,  8)
) AS s (Kode, Nama, Ikon, PakaiGps, WajibBerat, MaksJam, KunciLokasi, MaksKecepatanKmh, SortOrder)
ON t.Kode = s.Kode
WHEN NOT MATCHED THEN
    INSERT (Kode, Nama, Ikon, PakaiGps, WajibBerat, MaksJam, KunciLokasi, MaksKecepatanKmh, SortOrder)
    VALUES (s.Kode, s.Nama, s.Ikon, s.PakaiGps, s.WajibBerat, s.MaksJam, s.KunciLokasi, s.MaksKecepatanKmh, s.SortOrder);
GO

IF COL_LENGTH('dbo.GymAttendances', 'ActivityTypeId') IS NULL
    ALTER TABLE dbo.GymAttendances ADD
        ActivityTypeId   INT           NULL CONSTRAINT FK_GymAttendances_ActivityTypes REFERENCES dbo.ActivityTypes(Id),
        AktivitasNama    NVARCHAR(60)  NULL,
        AktivitasIkon    NVARCHAR(16)  NULL,
        PakaiGps         BIT           NULL,
        KunciLokasi      VARCHAR(12)   NULL,
        BatasAt          DATETIME2     NULL,
        JarakTempuhMeter INT           NULL,
        RutePolyline     NVARCHAR(MAX) NULL,
        GpsTitik         INT           NULL,
        GpsCelahDetik    INT           NULL;
GO

-- Data lama = Gym, batas sesi 3 jam sejak check-in (sama dengan aturan lama)
UPDATE g
SET ActivityTypeId = t.Id,
    AktivitasNama  = t.Nama,
    AktivitasIkon  = t.Ikon,
    PakaiGps       = 0,
    KunciLokasi    = 'MulaiSelesai'
FROM dbo.GymAttendances g
CROSS JOIN (SELECT Id, Nama, Ikon FROM dbo.ActivityTypes WHERE Kode = 'GYM') t
WHERE g.ActivityTypeId IS NULL;

UPDATE dbo.GymAttendances SET BatasAt = DATEADD(HOUR, 3, CreatedAt) WHERE BatasAt IS NULL;
GO

-- BeratBadan boleh kosong (lari, basket, dst). CHECK lama (tanpa nama) harus dilepas dulu sebelum ALTER COLUMN.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.GymAttendances') AND name = 'BeratBadan' AND is_nullable = 0)
BEGIN
    DECLARE @ck SYSNAME, @sql NVARCHAR(400);
    DECLARE ck_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT cc.name
        FROM sys.check_constraints cc
        INNER JOIN sys.columns c ON c.object_id = cc.parent_object_id AND c.column_id = cc.parent_column_id
        WHERE cc.parent_object_id = OBJECT_ID('dbo.GymAttendances') AND c.name = 'BeratBadan';
    OPEN ck_cursor;
    FETCH NEXT FROM ck_cursor INTO @ck;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'ALTER TABLE dbo.GymAttendances DROP CONSTRAINT ' + QUOTENAME(@ck);
        EXEC sp_executesql @sql;
        FETCH NEXT FROM ck_cursor INTO @ck;
    END
    CLOSE ck_cursor;
    DEALLOCATE ck_cursor;

    ALTER TABLE dbo.GymAttendances ALTER COLUMN BeratBadan DECIMAL(5,1) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_GymAttendances_BeratBadan')
    ALTER TABLE dbo.GymAttendances ADD CONSTRAINT CK_GymAttendances_BeratBadan
        CHECK (BeratBadan IS NULL OR BeratBadan BETWEEN 30 AND 200);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GymAttendances_ActivityTypeId' AND object_id = OBJECT_ID('dbo.GymAttendances'))
    CREATE INDEX IX_GymAttendances_ActivityTypeId ON dbo.GymAttendances(ActivityTypeId, CreatedAt);
GO
