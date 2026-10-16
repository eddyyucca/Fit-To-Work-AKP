-- ============================================================
-- Fit To Work - PT. Adhi Kartiko Pratama, Tbk
-- Database schema (SQL Server)
-- ============================================================

IF DB_ID('FitToWorkDb') IS NULL
BEGIN
    CREATE DATABASE FitToWorkDb;
END
GO

USE FitToWorkDb;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Pilihan-pilihan (radio/checkbox/dropdown) yang dipakai form Fit To Work.
-- Dulunya hardcode di kode C#, sekarang dinamis lewat tabel ini supaya admin
-- bisa tambah/ubah/nonaktifkan pilihan tanpa perlu deploy ulang aplikasi.
IF OBJECT_ID('dbo.FormOptions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FormOptions
    (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        GroupKey     NVARCHAR(50)  NOT NULL,   -- lihat FormOptionGroups di Models/FormOption.cs
        OptionValue  NVARCHAR(300) NOT NULL,
        SortOrder    INT           NOT NULL DEFAULT(0),
        IsActive     BIT           NOT NULL DEFAULT(1)
    );

    CREATE INDEX IX_FormOptions_GroupKey ON dbo.FormOptions(GroupKey, SortOrder);
END
GO

-- Seed data awal (dipakai kalau tabel masih kosong), sama seperti daftar
-- statis yang dulu ada di FitToWorkFormViewModel.
IF NOT EXISTS (SELECT 1 FROM dbo.FormOptions)
BEGIN
    INSERT INTO dbo.FormOptions (GroupKey, OptionValue, SortOrder) VALUES
    ('JadwalKerja', 'NON SHIFT', 1),
    ('JadwalKerja', 'SHIFT PAGI', 2),
    ('JadwalKerja', 'SHIFT MALAM', 3),

    ('AreaKerja', 'LAPANGAN', 1),
    ('AreaKerja', 'OFFICE', 2),
    ('AreaKerja', 'LAPANGAN & OFFICE', 3),
    ('AreaKerja', 'Other', 4),

    ('JamTidur', '< 5 Jam (kurang dari 5 jam)', 1),
    ('JamTidur', '5 Jam', 2),
    ('JamTidur', '6 Jam', 3),
    ('JamTidur', '7 Jam', 4),
    ('JamTidur', '8 Jam', 5),
    ('JamTidur', '> 8 Jam (lebih dari 8 jam)', 6),

    ('KualitasTidur', 'Sangat Buruk: Terbangun berkali-kali, merasa sangat lemas.', 1),
    ('KualitasTidur', 'Buruk: Tidur tidak nyenyak, masih merasa mengantuk.', 2),
    ('KualitasTidur', 'Cukup: Tidur standar, kondisi fisik biasa saja.', 3),
    ('KualitasTidur', 'Baik: Tidur nyenyak, merasa cukup segar.', 4),
    ('KualitasTidur', 'Sangat Baik: Tidur sangat berkualitas, merasa sangat fit & siap kerja.', 5),

    ('GejalaKesehatan', 'Sakit Kepala / Pusing', 1),
    ('GejalaKesehatan', 'Batuk / Pilek / Sesak Nafas', 2),
    ('GejalaKesehatan', 'Sakit Perut / Mual / Diare', 3),
    ('GejalaKesehatan', 'Nyeri Otot / Pegal Linu', 4),
    ('GejalaKesehatan', 'Pandangan Kabur / Berkunang-kunang', 5),
    ('GejalaKesehatan', 'TIDAK ADA GEJALA', 6),

    ('KondisiFokus', 'Fokus & Tenang (Siap Bekerja)', 1),
    ('KondisiFokus', 'Sedikit Terganggu (Masalah pribadi ringan)', 2),
    ('KondisiFokus', 'Sangat Terganggu (Ada masalah berat/sulit fokus)', 3),

    ('KesiapanKerja', 'YA, SAYA SIAP & FIT UNTUK BEKERJA', 1),
    ('KesiapanKerja', 'TIDAK, SAYA MERASA TIDAK FIT/KURANG SEHAT', 2);
END
GO

-- Catatan migrasi: jika database Anda sudah terlanjur dibuat dengan versi skema
-- sebelumnya (yang punya kolom Employees.PasswordHash), jalankan ini dulu:
--   IF COL_LENGTH('dbo.Employees', 'PasswordHash') IS NOT NULL
--       ALTER TABLE dbo.Employees DROP COLUMN PasswordHash;

-- Master data karyawan (dipakai untuk login & auto-fill form)
IF OBJECT_ID('dbo.Employees', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Employees
    (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        NIP           NVARCHAR(20)  NOT NULL UNIQUE,      -- Nomor Induk Karyawan (dipakai untuk login)
        Nama          NVARCHAR(150) NOT NULL,
        NomorHp       NVARCHAR(20)  NULL,
        Section       NVARCHAR(100) NOT NULL,              -- Department / bagian, prefill ke form
        IsActive      BIT           NOT NULL DEFAULT(1),
        CreatedAt     DATETIME2     NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

-- Catatan migrasi: jika database Anda sudah terlanjur dibuat dengan versi skema
-- sebelumnya (yang belum punya kolom AdminUsers.EmployeeId), jalankan ini dulu:
--   IF COL_LENGTH('dbo.AdminUsers', 'EmployeeId') IS NULL
--       ALTER TABLE dbo.AdminUsers ADD EmployeeId INT NULL REFERENCES dbo.Employees(Id);

-- Akun admin (untuk mengakses panel admin)
IF OBJECT_ID('dbo.AdminUsers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdminUsers
    (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        Username      NVARCHAR(50)  NOT NULL UNIQUE,
        PasswordHash  NVARCHAR(255) NOT NULL,
        FullName      NVARCHAR(150) NOT NULL,
        EmployeeId    INT           NULL REFERENCES dbo.Employees(Id),  -- opsional, kalau admin juga terdaftar sbg karyawan
        CreatedAt     DATETIME2     NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

-- Riwayat pengisian Fit To Work
IF OBJECT_ID('dbo.FitToWorkSubmissions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FitToWorkSubmissions
    (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeId          INT NOT NULL REFERENCES dbo.Employees(Id),
        NamaSnapshot        NVARCHAR(150) NOT NULL,   -- snapshot nama saat submit (jaga histori jika nama berubah)
        SectionSnapshot     NVARCHAR(100) NOT NULL,
        NomorHp             NVARCHAR(20)  NULL,
        JadwalKerja         NVARCHAR(20)  NOT NULL,   -- NON SHIFT / SHIFT PAGI / SHIFT MALAM
        AreaKerja           NVARCHAR(50)  NOT NULL,   -- LAPANGAN / OFFICE / LAPANGAN & OFFICE / Other
        AreaKerjaLainnya    NVARCHAR(100) NULL,
        JamTidur            NVARCHAR(20)  NOT NULL,
        KualitasTidur       NVARCHAR(100) NOT NULL,
        GejalaKesehatan     NVARCHAR(300) NOT NULL,   -- gabungan gejala dipisah koma, atau 'TIDAK ADA GEJALA'
        KondisiFokus        NVARCHAR(100) NOT NULL,
        KonsumsiObat        NVARCHAR(10)  NOT NULL,   -- YA / TIDAK
        KesiapanKerja       NVARCHAR(60)  NOT NULL,   -- FIT / TIDAK FIT
        CreatedAt           DATETIME2     NOT NULL DEFAULT(SYSUTCDATETIME())
    );

    CREATE INDEX IX_FitToWorkSubmissions_EmployeeId ON dbo.FitToWorkSubmissions(EmployeeId);
    CREATE INDEX IX_FitToWorkSubmissions_CreatedAt ON dbo.FitToWorkSubmissions(CreatedAt);
END
GO

-- Catatan: akun admin default (admin / Admin123!) di-seed OTOMATIS oleh aplikasi
-- saat pertama kali dijalankan (lihat SeedAdminUser() di Program.cs), supaya
-- password hash yang tersimpan pasti valid (di-generate oleh BCrypt saat runtime).
GO


-- ============================================================
-- Data Organisasi: Department -> Section -> Position
-- Menggantikan daftar Section yang dulu hardcode (SectionList.All di kode C#).
-- ============================================================

IF OBJECT_ID('dbo.Departments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Departments
    (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        Name      NVARCHAR(150) NOT NULL,
        SortOrder INT NOT NULL DEFAULT(0),
        IsActive  BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

IF OBJECT_ID('dbo.Sections', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Sections
    (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        DepartmentId INT NOT NULL REFERENCES dbo.Departments(Id),
        Name         NVARCHAR(150) NOT NULL,
        SortOrder    INT NOT NULL DEFAULT(0),
        IsActive     BIT NOT NULL DEFAULT(1),
        CreatedAt    DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_Sections_DepartmentId ON dbo.Sections(DepartmentId);
END
GO

IF OBJECT_ID('dbo.Positions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Positions
    (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        Name      NVARCHAR(150) NOT NULL,
        SortOrder INT NOT NULL DEFAULT(0),
        IsActive  BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

-- Seed dari daftar Section lama (SectionList.All) supaya data existing tidak
-- hilang - semua dimasukkan dulu di bawah Department "Umum", admin bisa
-- reorganisir manual lewat menu Data Organisasi kapan saja.
IF NOT EXISTS (SELECT 1 FROM dbo.Departments)
BEGIN
    INSERT INTO dbo.Departments (Name, SortOrder) VALUES ('Umum', 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Sections)
BEGIN
    DECLARE @DefaultDeptId INT = (SELECT TOP 1 Id FROM dbo.Departments WHERE Name = 'Umum');

    INSERT INTO dbo.Sections (DepartmentId, Name, SortOrder) VALUES
    (@DefaultDeptId, 'GENERAL MANAGER', 1),
    (@DefaultDeptId, 'MANAGER', 2),
    (@DefaultDeptId, 'PRODUCTION', 3),
    (@DefaultDeptId, 'MINE PROJECT', 4),
    (@DefaultDeptId, 'GRADE CONTROL', 5),
    (@DefaultDeptId, 'STOCKPILE', 6),
    (@DefaultDeptId, 'PREPARATION', 7),
    (@DefaultDeptId, 'LABORATORIUM', 8),
    (@DefaultDeptId, 'SHIPPING', 9),
    (@DefaultDeptId, 'GEOLOGY', 10),
    (@DefaultDeptId, 'MINE PLAN ENGINEERING', 11),
    (@DefaultDeptId, 'SURVEY', 12),
    (@DefaultDeptId, 'CONSTRUCTION', 13),
    (@DefaultDeptId, 'MECHANICAL ELECTRICAL', 14),
    (@DefaultDeptId, 'COMPLIANCE', 15),
    (@DefaultDeptId, 'SAFETY / ERT / HEALTH', 16),
    (@DefaultDeptId, 'ENVIRONMENT', 17),
    (@DefaultDeptId, 'FORESTRY', 18),
    (@DefaultDeptId, 'HR DEVELOPMENT', 19),
    (@DefaultDeptId, 'HR OPERATIONAL', 20),
    (@DefaultDeptId, 'GA', 21),
    (@DefaultDeptId, 'IT', 22),
    (@DefaultDeptId, 'EXTERNAL RELATION', 23),
    (@DefaultDeptId, 'SECURITY', 24),
    (@DefaultDeptId, 'FINANCE', 25),
    (@DefaultDeptId, 'PLANNING & LOGISTIC', 26),
    (@DefaultDeptId, 'ENGINEERING - PROJECT DEVELOPMENT', 27),
    (@DefaultDeptId, 'NON ENGINEERING - STONE CRUSHER - PROJECT DEVELOPMENT', 28);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Positions)
BEGIN
    INSERT INTO dbo.Positions (Name, SortOrder) VALUES
    ('Staff', 1),
    ('Supervisor', 2),
    ('Superintendent', 3),
    ('Manager', 4),
    ('General Manager', 5);
END
GO

-- Tambah kolom Department & Position ke Employees (idempotent, aman dijalankan berkali-kali)
IF COL_LENGTH('dbo.Employees', 'Department') IS NULL
    ALTER TABLE dbo.Employees ADD Department NVARCHAR(150) NULL;
GO

IF COL_LENGTH('dbo.Employees', 'Position') IS NULL
    ALTER TABLE dbo.Employees ADD Position NVARCHAR(150) NULL;
GO

-- ============================================================
-- Data Organisasi: Division, Department -> Section, Position
-- (Division, Department, Position berdiri sendiri/flat; Section terikat ke Department)
-- Menggantikan daftar Section yang dulu hardcode (SectionList.All di kode C#).
-- ============================================================

IF OBJECT_ID('dbo.Divisions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Divisions
    (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        Name      NVARCHAR(150) NOT NULL,
        SortOrder INT NOT NULL DEFAULT(0),
        IsActive  BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

IF OBJECT_ID('dbo.Departments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Departments
    (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        Name      NVARCHAR(150) NOT NULL,
        SortOrder INT NOT NULL DEFAULT(0),
        IsActive  BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

IF OBJECT_ID('dbo.Sections', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Sections
    (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        DepartmentId INT NOT NULL REFERENCES dbo.Departments(Id),
        Name         NVARCHAR(150) NOT NULL,
        SortOrder    INT NOT NULL DEFAULT(0),
        IsActive     BIT NOT NULL DEFAULT(1),
        CreatedAt    DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_Sections_DepartmentId ON dbo.Sections(DepartmentId);
END
GO

IF OBJECT_ID('dbo.Positions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Positions
    (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        Name      NVARCHAR(150) NOT NULL,
        SortOrder INT NOT NULL DEFAULT(0),
        IsActive  BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

-- Seed dari daftar Section lama (SectionList.All) supaya data existing tidak
-- hilang - semua dimasukkan dulu di bawah Department "Umum", admin bisa
-- reorganisir manual lewat menu Data Organisasi kapan saja.
IF NOT EXISTS (SELECT 1 FROM dbo.Divisions)
BEGIN
    INSERT INTO dbo.Divisions (Name, SortOrder) VALUES ('Umum', 1);
END
GO

-- Kolom organisasi di Employees (dipakai EmployeeRepository & import karyawan). Idempotent.
IF COL_LENGTH('dbo.Employees', 'DivisionId') IS NULL
    ALTER TABLE dbo.Employees ADD DivisionId INT NULL CONSTRAINT FK_Employees_Divisions REFERENCES dbo.Divisions(Id);
GO

IF COL_LENGTH('dbo.Employees', 'DepartmentId') IS NULL
    ALTER TABLE dbo.Employees ADD DepartmentId INT NULL CONSTRAINT FK_Employees_Departments REFERENCES dbo.Departments(Id);
GO

IF COL_LENGTH('dbo.Employees', 'SectionId') IS NULL
    ALTER TABLE dbo.Employees ADD SectionId INT NULL CONSTRAINT FK_Employees_Sections REFERENCES dbo.Sections(Id);
GO

IF COL_LENGTH('dbo.Employees', 'PositionId') IS NULL
    ALTER TABLE dbo.Employees ADD PositionId INT NULL CONSTRAINT FK_Employees_Positions REFERENCES dbo.Positions(Id);
GO

-- Kolom teks lama Section sudah digantikan SectionId dan tidak lagi diisi kode -> harus boleh NULL,
-- kalau tidak setiap INSERT karyawan gagal.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Employees') AND name = 'Section' AND is_nullable = 0)
    ALTER TABLE dbo.Employees ALTER COLUMN Section NVARCHAR(100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Departments)
BEGIN
    INSERT INTO dbo.Departments (Name, SortOrder) VALUES ('Umum', 1);
END
GO

-- ============================================================
-- Daftar Gym (pengganti Google Form "DAFTAR HADIR")
-- Data diri disimpan sebagai snapshot; CreatedAt dalam UTC.
-- Aman dijalankan berkali-kali (idempotent).
-- ============================================================
IF OBJECT_ID('dbo.GymAttendances', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GymAttendances
    (
        Id                 INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeId         INT NOT NULL REFERENCES dbo.Employees(Id),
        NamaSnapshot       NVARCHAR(150) NOT NULL,
        DepartmentSnapshot NVARCHAR(150) NOT NULL DEFAULT(''),
        SectionSnapshot    NVARCHAR(150) NOT NULL DEFAULT(''),
        PositionSnapshot   NVARCHAR(150) NOT NULL DEFAULT(''),
        BeratBadan         DECIMAL(5,1) NOT NULL CHECK (BeratBadan BETWEEN 30 AND 200),
        CreatedAt          DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_GymAttendances_CreatedAt ON dbo.GymAttendances(CreatedAt);
    CREATE INDEX IX_GymAttendances_EmployeeId_CreatedAt ON dbo.GymAttendances(EmployeeId, CreatedAt);
END
GO


-- ============================================================
-- RBAC: Role & hak akses per menu panel admin
-- Aman dijalankan berkali-kali (idempotent).
-- Admin lama yang belum punya role otomatis jadi Super Admin
-- supaya tidak ada yang tiba-tiba kehilangan akses.
-- ============================================================
IF OBJECT_ID('dbo.Roles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Roles
    (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        Name         NVARCHAR(100) NOT NULL UNIQUE,
        Description  NVARCHAR(250) NULL,
        IsSuperAdmin BIT NOT NULL DEFAULT(0),   -- 1 = semua akses, tidak bisa dihapus
        CreatedAt    DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END
GO

IF OBJECT_ID('dbo.RolePermissions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RolePermissions
    (
        RoleId     INT NOT NULL REFERENCES dbo.Roles(Id) ON DELETE CASCADE,
        Permission NVARCHAR(50) NOT NULL,       -- key dari Security/Permissions.cs, mis. 'gym.history'
        CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleId, Permission)
    );
END
GO

IF COL_LENGTH('dbo.AdminUsers', 'RoleId') IS NULL
    ALTER TABLE dbo.AdminUsers ADD RoleId INT NULL REFERENCES dbo.Roles(Id);
GO

-- Role bawaan
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE IsSuperAdmin = 1)
    INSERT INTO dbo.Roles (Name, Description, IsSuperAdmin) VALUES ('Super Admin', 'Akses penuh ke semua menu', 1);

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'HSE')
BEGIN
    INSERT INTO dbo.Roles (Name, Description) VALUES ('HSE', 'Monitoring Fit To Work, Daftar Gym & master form');
    INSERT INTO dbo.RolePermissions (RoleId, Permission)
    SELECT r.Id, p.Permission FROM dbo.Roles r
    CROSS JOIN (VALUES ('ftw.dashboard'), ('ftw.history'), ('gym.history'), ('master.formoptions')) p(Permission)
    WHERE r.Name = 'HSE';
END

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'HR')
BEGIN
    INSERT INTO dbo.Roles (Name, Description) VALUES ('HR', 'Data karyawan & organisasi, lihat riwayat');
    INSERT INTO dbo.RolePermissions (RoleId, Permission)
    SELECT r.Id, p.Permission FROM dbo.Roles r
    CROSS JOIN (VALUES ('ftw.dashboard'), ('ftw.history'), ('gym.history'), ('master.employees'), ('master.organization')) p(Permission)
    WHERE r.Name = 'HR';
END
GO

UPDATE dbo.AdminUsers
SET RoleId = (SELECT TOP 1 Id FROM dbo.Roles WHERE IsSuperAdmin = 1 ORDER BY Id)
WHERE RoleId IS NULL;
GO


-- ============================================================
-- Log akses admin (login, logout, akses ditolak, aktivitas)
-- CreatedAt UTC. Log lama dihapus otomatis oleh aplikasi
-- setelah App:AccessLogRetentionDays hari (default 365).
-- Aman dijalankan berkali-kali (idempotent).
-- ============================================================
IF OBJECT_ID('dbo.AccessLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccessLogs
    (
        Id          BIGINT IDENTITY(1,1) PRIMARY KEY,
        CreatedAt   DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        AdminUserId INT NULL,                   -- sengaja tanpa FK: log tetap ada walau akun admin dihapus
        Username    NVARCHAR(100) NOT NULL,
        EventType   NVARCHAR(30)  NOT NULL,     -- Login, LoginGagal, Logout, AksesDitolak, Lihat, Export, Ubah
        Activity    NVARCHAR(300) NOT NULL,
        HttpMethod  NVARCHAR(10)  NULL,
        Path        NVARCHAR(500) NULL,
        StatusCode  INT           NULL,
        IpAddress   NVARCHAR(64)  NULL,
        UserAgent   NVARCHAR(300) NULL
    );
    CREATE INDEX IX_AccessLogs_CreatedAt ON dbo.AccessLogs(CreatedAt);
    CREATE INDEX IX_AccessLogs_Username_CreatedAt ON dbo.AccessLogs(Username, CreatedAt);
    CREATE INDEX IX_AccessLogs_EventType_CreatedAt ON dbo.AccessLogs(EventType, CreatedAt);
END
GO


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

-- ============================================================
-- (dari migration_master_klinik.sql)
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

-- ============================================================
-- Status Aktif/Nonaktif akun admin + 1 karyawan hanya untuk 1 akun admin
--   - dbo.AdminUsers.IsActive : 0 = tidak bisa login, sesi yang sedang berjalan diputus otomatis.
--                               Akun lama otomatis aktif (default 1).
--   - UX_AdminUsers_EmployeeId: satu karyawan hanya boleh terhubung ke satu akun admin.
--                               Dibuat hanya kalau data lama belum ada yang dobel.
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_mitra.sql.
-- ============================================================

IF COL_LENGTH('dbo.AdminUsers', 'IsActive') IS NULL
    ALTER TABLE dbo.AdminUsers ADD IsActive BIT NOT NULL CONSTRAINT DF_AdminUsers_IsActive DEFAULT(1);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_AdminUsers_EmployeeId' AND object_id = OBJECT_ID('dbo.AdminUsers'))
BEGIN
    IF EXISTS (SELECT EmployeeId FROM dbo.AdminUsers WHERE EmployeeId IS NOT NULL GROUP BY EmployeeId HAVING COUNT(1) > 1)
        PRINT 'PERINGATAN: ada karyawan yang terhubung ke lebih dari 1 akun admin. Rapikan lewat menu Edit User, lalu jalankan ulang script ini.';
    ELSE
        CREATE UNIQUE INDEX UX_AdminUsers_EmployeeId ON dbo.AdminUsers(EmployeeId) WHERE EmployeeId IS NOT NULL;
END
GO

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

-- ============================================================
-- Gym: check-in & check-out
--   - CreatedAt            = jam check-in (UTC, tidak berubah)
--   - CheckOutAt           = jam check-out (UTC). NULL kalau belum / tidak check-out.
--   - CheckOutStatus       : NULL     = sesi masih terbuka (karyawan masih di gym)
--                            'Manual' = karyawan check-out sendiri -> durasi dihitung
--                            'Auto'   = tidak check-out, ditutup sistem setelah 3 jam -> durasi TIDAK dihitung
--                            'Legacy' = data sebelum fitur check-out (daftar hadir lama)
--   - CheckOutJarakMeter   = jarak ke titik site saat check-out (jejak audit, boleh NULL)
--   - UX_GymAttendances_Open: 1 karyawan hanya boleh punya 1 sesi terbuka (harus check-out dulu).
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_site_location.sql.
-- ============================================================

-- Wajib ON untuk membuat filtered index (default sqlcmd = OFF)
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.GymAttendances', 'CheckOutAt') IS NULL
    ALTER TABLE dbo.GymAttendances ADD
        CheckOutAt         DATETIME2   NULL,
        CheckOutStatus     VARCHAR(10) NULL,
        CheckOutJarakMeter INT         NULL;
GO

-- Semua data lama = daftar hadir tanpa check-out. Ditandai 'Legacy' supaya tidak dianggap "masih di gym".
-- (Hanya dijalankan sekali: sebelum index sesi terbuka dibuat.)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_GymAttendances_Open' AND object_id = OBJECT_ID('dbo.GymAttendances'))
BEGIN
    UPDATE dbo.GymAttendances SET CheckOutStatus = 'Legacy' WHERE CheckOutStatus IS NULL;
    CREATE UNIQUE INDEX UX_GymAttendances_Open ON dbo.GymAttendances(EmployeeId) WHERE CheckOutStatus IS NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_GymAttendances_CheckOutStatus')
    ALTER TABLE dbo.GymAttendances ADD CONSTRAINT CK_GymAttendances_CheckOutStatus
        CHECK (CheckOutStatus IS NULL OR CheckOutStatus IN ('Manual', 'Auto', 'Legacy'));
GO

-- ============================================================
-- Pengaturan aplikasi yang bisa diubah admin (key-value)
--   - dbo.AppSettings : dipakai pertama kali oleh menu Fit To Work > Pengaturan Pengisian (key "Ftw.*"):
--       Ftw.ResetMode    : 'Jeda' (N menit sejak pengisian terakhir) | 'JamTetap' (jam reset tertentu)
--       Ftw.JedaMenit    : jeda dalam menit (default 720 = 12 jam)
--       Ftw.JamReset     : daftar jam reset, mis. '06:00,18:00'
--       Ftw.IzinIsiUlang : '1' = tombol "Isi Ulang" tampil, '0' = tidak
--       Ftw.DetikKembali : lama halaman "sudah mengisi" sebelum kembali ke layar NIP
--     Key yang belum ada = aplikasi memakai nilai default (jeda 12 jam, isi ulang boleh, 8 detik).
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_gym_checkout.sql.
-- ============================================================

IF OBJECT_ID('dbo.AppSettings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppSettings
    (
        [Key]     NVARCHAR(100)  NOT NULL PRIMARY KEY,
        Value     NVARCHAR(1000) NOT NULL,
        UpdatedAt DATETIME2      NOT NULL CONSTRAINT DF_AppSettings_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedBy NVARCHAR(150)  NULL
    );
END
GO

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

-- ============================================================
-- Password "Riwayat Saya" (portal karyawan)
--   - Employees.PortalPasswordHash      : hash BCrypt password Riwayat Saya.
--                                         NULL = masih password default (tanggal lahir ddMMyyyy),
--                                         karyawan wajib ganti password setelah login pertama.
--   - Employees.PortalPasswordChangedAt : kapan terakhir diganti (UTC). NULL = belum pernah / sudah di-reset admin.
-- Tidak perlu mengisi data awal: semua karyawan otomatis mulai dari password default.
-- Reset oleh admin = kedua kolom diset NULL lagi.
-- Aman dijalankan berkali-kali. Jalankan SETELAH migration_fitness_activity.sql.
-- ============================================================

IF COL_LENGTH('dbo.Employees', 'PortalPasswordHash') IS NULL
    ALTER TABLE dbo.Employees ADD PortalPasswordHash NVARCHAR(100) NULL;
GO

IF COL_LENGTH('dbo.Employees', 'PortalPasswordChangedAt') IS NULL
    ALTER TABLE dbo.Employees ADD PortalPasswordChangedAt DATETIME2 NULL;
GO
