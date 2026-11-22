using UnityEngine;

namespace Feeder.MCP.Editor.Persistence
{
    /// <summary>
    /// Small typed wrappers used by the package's editor UI. Keys intentionally match the
    /// former preferences-package format so upgrading from an embedded FeederBase copy keeps settings.
    /// </summary>
    public struct PlayerPrefsBool
    {
        private readonly string _internalKey;
        private readonly bool _defaultValue;

        public PlayerPrefsBool(string key, bool defaultValue = false)
        {
            _internalKey = $"Boolean:{key}";
            _defaultValue = defaultValue;
        }

        public bool Value
        {
            get => PlayerPrefs.GetInt(_internalKey, _defaultValue ? 1 : 0) == 1;
            set => PlayerPrefs.SetInt(_internalKey, value ? 1 : 0);
        }
    }

    public struct PlayerPrefsInt
    {
        private readonly string _internalKey;
        private readonly int _defaultValue;

        public PlayerPrefsInt(string key, int defaultValue = 0)
        {
            _internalKey = $"Int32:{key}";
            _defaultValue = defaultValue;
        }

        public int Value
        {
            get => PlayerPrefs.GetInt(_internalKey, _defaultValue);
            set => PlayerPrefs.SetInt(_internalKey, value);
        }
    }

    public struct PlayerPrefsString
    {
        private readonly string _internalKey;
        private readonly string _defaultValue;

        public PlayerPrefsString(string key, string defaultValue = "")
        {
            _internalKey = $"String:{key}";
            _defaultValue = defaultValue;
        }

        public string Value
        {
            get => PlayerPrefs.GetString(_internalKey, _defaultValue);
            set => PlayerPrefs.SetString(_internalKey, value);
        }
    }
}
