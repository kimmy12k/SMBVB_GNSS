using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SMBVB_GNSS
{
    /// <summary>
    /// INI 파일 읽기/쓰기 유틸리티
    ///
    /// 지원 형식:
    ///   [섹션명]
    ///   키=값        ; 인라인 주석
    ///   # 줄 주석
    ///   ; 줄 주석
    /// </summary>
    internal class IniParser
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data
            = new(StringComparer.OrdinalIgnoreCase);

        private string _filePath = string.Empty;

        private IniParser() { }

        // ════════════════════════════════════════════
        // 로드
        // ════════════════════════════════════════════

        public static IniParser Load(string filePath)
        {
            var parser = new IniParser();
            parser._filePath = filePath;

            if (!File.Exists(filePath))
                return parser;

            string currentSection = string.Empty;

            foreach (string rawLine in File.ReadLines(filePath, Encoding.UTF8))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrEmpty(line) ||
                    line.StartsWith(";") ||
                    line.StartsWith("#"))
                    continue;

                if (line.StartsWith("[") && line.Contains("]"))
                {
                    int end = line.IndexOf("]");
                    currentSection = line.Substring(1, end - 1).Trim();

                    if (!parser._data.ContainsKey(currentSection))
                        parser._data[currentSection] =
                            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq < 1) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1);

                int commentIdx = IndexOfInlineComment(val);
                if (commentIdx >= 0)
                    val = val.Substring(0, commentIdx);

                val = val.Trim();

                if (string.IsNullOrEmpty(key)) continue;

                if (!parser._data.ContainsKey(currentSection))
                    parser._data[currentSection] =
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                parser._data[currentSection][key] = val;
            }

            return parser;
        }

        // ════════════════════════════════════════════
        // 저장
        // ════════════════════════════════════════════

        public void Save() => Save(_filePath);

        public void Save(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; SMBV100B GNSS Control 설정 파일");
            sb.AppendLine($"; 저장 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            foreach (var section in _data)
            {
                sb.AppendLine($"[{section.Key}]");
                foreach (var kv in section.Value)
                    sb.AppendLine($"{kv.Key}={kv.Value}");
                sb.AppendLine();
            }

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            _filePath = filePath;
        }

        // ════════════════════════════════════════════
        // Get
        // ════════════════════════════════════════════

        public string Get(string section, string key, string defaultValue = "")
        {
            if (_data.TryGetValue(section, out var sec) &&
                sec.TryGetValue(key, out var val))
                return val;
            return defaultValue;
        }

        public int GetInt(string section, string key, int defaultValue = 0)
        {
            string raw = Get(section, key);
            return int.TryParse(raw, out int result) ? result : defaultValue;
        }

        public double GetDouble(string section, string key, double defaultValue = 0.0)
        {
            string raw = Get(section, key);
            return double.TryParse(raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double result) ? result : defaultValue;
        }

        public bool GetBool(string section, string key, bool defaultValue = false)
        {
            string raw = Get(section, key).ToLower();
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            return raw == "true" || raw == "1" || raw == "yes";
        }

        // ════════════════════════════════════════════
        // Set
        // ════════════════════════════════════════════

        public void Set(string section, string key, string value)
        {
            if (!_data.ContainsKey(section))
                _data[section] =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _data[section][key] = value ?? string.Empty;
        }

        public void Set(string section, string key, int value)
            => Set(section, key, value.ToString());

        public void Set(string section, string key, double value)
            => Set(section, key,
                value.ToString("G", System.Globalization.CultureInfo.InvariantCulture));

        public void Set(string section, string key, bool value)
            => Set(section, key, value ? "true" : "false");

        // ════════════════════════════════════════════
        // 기타
        // ════════════════════════════════════════════

        public bool HasSection(string section) => _data.ContainsKey(section);

        public bool HasKey(string section, string key)
            => _data.TryGetValue(section, out var sec) && sec.ContainsKey(key);

        public IEnumerable<string> GetKeys(string section)
        {
            if (_data.TryGetValue(section, out var sec))
                return sec.Keys;
            return Array.Empty<string>();
        }

        private static int IndexOfInlineComment(string s)
        {
            bool inQuote = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '"') { inQuote = !inQuote; continue; }
                if (!inQuote && (s[i] == ';' || s[i] == '#'))
                    return i;
            }
            return -1;
        }
    }
}
