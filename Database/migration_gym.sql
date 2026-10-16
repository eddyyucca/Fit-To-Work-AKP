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
