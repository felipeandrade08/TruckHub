using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TransPoli;

internal static class SecureTokenStore
{
    private const string FileName = "access-token.dat";
    private const string UserIdFileName = "account-user-id.dat";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TransPoli-AccessToken-v1");

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");

    private static string ProtectedPath => Path.Combine(Folder, FileName);
    private static string LegacyPath => Path.Combine(Folder, "access-token.txt");
    private static string UserIdPath => Path.Combine(Folder, UserIdFileName);

    public static void Save(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token inválido.", nameof(token));
        Directory.CreateDirectory(Folder);
        var protectedData = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token.Trim()), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(ProtectedPath, protectedData);
        TryDelete(LegacyPath);
        TryDelete(UserIdPath);
    }

    public static string? Read()
    {
        try
        {
            if (File.Exists(ProtectedPath))
            {
                var data = ProtectedData.Unprotect(
                    File.ReadAllBytes(ProtectedPath), Entropy, DataProtectionScope.CurrentUser);
                var token = Encoding.UTF8.GetString(data).Trim();
                if (!string.IsNullOrWhiteSpace(token)) return token;
            }
        }
        catch { }

        // One-time migration from the old plaintext token file.
        try
        {
            if (!File.Exists(LegacyPath)) return null;
            var legacy = File.ReadAllText(LegacyPath).Trim();
            if (string.IsNullOrWhiteSpace(legacy)) return null;
            Save(legacy);
            return legacy;
        }
        catch
        {
            return null;
        }
    }


    public static void SaveUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        Directory.CreateDirectory(Folder);
        var protectedData = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(userId.Trim()), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(UserIdPath, protectedData);
    }

    public static string? ReadUserId()
    {
        try
        {
            if (!File.Exists(UserIdPath)) return null;
            var data = ProtectedData.Unprotect(
                File.ReadAllBytes(UserIdPath), Entropy, DataProtectionScope.CurrentUser);
            var userId = Encoding.UTF8.GetString(data).Trim();
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }
        catch { return null; }
    }

    public static void Delete()
    {
        TryDelete(ProtectedPath);
        TryDelete(LegacyPath);
        TryDelete(UserIdPath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
