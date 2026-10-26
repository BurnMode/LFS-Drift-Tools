using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LFSDriftTools
{
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string contents, bool keepBackup = true)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, contents);
            Commit(temp, path, keepBackup);
        }

        public static void WriteAllBytes(string path, byte[] contents, bool keepBackup = true)
        {
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, contents);
            Commit(temp, path, keepBackup);
        }

        public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding encoding, bool keepBackup = true)
        {
            string temp = path + ".tmp";
            File.WriteAllLines(temp, lines, encoding);
            Commit(temp, path, keepBackup);
        }

        private static void Commit(string temp, string path, bool keepBackup)
        {
            if (File.Exists(path))
                File.Replace(temp, path, keepBackup ? path + ".bak" : null);
            else
                File.Move(temp, path);
        }
    }
}
