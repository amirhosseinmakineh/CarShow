using System;
using System.Security.Cryptography;
using System.Text;

namespace Carshow.Utilities.Hasher
{
    public static class PasswordHasher
    {
        public static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] combined = new byte[passwordBytes.Length + salt.Length];

            Buffer.BlockCopy(passwordBytes, 0, combined, 0, passwordBytes.Length);
            Buffer.BlockCopy(salt, 0, combined, passwordBytes.Length, salt.Length);

            byte[] hash = SHA256.HashData(combined);
            byte[] result = new byte[salt.Length + hash.Length];

            Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
            Buffer.BlockCopy(hash, 0, result, salt.Length, hash.Length);

            return Convert.ToBase64String(result);
        }

        public static bool VerifyPassword(string password, string storedHash)
        {
            byte[] fullBytes = Convert.FromBase64String(storedHash);
            int saltSize = 16;
            byte[] salt = new byte[saltSize];
            byte[] storedHashBytes = new byte[fullBytes.Length - saltSize];

            Buffer.BlockCopy(fullBytes, 0, salt, 0, saltSize);
            Buffer.BlockCopy(fullBytes, saltSize, storedHashBytes, 0, storedHashBytes.Length);

            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] combined = new byte[passwordBytes.Length + salt.Length];

            Buffer.BlockCopy(passwordBytes, 0, combined, 0, passwordBytes.Length);
            Buffer.BlockCopy(salt, 0, combined, passwordBytes.Length, salt.Length);
            byte[] hash = SHA256.HashData(combined);

            return hash.SequenceEqual(storedHashBytes);
        }
    }
}