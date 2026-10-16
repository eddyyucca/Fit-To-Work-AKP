namespace FitToWorkApp.Services
{
    // Wrapper tipis di atas BCrypt.Net supaya mudah diganti implementasinya kalau perlu.
    public static class PasswordHasher
    {
        public static string Hash(string plainText) => BCrypt.Net.BCrypt.HashPassword(plainText);

        public static bool Verify(string plainText, string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(plainText, hash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }
    }
}
