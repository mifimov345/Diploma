using System.Security.Cryptography;
using System.Text;

namespace AuthService.Utils
{
    public static class PasswordHelper
    {
        public static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashed = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return System.Convert.ToBase64String(hashed);
            }
        }
    }
}
