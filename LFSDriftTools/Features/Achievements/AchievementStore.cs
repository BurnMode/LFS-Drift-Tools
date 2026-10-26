using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LFSDriftTools
{
    public sealed class AchievementUnlockRecord
    {
        public DateTime Utc { get; set; }
        public string Car { get; set; } = "";
        public string Track { get; set; } = "";
    }

    public sealed class AchievementSaveData
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Dictionary<string, AchievementUnlockRecord>> Unlocked { get; set; } = new();
        public Dictionary<string, Dictionary<string, long>> Counters { get; set; } = new();
    }

    public sealed class AchievementStore
    {
        private readonly string _path;
        private readonly object _sync = new();
        private AchievementSaveData _data = new();
        private bool _dirty;
        private bool _saveBlocked;

        public AchievementStore(string path)
        {
            _path = path;
        }

        private const string Identity = "achievements";

        public SecureLoadResult LastLoad { get; private set; }

        public void Load()
        {
            lock (_sync)
            {
                _saveBlocked = false;
                LastLoad = default;
                SecureFile.ArchiveLegacy(Path.ChangeExtension(_path, ".json"));
                try
                {
                    LastLoad = SecureFile.Load<AchievementSaveData>(_path, SecureFileKind.Achievements, Identity, IsPlausible, out var data);
                    _data = data ?? new AchievementSaveData();
                    _saveBlocked = LastLoad.SaveBlocked;
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Achievements load", ex);
                    _data = new AchievementSaveData();
                    _saveBlocked = File.Exists(_path);
                }
                _dirty = false;
            }
        }

        private static bool IsPlausible(AchievementSaveData data)
        {
            if (data.Unlocked == null || data.Counters == null) return false;
            foreach (var byMethod in data.Unlocked.Values)
            {
                if (byMethod == null) return false;
                foreach (var (method, record) in byMethod)
                    if (!IsKnownMethod(method) || record == null) return false;
            }
            foreach (var byMethod in data.Counters.Values)
            {
                if (byMethod == null) return false;
                foreach (var (method, value) in byMethod)
                    if (!IsKnownMethod(method) || value < 0) return false;
            }
            return true;
        }

        private static bool IsKnownMethod(string name) =>
            Enum.TryParse<InputMethod>(name, false, out var method) && method.ToString() == name;

        public void Save()
        {
            byte[] bytes;
            lock (_sync)
            {
                if (_saveBlocked) return;
                bytes = SecureFile.Protect(SecureFileKind.Achievements, Identity, JsonSerializer.Serialize(_data));
                _dirty = false;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AtomicFile.WriteAllBytes(_path, bytes);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Achievements save", ex);
                lock (_sync) _dirty = true;
            }
        }

        public void SaveIfDirty()
        {
            bool dirty;
            lock (_sync) dirty = _dirty;
            if (dirty) Save();
        }

        public bool IsUnlocked(string id, InputMethod method)
        {
            lock (_sync)
                return _data.Unlocked.TryGetValue(id, out var byMethod) && byMethod.ContainsKey(method.ToString());
        }

        public bool IsUnlockedAny(string id)
        {
            lock (_sync)
                return _data.Unlocked.TryGetValue(id, out var byMethod) && byMethod.Count > 0;
        }

        public bool TryUnlock(string id, InputMethod method, string car, string track)
        {
            lock (_sync)
            {
                if (!_data.Unlocked.TryGetValue(id, out var byMethod))
                {
                    byMethod = new Dictionary<string, AchievementUnlockRecord>();
                    _data.Unlocked[id] = byMethod;
                }
                string key = method.ToString();
                if (byMethod.ContainsKey(key)) return false;
                byMethod[key] = new AchievementUnlockRecord { Utc = DateTime.UtcNow, Car = car ?? "", Track = track ?? "" };
                _dirty = true;
                return true;
            }
        }

        public long AddCounter(string id, InputMethod method, long delta)
        {
            lock (_sync)
            {
                if (!_data.Counters.TryGetValue(id, out var byMethod))
                {
                    byMethod = new Dictionary<string, long>();
                    _data.Counters[id] = byMethod;
                }
                string key = method.ToString();
                byMethod.TryGetValue(key, out long value);
                value += delta;
                byMethod[key] = value;
                _dirty = true;
                return value;
            }
        }

        public long GetCounter(string id, InputMethod method)
        {
            lock (_sync)
                return _data.Counters.TryGetValue(id, out var byMethod) && byMethod.TryGetValue(method.ToString(), out long value) ? value : 0;
        }
    }
}
