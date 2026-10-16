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
