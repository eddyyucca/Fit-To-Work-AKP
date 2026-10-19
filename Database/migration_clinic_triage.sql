-- ============================================================
-- Pemeriksaan Awal (triage) perawat -> dilanjutkan dokter.
--
-- Alur: perawat mencatat pemeriksaan awal (keluhan + tanda vital) sebagai DRAFT. Dokter membuka draft itu,
-- melanjutkan pemeriksaan detail (diagnosa ICD-10, obat, rekomendasi) di form Kunjungan Klinik dan memfinalkannya.
-- Saat difinalkan, kunjungan klinik dibuat seperti biasa (stok obat berkurang) dan draft ditandai Selesai.
--
-- Tabel BARU. Tidak mengubah tabel ClinicVisits / data yang ada, jadi dashboard, laporan, dan stok obat tidak terpengaruh
-- (draft BUKAN kunjungan klinik sampai difinalkan dokter).
--
-- Relasi: hapus karyawan -> draft-nya ikut terhapus (CASCADE); hapus kunjungan -> VisitId di draft menjadi NULL.
-- Aman dijalankan berkali-kali. BACKUP database dulu. Jalankan SEBELUM menjalankan aplikasi versi baru.
-- ============================================================

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.ClinicTriages', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClinicTriages
    (
        Id                 INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeId         INT NOT NULL CONSTRAINT FK_ClinicTriages_Employees REFERENCES dbo.Employees(Id) ON DELETE CASCADE,
        FtwSubmissionId    INT NULL CONSTRAINT FK_ClinicTriages_FtwSubmissions REFERENCES dbo.FitToWorkSubmissions(Id) ON DELETE SET NULL,   -- pengisian FTW tidak fit yang menjadi asal pemeriksaan awal (opsional)
        NamaSnapshot       NVARCHAR(150) NOT NULL,
        DepartmentSnapshot NVARCHAR(150) NOT NULL CONSTRAINT DF_ClinicTriages_Dept DEFAULT(''),
        SectionSnapshot    NVARCHAR(150) NOT NULL CONSTRAINT DF_ClinicTriages_Section DEFAULT(''),
        PositionSnapshot   NVARCHAR(150) NOT NULL CONSTRAINT DF_ClinicTriages_Position DEFAULT(''),
        TriageAt           DATETIME2 NOT NULL,            -- waktu pemeriksaan awal (UTC)
        Keluhan            NVARCHAR(1000) NOT NULL,
        Sistolik           INT NULL,                      -- mmHg
        Diastolik          INT NULL,                      -- mmHg
        Nadi               INT NULL,                      -- x/menit
        Suhu               DECIMAL(4,1) NULL,             -- derajat Celsius
        Napas              INT NULL,                      -- x/menit
        Spo2               INT NULL,                      -- persen
        BeratBadan         DECIMAL(5,1) NULL,             -- kg
        CatatanPerawat     NVARCHAR(1000) NULL,
        Status             NVARCHAR(20) NOT NULL CONSTRAINT DF_ClinicTriages_Status DEFAULT('Draft'),   -- Draft | Selesai | Batal
        NurseAdminId       INT NULL,
        NurseName          NVARCHAR(150) NOT NULL,
        VisitId            INT NULL CONSTRAINT FK_ClinicTriages_ClinicVisits REFERENCES dbo.ClinicVisits(Id) ON DELETE SET NULL,
        FinalizedAt        DATETIME2 NULL,                -- saat dokter memfinalkan kunjungan (UTC)
        CancelReason       NVARCHAR(250) NULL,
        CancelledAt        DATETIME2 NULL,
        CancelledBy        NVARCHAR(150) NULL,
        CreatedAt          DATETIME2 NOT NULL CONSTRAINT DF_ClinicTriages_CreatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedAt          DATETIME2 NULL,
        CONSTRAINT CK_ClinicTriages_Status CHECK (Status IN ('Draft', 'Selesai', 'Batal'))
    );

    CREATE INDEX IX_ClinicTriages_Status_TriageAt ON dbo.ClinicTriages(Status, TriageAt);
    CREATE INDEX IX_ClinicTriages_EmployeeId ON dbo.ClinicTriages(EmployeeId, TriageAt);
    CREATE INDEX IX_ClinicTriages_VisitId ON dbo.ClinicTriages(VisitId) WHERE VisitId IS NOT NULL;
END
GO

-- Tautan ke pengisian FTW (untuk tombol "Pemeriksaan Awal" di Laporan FTW > Tindak Lanjut). Untuk tabel yang sudah ada. Idempotent.
IF OBJECT_ID('dbo.ClinicTriages', 'U') IS NOT NULL AND COL_LENGTH('dbo.ClinicTriages', 'FtwSubmissionId') IS NULL
    ALTER TABLE dbo.ClinicTriages ADD FtwSubmissionId INT NULL CONSTRAINT FK_ClinicTriages_FtwSubmissions REFERENCES dbo.FitToWorkSubmissions(Id) ON DELETE SET NULL;
GO

IF OBJECT_ID('dbo.ClinicTriages', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClinicTriages_FtwSubmissionId' AND object_id = OBJECT_ID('dbo.ClinicTriages'))
    CREATE INDEX IX_ClinicTriages_FtwSubmissionId ON dbo.ClinicTriages(FtwSubmissionId) WHERE FtwSubmissionId IS NOT NULL;
GO

PRINT 'Tabel dbo.ClinicTriages siap.';
GO
