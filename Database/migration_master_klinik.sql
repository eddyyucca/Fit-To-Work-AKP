-- ============================================================
-- Master Kategori Penyakit & Satuan Obat (dipakai sebagai dropdown
-- di Master Jenis Penyakit dan Master Obat).
-- Diseases.Kategori & Medicines.Satuan tetap berupa teks; mengganti
-- nama di master otomatis ikut mengganti data yang memakainya.
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_medical.sql.
-- ============================================================

IF OBJECT_ID('dbo.DiseaseCategories', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DiseaseCategories
    (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        Name      NVARCHAR(100) NOT NULL CONSTRAINT UQ_DiseaseCategories_Name UNIQUE,
        SortOrder INT NOT NULL DEFAULT(0),
        IsActive  BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

IF OBJECT_ID('dbo.MedicineUnits', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MedicineUnits
    (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        Name      NVARCHAR(30) NOT NULL CONSTRAINT UQ_MedicineUnits_Name UNIQUE,
        SortOrder INT NOT NULL DEFAULT(0),
        IsActive  BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

-- ---------- Isi awal kategori ----------
INSERT INTO dbo.DiseaseCategories (Name, SortOrder)
SELECT v.Name, v.SortOrder
FROM (VALUES
    (N'Pernapasan', 10), (N'Pencernaan', 20), (N'Kardiovaskular', 30), (N'Saraf', 40),
    (N'Muskuloskeletal', 50), (N'Kulit', 60), (N'Mata', 70), (N'THT', 80),
    (N'Gigi & Mulut', 90), (N'Metabolik', 100), (N'Cedera', 110), (N'Umum', 120), (N'Lainnya', 999)
) v(Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.DiseaseCategories c WHERE c.Name = v.Name);

-- Kategori yang sudah terlanjur dipakai di data penyakit ikut dimasukkan
INSERT INTO dbo.DiseaseCategories (Name, SortOrder)
SELECT DISTINCT LTRIM(RTRIM(d.Kategori)), 500
FROM dbo.Diseases d
WHERE d.Kategori IS NOT NULL AND LTRIM(RTRIM(d.Kategori)) <> ''
  AND NOT EXISTS (SELECT 1 FROM dbo.DiseaseCategories c WHERE c.Name = LTRIM(RTRIM(d.Kategori)));
GO

-- ---------- Isi awal satuan ----------
INSERT INTO dbo.MedicineUnits (Name, SortOrder)
SELECT v.Name, v.SortOrder
FROM (VALUES
    (N'tablet', 10), (N'kapsul', 20), (N'kaplet', 30), (N'botol', 40), (N'tube', 50),
    (N'sachet', 60), (N'strip', 70), (N'ampul', 80), (N'pcs', 90), (N'ml', 100)
) v(Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MedicineUnits u WHERE u.Name = v.Name);

INSERT INTO dbo.MedicineUnits (Name, SortOrder)
SELECT DISTINCT LTRIM(RTRIM(m.Satuan)), 500
FROM dbo.Medicines m
WHERE LTRIM(RTRIM(m.Satuan)) <> ''
  AND NOT EXISTS (SELECT 1 FROM dbo.MedicineUnits u WHERE u.Name = LTRIM(RTRIM(m.Satuan)));
GO
