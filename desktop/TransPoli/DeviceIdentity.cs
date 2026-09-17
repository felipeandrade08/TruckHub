using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TransPoli;

public static class DeviceIdentity
{
    private const string FileName = "device-id.dat";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TransPoli-DeviceIdentity-v1");

    public static string GetOrCreate()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);

        try
        {
            if (File.Exists(path))
            {
                var protectedData = File.ReadAllBytes(path);
                var plain = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
                var existing = Encoding.UTF8.GetString(plain).Trim();
                if (Guid.TryParse(existing, out _))
                    return existing;
            }

            var id = Guid.NewGuid().ToString("D");
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(id), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
            return id;
        }
        catch
        {
            return Guid.NewGuid().ToString("D");
        }
    }
}
