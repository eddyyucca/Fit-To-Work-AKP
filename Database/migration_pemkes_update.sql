-- ============================================================
-- Update modul Klinik & Kesehatan (Sept 2026):
--   1. Temuan & follow up pemeriksaan kesehatan (pemkes)
--   2. Lama istirahat kunjungan klinik pakai tanggal (dari - sampai)
--   3. Lampiran / upload hasil pemeriksaan (pemkes & tes urin/alkohol)
--   4. Tes Urin & Alkohol + master parameter tes urin
--   5. Acuan nilai pemkes yang bisa diubah dari menu admin
--   6. Hak akses baru "medical.drugtest" (otomatis diberikan ke role Medic)
-- Semua waktu disimpan UTC. Aman dijalankan berkali-kali (idempotent).
-- Jalankan SETELAH migration_medical.sql & migration_master_klinik.sql.
-- ============================================================

-- ---------- 1. Follow up pemeriksaan kesehatan ----------
IF COL_LENGTH('dbo.HealthCheckups', 'AdaTemuan') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD AdaTemuan BIT NOT NULL CONSTRAINT DF_HealthCheckups_AdaTemuan DEFAULT(0);
GO
IF COL_LENGTH('dbo.HealthCheckups', 'TemuanKeterangan') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD TemuanKeterangan NVARCHAR(1000) NULL;
GO
IF COL_LENGTH('dbo.HealthCheckups', 'FollowUpDate') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD FollowUpDate DATE NULL;          -- tanggal lokal site
GO
IF COL_LENGTH('dbo.HealthCheckups', 'FollowUpDoneAt') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD FollowUpDoneAt DATETIME2 NULL;   -- UTC, NULL = belum selesai
GO
IF COL_LENGTH('dbo.HealthCheckups', 'FollowUpDoneBy') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD FollowUpDoneBy NVARCHAR(150) NULL;
GO
IF COL_LENGTH('dbo.HealthCheckups', 'FollowUpHasil') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD FollowUpHasil NVARCHAR(1000) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HealthCheckups_FollowUp' AND object_id = OBJECT_ID('dbo.HealthCheckups'))
    CREATE INDEX IX_HealthCheckups_FollowUp ON dbo.HealthCheckups(AdaTemuan, FollowUpDoneAt, FollowUpDate);
GO

-- ---------- 2. Istirahat pakai tanggal ----------
-- HariIstirahat tetap diisi (dihitung dari tanggal) supaya dashboard & export lama tetap jalan
IF COL_LENGTH('dbo.ClinicVisits', 'IstirahatDari') IS NULL
    ALTER TABLE dbo.ClinicVisits ADD IstirahatDari DATE NULL;
GO
IF COL_LENGTH('dbo.ClinicVisits', 'IstirahatSampai') IS NULL
    ALTER TABLE dbo.ClinicVisits ADD IstirahatSampai DATE NULL;
GO

