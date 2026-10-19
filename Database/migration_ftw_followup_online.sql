-- ============================================================
-- Tindak lanjut FTW: FOLLOW UP ONLINE (hubungi lewat WhatsApp / telepon), tanpa kunjungan ke klinik.
--
-- Di Laporan Fit To Work > Tindak Lanjut, tiap karyawan tidak fit punya dua pilihan:
--   - Kunjungan Langsung : datang ke klinik -> Pemeriksaan Awal (perawat) -> dokter. Setelah difinalkan dokter, tercatat sebagai
--                          KUNJUNGAN KLINIK di laporan klinik.
--   - Follow Up Online   : hanya dihubungi (kanal, hasil, catatan, kasus SELESAI atau BELUM + jadwal follow up berikutnya). Disimpan di tabel ini dan TIDAK PERNAH menjadi kunjungan klinik,
--                          jadi tidak masuk dashboard / laporan kunjungan klinik.
--
-- Tabel BARU. Tidak mengubah tabel yang ada. Satu kejadian tidak fit boleh punya beberapa follow up (riwayat); yang terbaru jadi statusnya.
-- Kasus yang BELUM selesai tetap terbuka di Tindak Lanjut dan ditandai terlambat kalau jadwal follow up berikutnya terlewat.
-- Hapus pengisian FTW -> follow up-nya ikut terhapus (CASCADE).
-- Aman dijalankan berkali-kali. BACKUP database dulu. Jalankan SEBELUM menjalankan aplikasi versi baru
-- (tanpa tabel ini, halaman Tindak Lanjut tetap terbuka, hanya tanpa tombol Follow Up Online).
-- ============================================================

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.FtwFollowUps', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FtwFollowUps
    (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        FtwSubmissionId INT NOT NULL CONSTRAINT FK_FtwFollowUps_Submissions REFERENCES dbo.FitToWorkSubmissions(Id) ON DELETE CASCADE,
        Kanal           NVARCHAR(30) NOT NULL,            -- WhatsApp | Telepon | Pesan | Lainnya
        Hasil           NVARCHAR(40) NOT NULL,            -- Membaik | MasihSakit | PerluKlinik | TidakDapatDihubungi | IstirahatDiRumah | Lainnya
        Catatan         NVARCHAR(1000) NULL,
        FollowUpAt      DATETIME2 NOT NULL,               -- waktu menghubungi (UTC)
        IsClosed        BIT NOT NULL CONSTRAINT DF_FtwFollowUps_IsClosed DEFAULT(0),   -- 1 = masalah sudah selesai (kasus ditutup)
        NextFollowUpDate DATE NULL,                -- jadwal follow up berikutnya (untuk kasus yang belum selesai)
        ByAdminId       INT NULL,
        ByName          NVARCHAR(150) NOT NULL,
        CreatedAt       DATETIME2 NOT NULL CONSTRAINT DF_FtwFollowUps_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT CK_FtwFollowUps_Kanal CHECK (Kanal IN ('WhatsApp', 'Telepon', 'Pesan', 'Lainnya')),
        CONSTRAINT CK_FtwFollowUps_Hasil CHECK (Hasil IN ('Membaik', 'MasihSakit', 'PerluKlinik', 'TidakDapatDihubungi', 'IstirahatDiRumah', 'Lainnya'))
    );

    CREATE INDEX IX_FtwFollowUps_Submission ON dbo.FtwFollowUps(FtwSubmissionId, FollowUpAt DESC);
END
GO

-- Kasus selesai / belum + jadwal follow up berikutnya. Untuk tabel yang sudah ada. Idempotent.
IF OBJECT_ID('dbo.FtwFollowUps', 'U') IS NOT NULL AND COL_LENGTH('dbo.FtwFollowUps', 'IsClosed') IS NULL
    ALTER TABLE dbo.FtwFollowUps ADD IsClosed BIT NOT NULL CONSTRAINT DF_FtwFollowUps_IsClosed DEFAULT(0);
GO

IF OBJECT_ID('dbo.FtwFollowUps', 'U') IS NOT NULL AND COL_LENGTH('dbo.FtwFollowUps', 'NextFollowUpDate') IS NULL
    ALTER TABLE dbo.FtwFollowUps ADD NextFollowUpDate DATE NULL;
GO

PRINT 'Tabel dbo.FtwFollowUps siap.';
GO
