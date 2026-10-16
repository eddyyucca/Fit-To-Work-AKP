using System.Globalization;

namespace FitToWorkApp.Services
{
    /// <summary>Hasil pengecekan login Riwayat Saya.</summary>
    public enum PortalLoginCheck
    {
        /// <summary>Password sudah pernah diganti dan cocok.</summary>
        Ok,

        /// <summary>Masih password default (tanggal lahir) dan cocok: wajib ganti password dulu.</summary>
        OkMustChange,

        /// <summary>Password salah.</summary>
        Wrong,

        /// <summary>Password belum pernah diganti dan tanggal lahir kosong: tidak bisa login, hubungi admin.</summary>
        NoBirthDate
    }

    /// <summary>
    /// Aturan password "Riwayat Saya" (logika murni, tanpa database, mudah di-test).
    /// - Password default = tanggal lahir format ddMMyyyy, mis. 17 Agustus 1990 -> "17081990".
    /// - Hash NULL di database = karyawan masih memakai password default.
    /// - Setelah diganti, tanggal lahir tidak bisa dipakai login lagi (yang dicek hanya hash).
    /// </summary>
    public static class PortalPasswordRules
    {
        public const int MinLength = 8;
        public const int MaxLength = 100;

        /// <summary>Password default dari tanggal lahir: ddMMyyyy.</summary>
        public static string DefaultPassword(DateTime tanggalLahir) =>
            tanggalLahir.ToString("ddMMyyyy", CultureInfo.InvariantCulture);

        /// <summary>
        /// Cek password login.
        /// </summary>
        /// <param name="input">Password yang diketik karyawan.</param>
        /// <param name="passwordHash">Hash dari database (NULL = masih default).</param>
        /// <param name="tanggalLahir">Tanggal lahir karyawan.</param>
        /// <param name="verifyHash">Fungsi pembanding hash (BCrypt di aplikasi; fungsi palsu di test).</param>
        public static PortalLoginCheck Check(string? input, string? passwordHash, DateTime? tanggalLahir,
            Func<string, string, bool> verifyHash)
        {
            input ??= string.Empty;

            if (!string.IsNullOrEmpty(passwordHash))
                return input.Length > 0 && verifyHash(input, passwordHash) ? PortalLoginCheck.Ok : PortalLoginCheck.Wrong;

            if (!tanggalLahir.HasValue)
                return PortalLoginCheck.NoBirthDate;

            // Password default: pemisah diabaikan supaya "17-08-1990" / "17/08/1990" juga diterima
            var digits = new string(input.Where(char.IsDigit).ToArray());
            return digits == DefaultPassword(tanggalLahir.Value) ? PortalLoginCheck.OkMustChange : PortalLoginCheck.Wrong;
        }

        /// <summary>Validasi password baru. Mengembalikan pesan error, atau null kalau valid.</summary>
        public static string? ValidateNew(string? newPassword, string? confirmPassword, string nip, DateTime? tanggalLahir)
        {
            if (string.IsNullOrEmpty(newPassword))
                return "Password baru wajib diisi.";
            if (newPassword.Length < MinLength)
                return $"Password baru minimal {MinLength} karakter.";
            if (newPassword.Length > MaxLength)
                return $"Password baru maksimal {MaxLength} karakter.";
            if (newPassword.Trim().Length != newPassword.Length)
                return "Password tidak boleh diawali atau diakhiri spasi.";
            if (!string.IsNullOrEmpty(nip) && string.Equals(newPassword, nip, StringComparison.OrdinalIgnoreCase))
                return "Password tidak boleh sama dengan NIP.";
            if (tanggalLahir.HasValue && LooksLikeBirthDate(newPassword, tanggalLahir.Value))
                return "Password tidak boleh memakai tanggal lahir. Buat password baru yang berbeda.";
            if (newPassword != confirmPassword)
                return "Ulangi password baru tidak sama.";
            return null;
        }

        // Tanggal lahir dalam format umum (dengan / tanpa pemisah) dianggap sama dengan password default
        private static bool LooksLikeBirthDate(string password, DateTime tgl)
        {
            var digits = new string(password.Where(char.IsDigit).ToArray());
            var onlyDateChars = password.All(c => char.IsDigit(c) || c is '-' or '/' or '.' or ' ');
            if (!onlyDateChars)
                return false;

            var variants = new[] { "ddMMyyyy", "yyyyMMdd", "MMddyyyy", "ddMMyy" }
                .Select(f => tgl.ToString(f, CultureInfo.InvariantCulture));
            return variants.Contains(digits);
        }
    }
}
