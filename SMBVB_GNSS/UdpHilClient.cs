using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SMBVB_GNSS
{
    /// <summary>
    /// HIL 모드 UDP 전송 클라이언트
    /// 
    /// 동작 원리:
    ///   1. CsvRouteReader에서 경로 포인트를 한 줄씩 읽는다
    ///   2. WGS84 좌표를 ECEF로 변환한다 (CoordConverter)
    ///   3. 216바이트 패킷을 만든다 (HilPacket)
    ///   4. UDP로 SMBV100B에 전송한다
    ///   5. Stopwatch로 정확히 100ms 주기를 유지한다
    ///   6. CSV 끝까지 다 읽으면 자동 종료
    /// 
    /// 매뉴얼 참고:
    ///   p.245: Tips for Best Results
    ///   p.246: 시스템 레이턴시 20ms → 전송주기 최소 20ms
    ///   p.247: Mode A (ECEF) — UDP 전송 가능한 유일한 모드
    ///   p.248: command jitter 보상 → 일정 주기 유지 필수
    /// </summary>
    internal class UdpHilClient : IDisposable
    {
        private UdpClient _udp;
        private readonly string _ip;
        private readonly int _port;
        private readonly IPEndPoint _endpoint;

        // 통계
        private long _packetCount;
        private readonly Stopwatch _totalStopwatch = new Stopwatch();

        // ── 이벤트 ─────────────────────────────────
        /// <summary>
        /// 패킷 전송할 때마다 호출
        /// (패킷번호, 총경과ms, 현재위도, 현재경도, 현재고도, 남은포인트수)
        /// </summary>
        public event Action<long, double, double, double, double, int> OnPacketSent;

        /// <summary>에러 발생 시 호출</summary>
        public event Action<string> OnError;

        /// <summary>경로 재생 완료 시 호출</summary>
        public event Action<long> OnRouteFinished;

        // ════════════════════════════════════════════
        // 생성자
        // ════════════════════════════════════════════
        public UdpHilClient(string ip, int port)
        {
            _ip = ip;
            _port = port;
            _endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
        }

        // ════════════════════════════════════════════
        // CSV 경로 재생 루프
        // ════════════════════════════════════════════
        /// <summary>
        /// CSV 경로 파일을 읽으면서 UDP 패킷을 전송한다.
        /// 
        /// 흐름:
        ///   CSV 한 줄 읽기 → WGS84→ECEF 변환 → 216B 패킷 → UDP 전송
        ///   → 100ms 대기 → 다음 줄 → ... → CSV 끝 → 자동 종료
        /// 
        /// ⚠ Task.Delay(intervalMs)만 쓰면 안 됨!
        ///   패킷 생성/전송 시간을 빼고 남은 시간만 대기해야
        ///   주기가 정확해지고 위치 Jump가 안 생김 (매뉴얼 p.248)
        /// </summary>
        /// <param name="route">로드된 CSV 경로 데이터</param>
        /// <param name="intervalMs">전송 주기 (ms). 최소 20, 권장 100</param>
        /// <param name="cancelToken">취소 토큰 (HIL Stop 버튼)</param>
        public async Task StartAsync(
            CsvRouteReader route,
            int intervalMs,
            CancellationToken cancelToken)
        {
            if (intervalMs < 20)
                throw new ArgumentException(
                    "전송 주기는 최소 20ms (시스템 레이턴시 제한, 매뉴얼 p.246)");

            if (route == null || route.Count == 0)
                throw new ArgumentException("경로 데이터가 없습니다.");

            _udp = new UdpClient();
            _packetCount = 0;
            _totalStopwatch.Restart();
            route.ResetIndex();  // 처음부터 시작

            var loopWatch = new Stopwatch();

            try
            {
                // ── CSV 끝까지 반복 ──────────────────
                while (!route.IsFinished && !cancelToken.IsCancellationRequested)
                {
                    loopWatch.Restart();
                    // ① CSV에서 다음 포인트 읽기
                    var pt = route.GetNext();
                    // ② WGS84 → ECEF 변환
                    //    SMBV100B HIL UDP는 ECEF만 받음 (매뉴얼 p.247: Mode A)
                    CoordConverter.ToECEF(
                        pt.Latitude, pt.Longitude, pt.Altitude,
                        out double x, out double y, out double z);
                    // ③ 경과 시간 (CSV의 Time 컬럼 사용)
                    double elapsedSec = pt.Time;
                    // ④ 216바이트 패킷 생성
                    //    Big Endian IEEE754 (매뉴얼 p.247)
                    byte[] packet = HilPacket.Build(
                        x, y, z,           // ECEF 위치 [m]
                        0, 0, 0,           // 속도 (CSV에 없으면 0)
                        0, 0, 0,           // 자세 (yaw, pitch, roll)
                        elapsedSec);       // 타임스탬프

                    // ⑤ UDP 전송
                    //    SMBV100B의 HIL:PORT (기본 7755)로 전송
                    await _udp.SendAsync(packet, packet.Length, _endpoint);

                    // ⑥ 통계 업데이트 + UI 이벤트
                    _packetCount++;
                    int remaining = route.Count - route.CurrentIndex;
                    OnPacketSent?.Invoke(
                        _packetCount,
                        _totalStopwatch.Elapsed.TotalMilliseconds,
                        pt.Latitude, pt.Longitude, pt.Altitude,
                        remaining);

                    // ⑦ 정확한 주기 유지 (Stopwatch 기반)
                    //    패킷 생성/변환/전송에 걸린 시간을 빼고 남은 시간만 대기
                    //    예: intervalMs=100, 처리시간=3ms → 97ms만 대기
                    int elapsed = (int)loopWatch.ElapsedMilliseconds;
                    int delay = intervalMs - elapsed;
                    if (delay > 0)
                        await Task.Delay(delay, cancelToken);
                }

                // ── 정상 종료 (CSV 끝 도달) ──────────
                OnRouteFinished?.Invoke(_packetCount);
            }
            catch (OperationCanceledException)
            {
                // 사용자가 HIL Stop 버튼 누름 → 정상 취소
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"UDP 전송 오류: {ex.Message}");
            }
            finally
            {
                _totalStopwatch.Stop();
                _udp?.Close();
                _udp = null;
            }
        }

        // ════════════════════════════════════════════
        // 통계
        // ════════════════════════════════════════════
        public long PacketCount => _packetCount;
        public double ElapsedSeconds => _totalStopwatch.Elapsed.TotalSeconds;

        // ════════════════════════════════════════════
        // Dispose
        // ════════════════════════════════════════════
        public void Dispose()
        {
            _udp?.Close();
            _udp = null;
        }
    }
}
