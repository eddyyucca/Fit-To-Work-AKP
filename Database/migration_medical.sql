-- ============================================================
-- Modul Kesehatan: tanggal lahir & jenis kelamin karyawan,
-- master jenis penyakit, master obat + stok, pemeriksaan
-- kesehatan, kunjungan klinik, role Medic.
-- Semua waktu disimpan UTC. Aman dijalankan berkali-kali.
-- Jalankan SETELAH migration_rbac.sql.
-- ============================================================

-- ---------- Data karyawan ----------
IF COL_LENGTH('dbo.Employees', 'TanggalLahir') IS NULL
    ALTER TABLE dbo.Employees ADD TanggalLahir DATE NULL;
GO
IF COL_LENGTH('dbo.Employees', 'JenisKelamin') IS NULL
    ALTER TABLE dbo.Employees ADD JenisKelamin CHAR(1) NULL
        CONSTRAINT CK_Employees_JenisKelamin CHECK (JenisKelamin IN ('L', 'P'));
GO

-- ---------- Master jenis penyakit ----------
IF OBJECT_ID('dbo.Diseases', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Diseases
    (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        Kode      NVARCHAR(20)  NULL,           -- opsional, mis. ICD-10
        Name      NVARCHAR(150) NOT NULL UNIQUE,
        Kategori  NVARCHAR(100) NULL,
        IsActive  BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );

    -- Contoh awal penyakit yang umum di klinik site (boleh diubah/dinonaktifkan)
    INSERT INTO dbo.Diseases (Kode, Name, Kategori) VALUES
    ('J06.9', 'ISPA (Infeksi Saluran Pernapasan Atas)', 'Pernapasan'),
    ('J00',   'Common cold / Selesma', 'Pernapasan'),
    ('J02.9', 'Faringitis', 'Pernapasan'),
    ('K30',   'Dispepsia', 'Pencernaan'),
    ('K29.7', 'Gastritis', 'Pencernaan'),
    ('A09',   'Diare / Gastroenteritis', 'Pencernaan'),
    ('I10',   'Hipertensi', 'Kardiovaskular'),
    ('R51',   'Sakit kepala (Cephalgia)', 'Saraf'),
    ('R42',   'Pusing / Vertigo', 'Saraf'),
    ('M79.1', 'Myalgia / Nyeri otot', 'Muskuloskeletal'),
    ('M54.5', 'Nyeri punggung bawah (Low back pain)', 'Muskuloskeletal'),
    ('R50.9', 'Demam', 'Umum'),
    ('L30.9', 'Dermatitis', 'Kulit'),
    ('H10.9', 'Konjungtivitis', 'Mata'),
    ('K08.8', 'Sakit gigi', 'Gigi & Mulut'),
    ('T14.1', 'Luka terbuka ringan', 'Cedera'),
    ('E11',   'Diabetes melitus tipe 2', 'Metabolik'),
    ('M10.9', 'Gout / Asam urat', 'Metabolik'),
    (NULL,    'Lain-lain', 'Lainnya');
END
GO

-- ---------- Master obat & stok ----------
IF OBJECT_ID('dbo.Medicines', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Medicines
    (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        Name        NVARCHAR(150) NOT NULL UNIQUE,
        Satuan      NVARCHAR(30)  NOT NULL,
        Keterangan  NVARCHAR(250) NULL,
        Stok        INT NOT NULL DEFAULT(0) CONSTRAINT CK_Medicines_Stok CHECK (Stok >= 0),
        StokMinimum INT NOT NULL DEFAULT(0),
        IsActive    BIT NOT NULL DEFAULT(1),
        CreatedAt   DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

-- ---------- Pemeriksaan kesehatan ----------
IF OBJECT_ID('dbo.HealthCheckups', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HealthCheckups
    (
        Id                 INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeId         INT NOT NULL REFERENCES dbo.Employees(Id),
        NamaSnapshot       NVARCHAR(150) NOT NULL,
        DepartmentSnapshot NVARCHAR(150) NOT NULL DEFAULT(''),
        SectionSnapshot    NVARCHAR(150) NOT NULL DEFAULT(''),
        PositionSnapshot   NVARCHAR(150) NOT NULL DEFAULT(''),
        JenisKelamin       CHAR(1) NULL,
        ExaminedAt         DATETIME2 NOT NULL,   -- UTC
        Sistolik           INT NULL,
        Diastolik          INT NULL,
        BeratBadan         DECIMAL(5,1) NULL,
        TinggiBadan        DECIMAL(5,1) NULL,
        LingkarPerut       DECIMAL(5,1) NULL,
        Kolesterol         INT NULL,
        AsamUrat           DECIMAL(4,1) NULL,
        GulaDarah          INT NULL,
        GulaDarahJenis     NVARCHAR(10) NOT NULL DEFAULT('Sewaktu'),
        Keluhan            NVARCHAR(1000) NULL,
        Catatan            NVARCHAR(1000) NULL,
        ExaminerAdminId    INT NULL,
        ExaminerName       NVARCHAR(150) NOT NULL,
        CreatedAt          DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_HealthCheckups_ExaminedAt ON dbo.HealthCheckups(ExaminedAt);
    CREATE INDEX IX_HealthCheckups_EmployeeId_ExaminedAt ON dbo.HealthCheckups(EmployeeId, ExaminedAt);
END
GO

-- ---------- Kunjungan klinik ----------
IF OBJECT_ID('dbo.ClinicVisits', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClinicVisits
    (
        Id                 INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeId         INT NOT NULL REFERENCES dbo.Employees(Id),
        NamaSnapshot       NVARCHAR(150) NOT NULL,
        DepartmentSnapshot NVARCHAR(150) NOT NULL DEFAULT(''),
        SectionSnapshot    NVARCHAR(150) NOT NULL DEFAULT(''),
        PositionSnapshot   NVARCHAR(150) NOT NULL DEFAULT(''),
        VisitAt            DATETIME2 NOT NULL,   -- UTC
        Keluhan            NVARCHAR(1000) NOT NULL,
        Pemeriksaan        NVARCHAR(1000) NULL,
        Rekomendasi        NVARCHAR(30) NOT NULL, -- KembaliBekerja, KerjaTerbatas, Istirahat, Rujuk
        HariIstirahat      INT NULL,
        CatatanRekomendasi NVARCHAR(1000) NULL,
        ExaminerAdminId    INT NULL,
        ExaminerName       NVARCHAR(150) NOT NULL,
        CreatedAt          DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        IsCancelled        BIT NOT NULL DEFAULT(0),
        CancelReason       NVARCHAR(250) NULL,
        CancelledAt        DATETIME2 NULL,
        CancelledBy        NVARCHAR(150) NULL
    );
    CREATE INDEX IX_ClinicVisits_VisitAt ON dbo.ClinicVisits(VisitAt);
    CREATE INDEX IX_ClinicVisits_EmployeeId_VisitAt ON dbo.ClinicVisits(EmployeeId, VisitAt);
END
GO

IF OBJECT_ID('dbo.ClinicVisitDiseases', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClinicVisitDiseases
    (
        ClinicVisitId INT NOT NULL REFERENCES dbo.ClinicVisits(Id),
        DiseaseId     INT NOT NULL REFERENCES dbo.Diseases(Id),
        CONSTRAINT PK_ClinicVisitDiseases PRIMARY KEY (ClinicVisitId, DiseaseId)
    );
    CREATE INDEX IX_ClinicVisitDiseases_DiseaseId ON dbo.ClinicVisitDiseases(DiseaseId);
END
GO

IF OBJECT_ID('dbo.ClinicVisitMedicines', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClinicVisitMedicines
    (
        Id             INT IDENTITY(1,1) PRIMARY KEY,
        ClinicVisitId  INT NOT NULL REFERENCES dbo.ClinicVisits(Id),
        MedicineId     INT NOT NULL REFERENCES dbo.Medicines(Id),
        NamaSnapshot   NVARCHAR(150) NOT NULL,
        SatuanSnapshot NVARCHAR(30)  NOT NULL,
        Qty            INT NOT NULL CHECK (Qty > 0),
        AturanPakai    NVARCHAR(100) NULL
    );
    CREATE INDEX IX_ClinicVisitMedicines_Visit ON dbo.ClinicVisitMedicines(ClinicVisitId);
END
GO

-- Kartu stok: setiap perubahan stok obat tercatat di sini
IF OBJECT_ID('dbo.MedicineStockMovements', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MedicineStockMovements
    (
        Id            BIGINT IDENTITY(1,1) PRIMARY KEY,
        MedicineId    INT NOT NULL REFERENCES dbo.Medicines(Id),
        Type          NVARCHAR(20) NOT NULL,     -- StokAwal, Masuk, Keluar, Penyesuaian, BatalKunjungan
        Qty           INT NOT NULL,              -- + masuk, - keluar
        StokSetelah   INT NOT NULL,
        ClinicVisitId INT NULL REFERENCES dbo.ClinicVisits(Id),
        Keterangan    NVARCHAR(250) NULL,
        AdminUserId   INT NULL,
        AdminName     NVARCHAR(150) NULL,
        CreatedAt     DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_MedicineStockMovements_Medicine ON dbo.MedicineStockMovements(MedicineId, CreatedAt);
END
GO

-- ---------- Role Medic ----------
IF OBJECT_ID('dbo.Roles', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'Medic')
BEGIN
    INSERT INTO dbo.Roles (Name, Description) VALUES ('Medic', 'Pemeriksaan kesehatan, kunjungan klinik, obat & stok');
    INSERT INTO dbo.RolePermissions (RoleId, Permission)
    SELECT r.Id, p.Permission FROM dbo.Roles r
    CROSS JOIN (VALUES ('medical.checkup'), ('medical.visit'), ('medical.master')) p(Permission)
    WHERE r.Name = 'Medic';
END
GO
