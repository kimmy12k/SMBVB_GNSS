using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SMBV100B_GNSS
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
        // ── 내부 구조: 섹션 → (키 → 값) ─────────────────
        private readonly Dictionary<string, Dictionary<string, string>> _data
            = new(StringComparer.OrdinalIgnoreCase);

        private string _filePath = string.Empty;

        // 외부에서 new IniParser() 생성 금지
        // 반드시 IniParser.Load() 로만 생성
        private IniParser() { }

        // ════════════════════════════════════════════════
        // 로드
        // ════════════════════════════════════════════════

        /// <summary>INI 파일 로드. 파일 없으면 빈 인스턴스 반환</summary>
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

                // 빈 줄 / 주석 건너뜀
                if (string.IsNullOrEmpty(line) ||
                    line.StartsWith(";") ||
                    line.StartsWith("#"))
                    continue;

                // 섹션
                if (line.StartsWith("[") && line.Contains("]"))
                {
                    int end = line.IndexOf("]");
                    currentSection = line.Substring(1, end - 1).Trim();

                    if (!parser._data.ContainsKey(currentSection))
                        parser._data[currentSection] =
                            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    continue;
                }

                // 키=값
                int eq = line.IndexOf('=');
                if (eq < 1) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1);

                // 인라인 주석 제거 (; 또는 # 이후)
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

        // ════════════════════════════════════════════════
        // 저장
        // ════════════════════════════════════════════════

        /// <summary>현재 경로에 저장</summary>
        public void Save() => Save(_filePath);

        /// <summary>지정 경로에 저장</summary>
        public void Save(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; SMBV100B GNSS Logger 설정 파일");
            sb.AppendLine($"; 저장 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            foreach (var section in _data)
            {
                sb.AppendLine($"[{section.Key}]");
                foreach (var kv in section.Value)
                    sb.AppendLine($"{kv.Key}={kv.Value}");
                sb.AppendLine();
            }

            // 디렉터리 없으면 생성
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            _filePath = filePath;
        }

        // ════════════════════════════════════════════════
        // Get — 읽기
        // ════════════════════════════════════════════════

        /// <summary>문자열 값 반환. 없으면 defaultValue</summary>
        public string Get(string section, string key, string defaultValue = "")
        {
            if (_data.TryGetValue(section, out var sec) &&
                sec.TryGetValue(key, out var val))
                return val;

            return defaultValue;
        }

        /// <summary>int 값 반환. 파싱 실패 시 defaultValue</summary>
        public int GetInt(string section, string key, int defaultValue = 0)
        {
            string raw = Get(section, key);
            return int.TryParse(raw, out int result) ? result : defaultValue;
        }

        /// <summary>double 값 반환. 파싱 실패 시 defaultValue</summary>
        public double GetDouble(string section, string key, double defaultValue = 0.0)
        {
            string raw = Get(section, key);
            return double.TryParse(raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double result) ? result : defaultValue;
        }

        /// <summary>bool 값 반환. true/1/yes → true, 나머지 → false</summary>
        public bool GetBool(string section, string key, bool defaultValue = false)
        {
            string raw = Get(section, key).ToLower();
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            return raw == "true" || raw == "1" || raw == "yes";
        }

        // ════════════════════════════════════════════════
        // Set — 쓰기
        // ════════════════════════════════════════════════

        /// <summary>값 설정. 섹션/키 없으면 자동 생성</summary>
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

        // ════════════════════════════════════════════════
        // 기타
        // ════════════════════════════════════════════════

        /// <summary>섹션 존재 여부</summary>
        public bool HasSection(string section)
            => _data.ContainsKey(section);

        /// <summary>키 존재 여부</summary>
        public bool HasKey(string section, string key)
            => _data.TryGetValue(section, out var sec) && sec.ContainsKey(key);

        /// <summary>섹션의 모든 키 목록</summary>
        public IEnumerable<string> GetKeys(string section)
        {
            if (_data.TryGetValue(section, out var sec))
                return sec.Keys;
            return Array.Empty<string>();
        }

        // ── 인라인 주석 위치 찾기 (따옴표 밖의 ; 또는 #) ─
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
