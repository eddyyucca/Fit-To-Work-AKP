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
