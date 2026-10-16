-- ============================================================
-- Pemeriksaan kesehatan (pemkes) dengan PARAMETER CUSTOM + SKOR RISIKO
--   - dbo.CheckupParameters          : daftar isian pemkes (umur, TD sistolik, kolesterol, dll.)
--   - dbo.CheckupParameterCriteria   : kriteria per parameter -> poin & warna penanda
--   - dbo.CheckupRiskLevels          : interpretasi total skor (Risiko Rendah .. Sangat Tinggi)
--   - dbo.HealthCheckupValues        : nilai per parameter untuk setiap pemeriksaan (snapshot)
-- Data pemkes lama (kolom tetap: sistolik, kolesterol, gula, dll.) TIDAK diubah dan tetap bisa dilihat.
-- Semua diatur admin lewat menu Klinik & Kesehatan > Parameter & Skor Pemkes.
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_pemkes_update.sql.
-- ============================================================

-- ---------- Master parameter ----------
IF OBJECT_ID('dbo.CheckupParameters', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CheckupParameters
    (
        Id         INT IDENTITY(1,1) PRIMARY KEY,
        Name       NVARCHAR(100) NOT NULL CONSTRAINT UQ_CheckupParameters_Name UNIQUE,
        Unit       NVARCHAR(30)  NULL,
        InputType  NVARCHAR(10)  NOT NULL,        -- Number, Choice, Age (otomatis dari tgl lahir), Sex (otomatis), Bmi (dari BB & TB)
        Decimals   INT NOT NULL DEFAULT(0),
        MinInput   DECIMAL(10,3) NULL,            -- batas input yang masuk akal (validasi salah ketik)
        MaxInput   DECIMAL(10,3) NULL,
        IsScored   BIT NOT NULL DEFAULT(1),       -- 1 = poinnya dijumlahkan ke total skor
        Hint       NVARCHAR(250) NULL,
        SortOrder  INT NOT NULL DEFAULT(0),
        IsActive   BIT NOT NULL DEFAULT(1),
        CreatedAt  DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

IF OBJECT_ID('dbo.CheckupParameterCriteria', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CheckupParameterCriteria
    (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        ParameterId INT NOT NULL REFERENCES dbo.CheckupParameters(Id) ON DELETE CASCADE,
        Label       NVARCHAR(100) NOT NULL,       -- teks kriteria / pilihan, mis. "120 – 129" atau "Perokok aktif"
        MinValue    DECIMAL(10,3) NULL,           -- angka: batas bawah (termasuk), NULL = tanpa batas bawah
        MaxValue    DECIMAL(10,3) NULL,           -- angka: batas atas (termasuk), NULL = tanpa batas atas
        MatchText   NVARCHAR(10)  NULL,           -- jenis kelamin: L / P
        ForSex      CHAR(1)       NULL,           -- kriteria khusus L / P (NULL = semua)
        Points      INT NOT NULL DEFAULT(0),
        Level       NVARCHAR(20) NOT NULL DEFAULT(''), -- warna: Normal, Perhatian, Risiko, Info, '' (tanpa warna)
        SortOrder   INT NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_CheckupParameterCriteria_Parameter ON dbo.CheckupParameterCriteria(ParameterId, SortOrder);
END
GO

IF OBJECT_ID('dbo.CheckupRiskLevels', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CheckupRiskLevels
    (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        MinScore    INT NOT NULL,
        MaxScore    INT NULL,                     -- NULL = ke atas
        Label       NVARCHAR(50)  NOT NULL,
        Color       NVARCHAR(20)  NOT NULL,       -- success, warning, orange, danger, info, secondary
        Description NVARCHAR(250) NULL,
        SortOrder   INT NOT NULL DEFAULT(0)
    );
END
GO

-- ---------- Nilai per pemeriksaan ----------
IF OBJECT_ID('dbo.HealthCheckupValues', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HealthCheckupValues
    (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        CheckupId     INT NOT NULL REFERENCES dbo.HealthCheckups(Id) ON DELETE CASCADE,
        ParameterId   INT NULL,                   -- sengaja tanpa FK: parameter boleh dihapus, riwayat tetap utuh
        ParameterName NVARCHAR(100) NOT NULL,     -- snapshot saat pemeriksaan
        Unit          NVARCHAR(30)  NULL,
        InputType     NVARCHAR(10)  NOT NULL,
        SortOrder     INT NOT NULL DEFAULT(0),
        NumericValue  DECIMAL(10,3) NULL,
        TextValue     NVARCHAR(100) NULL,
        CriteriaLabel NVARCHAR(100) NULL,
        Points        INT NULL,
        Level         NVARCHAR(20) NOT NULL DEFAULT(''),
        IsScored      BIT NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_HealthCheckupValues_Checkup ON dbo.HealthCheckupValues(CheckupId);
END
GO

IF COL_LENGTH('dbo.HealthCheckups', 'UsesParameters') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD UsesParameters BIT NOT NULL CONSTRAINT DF_HealthCheckups_UsesParameters DEFAULT(0);
GO
IF COL_LENGTH('dbo.HealthCheckups', 'TotalSkor') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD TotalSkor INT NULL;
GO
IF COL_LENGTH('dbo.HealthCheckups', 'SkorParamTerisi') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD SkorParamTerisi INT NULL;
GO
IF COL_LENGTH('dbo.HealthCheckups', 'SkorParamTotal') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD SkorParamTotal INT NULL;
GO
IF COL_LENGTH('dbo.HealthCheckups', 'RisikoLabel') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD RisikoLabel NVARCHAR(50) NULL;
GO
IF COL_LENGTH('dbo.HealthCheckups', 'RisikoColor') IS NULL
    ALTER TABLE dbo.HealthCheckups ADD RisikoColor NVARCHAR(20) NULL;
GO

-- ---------- Isi awal: faktor risiko & kriteria skor ----------
IF NOT EXISTS (SELECT 1 FROM dbo.CheckupParameters)
BEGIN
    INSERT INTO dbo.CheckupParameters (Name, Unit, InputType, Decimals, MinInput, MaxInput, IsScored, Hint, SortOrder) VALUES
    (N'Umur',                    N'tahun',  'Age',    0, NULL, NULL, 1, N'Otomatis dari tanggal lahir karyawan', 10),
    (N'Jenis Kelamin',           NULL,      'Sex',    0, NULL, NULL, 1, N'Otomatis dari data karyawan', 20),
    (N'Tekanan Darah Sistolik',  N'mmHg',   'Number', 0, 50,   300,  1, NULL, 30),
    (N'Kolesterol Total',        N'mg/dL',  'Number', 0, 50,   600,  1, NULL, 40),
    (N'Kolesterol HDL',          N'mg/dL',  'Number', 0, 5,    200,  1, N'Kolesterol baik', 50),
    (N'Kolesterol LDL',          N'mg/dL',  'Number', 0, 10,   500,  1, N'Kolesterol jahat', 60),
    (N'Trigliserida',            N'mg/dL',  'Number', 0, 10,   3000, 1, NULL, 70),
    (N'IMT (Indeks Massa Tubuh)',N'kg/m²',  'Bmi',    1, NULL, NULL, 1, N'Dihitung otomatis dari berat & tinggi badan', 80),
    (N'Diabetes Melitus',        NULL,      'Choice', 0, NULL, NULL, 1, NULL, 90),
    (N'Status Merokok',          NULL,      'Choice', 0, NULL, NULL, 1, NULL, 100),
    (N'Aktivitas Fisik',         N'per minggu', 'Choice', 0, NULL, NULL, 1, N'Total olahraga / aktivitas fisik sedang-berat per minggu', 110);

    INSERT INTO dbo.CheckupParameterCriteria (ParameterId, Label, MinValue, MaxValue, MatchText, Points, Level, SortOrder)
    SELECT p.Id, v.Label, CAST(v.MinV AS DECIMAL(10,3)), CAST(v.MaxV AS DECIMAL(10,3)), v.MatchText, v.Points, v.Lvl, v.Sort
    FROM (VALUES
        (N'Umur', N'< 30',     NULL, 29,   NULL, 0, N'', 10),
        (N'Umur', N'30 – 39',  30,   39,   NULL, 1, N'', 20),
        (N'Umur', N'40 – 49',  40,   49,   NULL, 2, N'', 30),
        (N'Umur', N'50 – 59',  50,   59,   NULL, 3, N'', 40),
        (N'Umur', N'60 – 69',  60,   69,   NULL, 4, N'', 50),
        (N'Umur', N'≥ 70',     70,   NULL, NULL, 5, N'', 60),

        (N'Jenis Kelamin', N'Perempuan', NULL, NULL, N'P', 0, N'', 10),
        (N'Jenis Kelamin', N'Laki-laki', NULL, NULL, N'L', 1, N'', 20),

        (N'Tekanan Darah Sistolik', N'< 120',     NULL, 119,  NULL, 0, N'Normal', 10),
        (N'Tekanan Darah Sistolik', N'120 – 129', 120,  129,  NULL, 1, N'Perhatian', 20),
        (N'Tekanan Darah Sistolik', N'130 – 139', 130,  139,  NULL, 2, N'Perhatian', 30),
        (N'Tekanan Darah Sistolik', N'140 – 159', 140,  159,  NULL, 3, N'Risiko', 40),
        (N'Tekanan Darah Sistolik', N'≥ 160',     160,  NULL, NULL, 4, N'Risiko', 50),

        (N'Kolesterol Total', N'< 180',     NULL, 179,  NULL, 0, N'Normal', 10),
        (N'Kolesterol Total', N'180 – 199', 180,  199,  NULL, 1, N'Normal', 20),
        (N'Kolesterol Total', N'200 – 239', 200,  239,  NULL, 2, N'Perhatian', 30),
        (N'Kolesterol Total', N'240 – 279', 240,  279,  NULL, 3, N'Risiko', 40),
        (N'Kolesterol Total', N'≥ 280',     280,  NULL, NULL, 4, N'Risiko', 50),

        (N'Kolesterol HDL', N'≥ 60',    60,   NULL, NULL, 0, N'Normal', 10),
        (N'Kolesterol HDL', N'50 – 59', 50,   59,   NULL, 1, N'Normal', 20),
        (N'Kolesterol HDL', N'40 – 49', 40,   49,   NULL, 2, N'Perhatian', 30),
        (N'Kolesterol HDL', N'< 40',    NULL, 39,   NULL, 3, N'Risiko', 40),

        (N'Kolesterol LDL', N'< 100',     NULL, 99,   NULL, 0, N'Normal', 10),
        (N'Kolesterol LDL', N'100 – 129', 100,  129,  NULL, 1, N'Normal', 20),
        (N'Kolesterol LDL', N'130 – 159', 130,  159,  NULL, 2, N'Perhatian', 30),
        (N'Kolesterol LDL', N'160 – 189', 160,  189,  NULL, 3, N'Risiko', 40),
        (N'Kolesterol LDL', N'≥ 190',     190,  NULL, NULL, 4, N'Risiko', 50),

        (N'Trigliserida', N'< 150',     NULL, 149,  NULL, 0, N'Normal', 10),
        (N'Trigliserida', N'150 – 199', 150,  199,  NULL, 1, N'Perhatian', 20),
        (N'Trigliserida', N'200 – 499', 200,  499,  NULL, 2, N'Risiko', 30),
        (N'Trigliserida', N'≥ 500',     500,  NULL, NULL, 3, N'Risiko', 40),

        (N'IMT (Indeks Massa Tubuh)', N'< 18,5',      NULL, 18.4, NULL, 0, N'Info', 10),
        (N'IMT (Indeks Massa Tubuh)', N'18,5 – 24,9', 18.5, 24.9, NULL, 0, N'Normal', 20),
        (N'IMT (Indeks Massa Tubuh)', N'25,0 – 29,9', 25.0, 29.9, NULL, 1, N'Perhatian', 30),
        (N'IMT (Indeks Massa Tubuh)', N'≥ 30',        30,   NULL, NULL, 2, N'Risiko', 40),

        (N'Diabetes Melitus', N'Tidak', NULL, NULL, NULL, 0, N'Normal', 10),
        (N'Diabetes Melitus', N'Ya',    NULL, NULL, NULL, 3, N'Risiko', 20),

        (N'Status Merokok', N'Tidak pernah merokok', NULL, NULL, NULL, 0, N'Normal', 10),
        (N'Status Merokok', N'Mantan perokok',       NULL, NULL, NULL, 1, N'Perhatian', 20),
        (N'Status Merokok', N'Perokok aktif',        NULL, NULL, NULL, 3, N'Risiko', 30),

        (N'Aktivitas Fisik', N'≥ 150 menit',    NULL, NULL, NULL, 0, N'Normal', 10),
        (N'Aktivitas Fisik', N'60 – 149 menit', NULL, NULL, NULL, 1, N'Perhatian', 20),
        (N'Aktivitas Fisik', N'< 60 menit',     NULL, NULL, NULL, 2, N'Risiko', 30)
    ) v(Param, Label, MinV, MaxV, MatchText, Points, Lvl, Sort)
    INNER JOIN dbo.CheckupParameters p ON p.Name = v.Param;
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.CheckupRiskLevels)
BEGIN
    INSERT INTO dbo.CheckupRiskLevels (MinScore, MaxScore, Label, Color, Description, SortOrder) VALUES
    (0,  5,    N'Risiko Rendah',        'success', N'Faktor risiko relatif sedikit, kondisi kesehatan baik', 10),
    (6,  10,   N'Risiko Meningkat',     'warning', N'Ada beberapa faktor risiko, perlu perbaikan gaya hidup', 20),
    (11, 15,   N'Risiko Tinggi',        'orange',  N'Banyak faktor risiko, perlu intervensi terstruktur', 30),
    (16, NULL, N'Risiko Sangat Tinggi', 'danger',  N'Faktor risiko signifikan, perlu evaluasi medis & intervensi intensif', 40);
END
GO
