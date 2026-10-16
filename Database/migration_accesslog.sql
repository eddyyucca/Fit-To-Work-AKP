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
