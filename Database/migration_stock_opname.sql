-- ============================================================
-- Stok Opname Obat per PERIODE, bisa PARSIAL (dihitung bertahap), dengan tanggal terakhir pengisian.
--
-- Alur: buat opname untuk satu periode (mis. Oktober 2026) -> pilih semua obat atau sebagian -> isi stok fisik bertahap
-- (disimpan kapan saja; tiap obat mencatat tanggal & pengisi terakhir) -> terapkan selisih ke stok per obat
-- (tercatat di kartu stok sebagai "Penyesuaian / opname") -> selesaikan opname.
--
-- Tabel BARU. Tidak mengubah tabel Medicines / data yang ada. Stok baru berubah hanya saat admin menekan "Terapkan".
-- Hapus obat -> baris opname obat itu ikut terhapus (CASCADE), supaya fitur "Hapus data master obat" tetap jalan.
-- Aman dijalankan berkali-kali. BACKUP database dulu. Jalankan SEBELUM menjalankan aplikasi versi baru
-- (halaman Obat & Stok tetap berfungsi tanpa tabel ini, hanya tanpa kolom "Opname terakhir").
-- ============================================================

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.StockOpnames', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockOpnames
    (
        Id             INT IDENTITY(1,1) PRIMARY KEY,
        PeriodeLabel   NVARCHAR(100) NOT NULL,            -- mis. "Oktober 2026" / "Triwulan IV 2026"
        PeriodeMulai   DATE NOT NULL,
        PeriodeSelesai DATE NOT NULL,
        Status         NVARCHAR(20) NOT NULL CONSTRAINT DF_StockOpnames_Status DEFAULT('Berjalan'),   -- Berjalan | Selesai | Batal
        Catatan        NVARCHAR(500) NULL,
        CreatedAt      DATETIME2 NOT NULL CONSTRAINT DF_StockOpnames_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedBy      NVARCHAR(150) NOT NULL,
        LastFilledAt   DATETIME2 NULL,                    -- terakhir ada hitungan yang diisi / diubah (UTC)
        FinishedAt     DATETIME2 NULL,
        FinishedBy     NVARCHAR(150) NULL,
        CONSTRAINT CK_StockOpnames_Status CHECK (Status IN ('Berjalan', 'Selesai', 'Batal')),
        CONSTRAINT CK_StockOpnames_Periode CHECK (PeriodeSelesai >= PeriodeMulai)
    );

    -- Satu opname aktif per periode (diisi parsial berkali-kali di opname yang sama, bukan membuat dobel)
    CREATE UNIQUE INDEX UX_StockOpnames_Periode ON dbo.StockOpnames(PeriodeMulai, PeriodeSelesai) WHERE Status <> 'Batal';
END
GO

IF OBJECT_ID('dbo.StockOpnameItems', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockOpnameItems
    (
        Id             INT IDENTITY(1,1) PRIMARY KEY,
        OpnameId       INT NOT NULL CONSTRAINT FK_StockOpnameItems_Opname REFERENCES dbo.StockOpnames(Id) ON DELETE CASCADE,
        MedicineId     INT NOT NULL CONSTRAINT FK_StockOpnameItems_Medicine REFERENCES dbo.Medicines(Id) ON DELETE CASCADE,
        NamaSnapshot   NVARCHAR(150) NOT NULL,
        SatuanSnapshot NVARCHAR(30) NOT NULL,
        StokSistem     INT NULL,                          -- stok di sistem saat hitungan disimpan
        StokFisik      INT NULL,                          -- NULL = belum dihitung
        CountedAt      DATETIME2 NULL,                    -- tanggal pengisian hitungan (UTC)
        CountedBy      NVARCHAR(150) NULL,
        Catatan        NVARCHAR(250) NULL,
        AppliedAt      DATETIME2 NULL,                    -- selisih sudah diterapkan ke stok (UTC)
        AppliedDelta   INT NULL,
        AppliedBy      NVARCHAR(150) NULL,
        CONSTRAINT CK_StockOpnameItems_Fisik CHECK (StokFisik IS NULL OR StokFisik >= 0),
        CONSTRAINT UQ_StockOpnameItems UNIQUE (OpnameId, MedicineId)
    );

    CREATE INDEX IX_StockOpnameItems_Medicine ON dbo.StockOpnameItems(MedicineId, CountedAt);
END
GO

PRINT 'Tabel dbo.StockOpnames dan dbo.StockOpnameItems siap.';
GO
