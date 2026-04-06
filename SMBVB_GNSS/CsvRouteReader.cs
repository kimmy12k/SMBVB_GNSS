using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SMBVB_GNSS
{
    /// <summary>
    /// CSV 경로 파일 읽기
    /// 
    /// 역할:
    ///   CSV 파일에서 시간순으로 위치 데이터를 읽어서
    ///   UdpHilClient에 전달할 좌표 목록을 만든다.
    /// 
    /// CSV 형식 (첫 줄 헤더):
    ///   Time,Latitude,Longitude,Altitude
    ///   0.0,37.6584,126.8320,28
    ///   0.1,37.6585,126.8321,28
    ///   0.2,37.6587,126.8323,28
    ///   ...
    /// 
    /// Time: 초 단위 (시작 기준 경과 시간)
    /// Latitude: 위도 (degree)
    /// Longitude: 경도 (degree)
    /// Altitude: 고도 (meter)
    /// </summary>
    internal class CsvRouteReader
    {
        /// <summary>경로 한 지점의 데이터</summary>
        public struct RoutePoint
        {
            public double Time;       // 경과 시간 [s]
            public double Latitude;   // 위도 [deg]
            public double Longitude;  // 경도 [deg]
            public double Altitude;   // 고도 [m]
        }

        private readonly List<RoutePoint> _points = new List<RoutePoint>();
        private int _currentIndex = 0;

        /// <summary>총 경로 포인트 수</summary>
        public int Count => _points.Count;

        /// <summary>현재 인덱스</summary>
        public int CurrentIndex => _currentIndex;

        /// <summary>끝까지 읽었는지</summary>
        public bool IsFinished => _currentIndex >= _points.Count;

        /// <summary>파일 경로</summary>
        public string FilePath { get; private set; }

        // ════════════════════════════════════════════
        // CSV 파일 로드
        // ════════════════════════════════════════════

        /// <summary>
        /// CSV 파일을 읽어서 RoutePoint 목록으로 변환
        /// 
        /// 첫 줄은 헤더로 건너뜀
        /// 빈 줄, 파싱 실패 줄은 무시
        /// </summary>
        public void Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"경로 파일을 찾을 수 없습니다: {filePath}");

            FilePath = filePath;
            _points.Clear();
            _currentIndex = 0;

            string[] lines = File.ReadAllLines(filePath);

            // 첫 줄 헤더 건너뛰기
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] parts = line.Split(',');
                if (parts.Length < 4) continue;

                try
                {
                    var point = new RoutePoint
                    {
                        Time = double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture),
                        Latitude = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture),
                        Longitude = double.Parse(parts[2].Trim(), CultureInfo.InvariantCulture),
                        Altitude = double.Parse(parts[3].Trim(), CultureInfo.InvariantCulture),
                    };
                    _points.Add(point);
                }
                catch
                {
                    // 파싱 실패한 줄은 무시
                }
            }

            if (_points.Count == 0)
                throw new InvalidDataException("유효한 경로 데이터가 없습니다.");
        }

        // ════════════════════════════════════════════
        // 다음 포인트 가져오기
        // ════════════════════════════════════════════

        /// <summary>
        /// 다음 경로 포인트를 반환한다.
        /// 끝에 도달하면 마지막 포인트를 계속 반환한다.
        /// </summary>
        public RoutePoint GetNext()
        {
            if (_points.Count == 0)
                throw new InvalidOperationException("경로가 로드되지 않았습니다.");

            if (_currentIndex < _points.Count)
            {
                return _points[_currentIndex++];
            }

            // 끝에 도달: 마지막 포인트 유지
            return _points[_points.Count - 1];
        }

        /// <summary>
        /// 특정 인덱스의 포인트를 반환
        /// </summary>
        public RoutePoint GetAt(int index)
        {
            if (index < 0 || index >= _points.Count)
                throw new IndexOutOfRangeException($"인덱스 범위 초과: {index} (총 {_points.Count}개)");

            return _points[index];
        }

        /// <summary>처음으로 되돌리기</summary>
        public void ResetIndex()
        {
            _currentIndex = 0;
        }

        /// <summary>전체 경로의 총 시간 (초)</summary>
        public double TotalDuration//전체 경로가 몇 인지
        {
            get
            {
                if (_points.Count < 2) return 0;
                return _points[_points.Count - 1].Time - _points[0].Time;
            }
        }
    }
}