-- ---------- 3. Lampiran (file disimpan di disk, tabel ini hanya catatan) ----------
IF OBJECT_ID('dbo.MedicalAttachments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MedicalAttachments
    (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        EntityType  NVARCHAR(20)  NOT NULL,      -- Checkup, DrugTest
        EntityId    INT           NOT NULL,
        FileName    NVARCHAR(255) NOT NULL,      -- nama file asli (untuk ditampilkan / download)
        StoredPath  NVARCHAR(400) NOT NULL,      -- path relatif di folder upload, nama acak (GUID)
        ContentType NVARCHAR(100) NOT NULL,
        SizeBytes   BIGINT        NOT NULL,
        UploadedAt  DATETIME2     NOT NULL DEFAULT(SYSUTCDATETIME()),
        UploadedBy  NVARCHAR(150) NULL
    );
    CREATE INDEX IX_MedicalAttachments_Entity ON dbo.MedicalAttachments(EntityType, EntityId);
END
GO

-- ---------- 4. Tes Urin & Alkohol ----------
IF OBJECT_ID('dbo.UrineTestParameters', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UrineTestParameters
    (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        Name      NVARCHAR(100) NOT NULL CONSTRAINT UQ_UrineTestParameters_Name UNIQUE,
        SortOrder INT NOT NULL DEFAULT(0),
        IsActive  BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );

    -- Panel narkoba 6 parameter yang umum dipakai (boleh diubah / ditambah di menu Parameter Tes Urin)
    INSERT INTO dbo.UrineTestParameters (Name, SortOrder) VALUES
    (N'Amphetamine (AMP)', 10),
    (N'Methamphetamine (MET)', 20),
    (N'Marijuana (THC)', 30),
    (N'Cocaine (COC)', 40),
    (N'Morphine / Opiat (MOP)', 50),
    (N'Benzodiazepine (BZO)', 60);
END
GO

IF OBJECT_ID('dbo.DrugAlcoholTests', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DrugAlcoholTests
    (
        Id                 INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeId         INT NOT NULL REFERENCES dbo.Employees(Id),
        NamaSnapshot       NVARCHAR(150) NOT NULL,
        DepartmentSnapshot NVARCHAR(150) NOT NULL DEFAULT(''),
        SectionSnapshot    NVARCHAR(150) NOT NULL DEFAULT(''),
        PositionSnapshot   NVARCHAR(150) NOT NULL DEFAULT(''),
        TestedAt           DATETIME2 NOT NULL,        -- UTC
        Alasan             NVARCHAR(30) NOT NULL,     -- Rutin, Acak, PraKerja, PascaInsiden, Kecurigaan, Lainnya
        TesUrin            BIT NOT NULL DEFAULT(0),
        UrinHasil          NVARCHAR(10) NULL,         -- Negatif / Positif (gabungan semua parameter)
        TesAlkohol         BIT NOT NULL DEFAULT(0),
        AlkoholKadar       DECIMAL(8,3) NULL,         -- angka dari alat (satuan mengikuti alat)
        AlkoholBatas       DECIMAL(8,3) NULL,         -- snapshot batas positif saat tes
        AlkoholHasil       NVARCHAR(10) NULL,         -- Negatif / Positif
        Catatan            NVARCHAR(1000) NULL,
        ExaminerAdminId    INT NULL,
        ExaminerName       NVARCHAR(150) NOT NULL,
        CreatedAt          DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_DrugAlcoholTests_TestedAt ON dbo.DrugAlcoholTests(TestedAt);
    CREATE INDEX IX_DrugAlcoholTests_EmployeeId_TestedAt ON dbo.DrugAlcoholTests(EmployeeId, TestedAt);
END
GO

IF OBJECT_ID('dbo.DrugAlcoholTestUrineItems', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DrugAlcoholTestUrineItems
    (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        TestId        INT NOT NULL REFERENCES dbo.DrugAlcoholTests(Id) ON DELETE CASCADE,
        ParameterName NVARCHAR(100) NOT NULL,   -- snapshot nama parameter saat tes
        Hasil         NVARCHAR(10)  NOT NULL    -- Negatif / Positif
    );
    CREATE INDEX IX_DrugAlcoholTestUrineItems_Test ON dbo.DrugAlcoholTestUrineItems(TestId);
END
GO

-- ---------- 5. Acuan nilai pemkes (yang tidak ada di tabel = pakai nilai default di aplikasi) ----------
IF OBJECT_ID('dbo.HealthReferenceValues', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HealthReferenceValues
    (
        [Key]     NVARCHAR(50)   NOT NULL PRIMARY KEY,
        Value     DECIMAL(10,3)  NOT NULL,
        UpdatedAt DATETIME2      NOT NULL DEFAULT(SYSUTCDATETIME()),
        UpdatedBy NVARCHAR(150)  NULL
    );
END
GO

-- ---------- 6. Hak akses Tes Urin & Alkohol untuk role Medic ----------
IF OBJECT_ID('dbo.RolePermissions', 'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.RolePermissions (RoleId, Permission)
    SELECT r.Id, 'medical.drugtest'
    FROM dbo.Roles r
    WHERE r.Name = 'Medic'
      AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = r.Id AND rp.Permission = 'medical.drugtest');
END
GO
