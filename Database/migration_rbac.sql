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
