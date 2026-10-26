using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LFSDriftTools
{
    public enum AppLanguage { English, Polish, Turkish, German, Spanish }

    public static class Localization
    {
        private const string EmbeddedEnglishResource = "LFSDriftTools.Lang.en.json";

        private static readonly object Sync = new();
        private static readonly Dictionary<AppLanguage, Dictionary<string, string>> Loaded = new();
        private static volatile Dictionary<string, string> _current = new();
        private static volatile Dictionary<string, string> _english = new();
        private static bool _initialized;

        public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;

        public static event Action? LanguageChanged;

        public static string LangDirectory =>
            Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Lang");

        public static void SetLanguage(AppLanguage lang)
        {
            EnsureInitialized();
            if (CurrentLanguage == lang) return;
            _current = GetLanguage(lang);
            CurrentLanguage = lang;
            LanguageChanged?.Invoke();
        }

        public static string LanguageCode(AppLanguage lang) => lang switch
        {
            AppLanguage.Polish => "pl",
            AppLanguage.Turkish => "tr",
            AppLanguage.German => "de",
            AppLanguage.Spanish => "es",
            _ => "en",
        };

        public static string LanguageDisplayName(AppLanguage lang) => lang switch
        {
            AppLanguage.English => "English",
            AppLanguage.Polish => "Polski",
            AppLanguage.Turkish => "Türkçe",
            AppLanguage.German => "Deutsch",
            AppLanguage.Spanish => "Español",
            _ => lang.ToString()
        };

        public static string T(string key)
        {
            EnsureInitialized();

            if (_current.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;

            if (_english.TryGetValue(key, out var en))
                return en;

#if DEBUG
            return "[" + key + "]";
#else
            return key;
#endif
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (Sync)
            {
                if (_initialized) return;
                _english = GetLanguage(AppLanguage.English);
                _current = GetLanguage(CurrentLanguage);
                _initialized = true;
            }
        }

        private static Dictionary<string, string> GetLanguage(AppLanguage lang)
        {
            lock (Sync)
            {
                if (Loaded.TryGetValue(lang, out var cached))
                    return cached;

                var strings = ReadLanguageFile(Path.Combine(LangDirectory, LanguageCode(lang) + ".json"));
                if (lang == AppLanguage.English)
                {
                    var embedded = ReadEmbeddedEnglish();
                    foreach (var pair in embedded)
                    {
                        if (!strings.TryGetValue(pair.Key, out var text) || string.IsNullOrEmpty(text))
                            strings[pair.Key] = pair.Value;
                    }
                }

                Loaded[lang] = strings;
                return strings;
            }
        }

        private static Dictionary<string, string> ReadLanguageFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    return Parse(stream);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Language file " + Path.GetFileName(path), ex);
            }
            return new Dictionary<string, string>();
        }

        private static Dictionary<string, string> ReadEmbeddedEnglish()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedEnglishResource);
                if (stream != null)
                    return Parse(stream);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Embedded English texts", ex);
            }
            return new Dictionary<string, string>();
        }

        private static Dictionary<string, string> Parse(Stream stream) =>
            JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();

        public static List<string> Validate()
        {
            var problems = new List<string>();
            var english = ReadEmbeddedEnglish();
            var placeholder = new Regex(@"\{\d+\}");

            HashSet<string> Placeholders(string text)
            {
                var found = new HashSet<string>();
                foreach (Match m in placeholder.Matches(text))
                    found.Add(m.Value);
                return found;
            }

            foreach (AppLanguage lang in Enum.GetValues(typeof(AppLanguage)))
            {
                var strings = ReadLanguageFile(Path.Combine(LangDirectory, LanguageCode(lang) + ".json"));
                foreach (var pair in english)
                {
                    if (!strings.TryGetValue(pair.Key, out var text) || string.IsNullOrEmpty(text))
                    {
                        problems.Add(pair.Key + ": " + lang);
                        continue;
                    }
                    if (!Placeholders(pair.Value).SetEquals(Placeholders(text)))
                        problems.Add(pair.Key + ": {n} " + lang);
                }
            }
            return problems;
        }
    }
}
