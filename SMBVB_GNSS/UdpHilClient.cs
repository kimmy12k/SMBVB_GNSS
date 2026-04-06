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
    /// ★ ElapsedTime 동기화 원리:
    /// 
    ///   장비는 시뮬레이션 시작 후 자체 시계(HWTime)가 돌고 있음.
    ///   UDP 패킷의 ElapsedTime이 현재 HWTime보다 과거이면 "too old"로 무시됨.
    ///   
    ///   해결:
    ///     1. Form1에서 HWTime을 읽은 직후 Stopwatch 시작
    ///     2. ElapsedTime = HWTime(읽은 시점) + Stopwatch 경과시간
    ///     3. 이러면 TCP 닫기/대기 시간이 자동으로 보정됨
    ///   
    ///   타임라인:
    ///     [HWTime 읽기] → [&GTL] → [TCP 닫기] → [1초 대기] → [UDP 시작]
    ///     t=0              t=0.2    t=0.3        t=1.3         t=1.3
    ///     HWTime=50        ↓        ↓             ↓             ↓
    ///                     Stopwatch가 여기서부터 시간을 셈
    ///                     ElapsedTime = 50 + 1.3 = 51.3 (현재 장비 시간과 일치!)
    /// </summary>
    internal class UdpHilClient : IDisposable
    {
        private UdpClient _udp;
        private readonly string _targetIp;
        private readonly int _port;
        private readonly string _localIp;
        private readonly IPEndPoint _endpoint;

        private long _packetCount;
        private readonly Stopwatch _totalStopwatch = new Stopwatch();

        public event Action<long, double, double, double, double, int> OnPacketSent;
        public event Action<string> OnError;
        public event Action<long> OnRouteFinished;

        public UdpHilClient(string targetIp, int port, string localIp = "")
        {
            _targetIp = targetIp;
            _port = port;
            _localIp = localIp;
            _endpoint = new IPEndPoint(IPAddress.Parse(targetIp), port);
        }

        /// <summary>
        /// CSV 경로 재생 + UDP 전송 루프
        /// </summary>
        /// <param name="route">CSV 경로 데이터</param>
        /// <param name="intervalMs">전송 주기 (ms)</param>
        /// <param name="hwTimeOffset">HWTime 읽은 시점의 값</param>
        /// <param name="syncWatch">HWTime 읽은 직후 시작된 Stopwatch</param>
        /// <param name="cancelToken">취소 토큰</param>
        public async Task StartAsync(
            CsvRouteReader route,
            int intervalMs,
            double hwTimeOffset,
            Stopwatch syncWatch,
            CancellationToken cancelToken)
        {
            if (intervalMs < 20)
                throw new ArgumentException("전송 주기는 최소 20ms");

            if (route == null || route.Count == 0)
                throw new ArgumentException("경로 데이터가 없습니다.");

            // 로컬 IP 바인딩
            try
            {
                if (!string.IsNullOrEmpty(_localIp))
                    _udp = new UdpClient(
                        new IPEndPoint(IPAddress.Parse(_localIp), 0));
                else
                    _udp = new UdpClient();
            }
            catch
            {
                _udp = new UdpClient();  // 바인딩 실패 시 자동 라우팅
            }

            _packetCount = 0;
            _totalStopwatch.Restart();
            route.ResetIndex();

            var loopWatch = new Stopwatch();

            try
            {
                while (!route.IsFinished && !cancelToken.IsCancellationRequested)
                {
                    loopWatch.Restart();

                    // ① CSV에서 다음 좌표 읽기
                    var pt = route.GetNext();

                    // ② WGS84 → ECEF 변환
                    CoordConverter.ToECEF(
                        pt.Latitude, pt.Longitude, pt.Altitude,
                        out double x, out double y, out double z);

                    // ③ ★ ElapsedTime 계산 (핵심!)
                    //    HWTime(읽은 시점) + syncWatch 경과시간
                    //    syncWatch는 HWTime 읽은 직후 시작됨 → 자동 보정
                    //    pt.Time은 사용하지 않음 (장비 시간과 무관)
                    double elapsedSec = hwTimeOffset
                                      + syncWatch.Elapsed.TotalSeconds;

                    // ④ 216바이트 패킷 생성 (16바이트 예약 포함)
                    byte[] packet = HilPacket.Build(
                        x, y, z,
                        0, 0, 0,
                        0, 0, 0,
                        elapsedSec);

                    // ⑤ UDP 전송
                    await _udp.SendAsync(packet, packet.Length, _endpoint);

                    // ⑥ 통계 + 이벤트 (매번 호출 — 1Hz라 부하 없음)
                    _packetCount++;
                    int remaining = route.Count - route.CurrentIndex;

                    OnPacketSent?.Invoke(
                        _packetCount,
                        _totalStopwatch.Elapsed.TotalMilliseconds,
                        pt.Latitude, pt.Longitude, pt.Altitude,
                        remaining);

                    // ⑦ 정확한 주기 유지
                    int elapsed = (int)loopWatch.ElapsedMilliseconds;
                    int delay = intervalMs - elapsed;
                    if (delay > 0)
                        await Task.Delay(delay, cancelToken);
                }

                OnRouteFinished?.Invoke(_packetCount);
            }
            catch (OperationCanceledException) { }
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

        public long PacketCount => _packetCount;
        public double ElapsedSeconds => _totalStopwatch.Elapsed.TotalSeconds;

        public void Dispose()
        {
            _udp?.Close();
            _udp = null;
        }
    }
}
