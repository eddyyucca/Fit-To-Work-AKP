-- ============================================================
-- PERBAIKAN: pengisian Fit To Work gagal "Terjadi Kesalahan" untuk pilihan tertentu.
--
-- Penyebab: kolom dbo.FitToWorkSubmissions terlalu sempit untuk pilihan form. Contoh: JamTidur NVARCHAR(20), padahal pilihan
-- "< 5 Jam (kurang dari 5 jam)" (27 karakter) dan "> 8 Jam (lebih dari 8 jam)" (26 karakter) lebih panjang ->
-- SQL Server menolak ("String or binary data would be truncated") dan karyawan melihat "Terjadi Kesalahan".
-- Pilihan form (dbo.FormOptions.OptionValue) boleh sampai 300 karakter, jadi kolom jawaban harus selebar itu.
--
-- HANYA MELEBARKAN kolom (nilai yang sudah ada tidak berubah, tidak ada yang hilang). Tidak mengubah tabel lain.
-- Aman dijalankan berkali-kali: kolom yang sudah cukup lebar dilewati. BACKUP database dulu.
-- ============================================================

SET NOCOUNT ON;
GO

DECLARE @cols TABLE (Nama SYSNAME, Karakter INT, NotNull BIT);
INSERT @cols VALUES
    ('SectionSnapshot', 150, 1),      -- nama section di master sampai 150 karakter
    ('JadwalKerja',     300, 1),
    ('AreaKerja',       300, 1),
    ('JamTidur',        300, 1),
    ('KualitasTidur',   300, 1),
    ('KondisiFokus',    300, 1),
    ('KesiapanKerja',   300, 1),
    ('GejalaKesehatan', 1000, 1);     -- gabungan beberapa gejala dipisah koma

DECLARE @n SYSNAME, @k INT, @nn BIT, @sql NVARCHAR(400), @ubah INT = 0;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Nama, Karakter, NotNull FROM @cols;
OPEN c;
FETCH NEXT FROM c INTO @n, @k, @nn;
WHILE @@FETCH_STATUS = 0
BEGIN
    -- max_length dalam byte (NVARCHAR = 2 byte per karakter); -1 = MAX (sudah cukup)
    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FitToWorkSubmissions') AND name = @n AND max_length <> -1 AND max_length < @k * 2)
    BEGIN
        SET @sql = N'ALTER TABLE dbo.FitToWorkSubmissions ALTER COLUMN ' + QUOTENAME(@n) + N' NVARCHAR(' + CAST(@k AS NVARCHAR(10)) + N')'
                 + CASE WHEN @nn = 1 THEN N' NOT NULL' ELSE N' NULL' END;
        EXEC sp_executesql @sql;
        SET @ubah += 1;
    END
    FETCH NEXT FROM c INTO @n, @k, @nn;
END
CLOSE c;
DEALLOCATE c;

PRINT 'Kolom FitToWorkSubmissions dilebarkan: ' + CAST(@ubah AS VARCHAR(10)) + ' (0 = sudah cukup lebar)';
GO
