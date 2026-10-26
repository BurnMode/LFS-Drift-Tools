using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LFSDriftTools
{
    public enum SecureFileKind { Scores, Achievements }

    public enum SecureFileError { None, NotOurFormat, UnknownVersion, OtherBuild, Tampered, Invalid }

    public readonly record struct SecureLoadResult(SecureFileError Error, bool RestoredFromBackup, bool SaveBlocked);

    public static class SecureFile
    {
        private const byte FormatVersion = 1;
        private const int HeaderSize = 6;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int PayloadOffset = HeaderSize + NonceSize + TagSize;

        private static readonly byte[] ScoresKey = DeriveKey("LDT scores v1");
        private static readonly byte[] AchievementsKey = DeriveKey("LDT achievements v1");

        public static bool IsReleaseKey => BuildSecrets.IsReleaseKey;

        public static byte[] Protect(SecureFileKind kind, string identity, string json)
        {
            byte[] plain = Encoding.UTF8.GetBytes(json);
            byte[] result = new byte[PayloadOffset + plain.Length];
            Magic(kind).CopyTo(result, 0);
            result[4] = FormatVersion;
            result[5] = BuildSecrets.SaveKeyId;
            RandomNumberGenerator.Fill(result.AsSpan(HeaderSize, NonceSize));

            using var aes = new AesGcm(Key(kind), TagSize);
            aes.Encrypt(result.AsSpan(HeaderSize, NonceSize), plain, result.AsSpan(PayloadOffset),
                result.AsSpan(HeaderSize + NonceSize, TagSize), AssociatedData(result, identity));
            return result;
        }

        public static SecureFileError TryUnprotect(SecureFileKind kind, string identity, byte[] data, out string json)
        {
            json = "";
            if (data.Length < PayloadOffset || !data.AsSpan(0, 4).SequenceEqual(Magic(kind)))
                return SecureFileError.NotOurFormat;
            if (data[4] != FormatVersion)
                return SecureFileError.UnknownVersion;
            if (data[5] != BuildSecrets.SaveKeyId)
                return SecureFileError.OtherBuild;

            byte[] plain = new byte[data.Length - PayloadOffset];
            try
            {
                using var aes = new AesGcm(Key(kind), TagSize);
                aes.Decrypt(data.AsSpan(HeaderSize, NonceSize), data.AsSpan(PayloadOffset),
                    data.AsSpan(HeaderSize + NonceSize, TagSize), plain, AssociatedData(data, identity));
            }
            catch (AuthenticationTagMismatchException)
            {
                return SecureFileError.Tampered;
            }

            json = Encoding.UTF8.GetString(plain);
            return SecureFileError.None;
        }

        public static void Write<T>(string path, SecureFileKind kind, string identity, T value)
        {
            AtomicFile.WriteAllBytes(path, Protect(kind, identity, JsonSerializer.Serialize(value)));
        }

        public static SecureLoadResult Load<T>(string path, SecureFileKind kind, string identity, Func<T, bool> isValid, out T? value)
            where T : class
        {
            value = null;
            if (!File.Exists(path)) return default;

            SecureFileError error = Read(path, kind, identity, isValid, out value);
            if (error == SecureFileError.None) return default;

            ErrorLog.Write("Secure file", new InvalidDataException($"{Path.GetFileName(path)}: {error}"));
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            bool blocked = !MoveAside(path, ".rejected_" + stamp);

            bool restored = false;
            string backup = path + ".bak";
            if (File.Exists(backup))
            {
                try
                {
                    restored = Read(backup, kind, identity, isValid, out value) == SecureFileError.None;
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Secure file", ex);
                }
                if (!restored)
                {
                    value = null;
                    MoveAside(backup, ".rejected_" + stamp);
                }
            }

            return new SecureLoadResult(error, restored, blocked);
        }

        public static void ArchiveLegacy(string path)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            foreach (string candidate in new[] { path, path + ".bak", path + ".tmp" })
                if (File.Exists(candidate)) MoveAside(candidate, ".old_" + stamp);
        }

        public static string DescribeRejection(SecureFileKind kind, string name, SecureLoadResult result)
        {
            string reason = (kind, result.Error == SecureFileError.OtherBuild) switch
            {
                (SecureFileKind.Scores, true) => string.Format(Localization.T("status.secure.scores_otherbuild"), name),
                (SecureFileKind.Scores, false) => string.Format(Localization.T("status.secure.scores_tampered"), name),
                (_, true) => Localization.T("status.secure.achievements_otherbuild"),
                _ => Localization.T("status.secure.achievements_tampered"),
            };
            string outcome = Localization.T(result.RestoredFromBackup ? "status.secure.backup_loaded" : "status.secure.started_empty");
            string text = reason + " " + outcome;
            if (result.SaveBlocked) text += " " + Localization.T("status.secure.save_blocked");
            return text;
        }

        private static SecureFileError Read<T>(string path, SecureFileKind kind, string identity, Func<T, bool> isValid, out T? value)
            where T : class
        {
            value = null;
            SecureFileError error = TryUnprotect(kind, identity, File.ReadAllBytes(path), out string json);
            if (error != SecureFileError.None) return error;

            try
            {
                value = JsonSerializer.Deserialize<T>(json);
            }
            catch (JsonException)
            {
                return SecureFileError.Invalid;
            }

            if (value == null || !isValid(value))
            {
                value = null;
                return SecureFileError.Invalid;
            }
            return SecureFileError.None;
        }

        private static bool MoveAside(string path, string suffix)
        {
            try
            {
                File.Move(path, path + suffix);
                return true;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Secure file", ex);
                return !File.Exists(path);
            }
        }

        private static byte[] Magic(SecureFileKind kind) =>
            Encoding.ASCII.GetBytes(kind == SecureFileKind.Scores ? "LDTS" : "LDTA");

        private static byte[] Key(SecureFileKind kind) =>
            kind == SecureFileKind.Scores ? ScoresKey : AchievementsKey;

        private static byte[] AssociatedData(byte[] data, string identity)
        {
            byte[] id = Encoding.UTF8.GetBytes(identity);
            byte[] result = new byte[HeaderSize + id.Length];
            Array.Copy(data, result, HeaderSize);
            id.CopyTo(result, HeaderSize);
            return result;
        }

        private static byte[] DeriveKey(string purpose)
        {
            byte[] a = Convert.FromHexString(BuildSecrets.SaveKeyA);
            byte[] b = Convert.FromHexString(BuildSecrets.SaveKeyB);
            byte[] master = new byte[a.Length];
            for (int i = 0; i < master.Length; i++)
                master[i] = (byte)(a[i] ^ b[i]);

            try
            {
                return HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes(purpose));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(master);
            }
        }
    }
}
