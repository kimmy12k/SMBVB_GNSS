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
    /// 변경 이력:
    ///   - 로컬 IP 바인딩 추가 (169.254.x.x 대역 라우팅 문제 해결)
    ///   - HWTime 오프셋 추가 (장비 시뮬레이션 시간 기준 전송)
    /// 
    /// 핵심 원리:
    ///   장비는 시뮬레이션 시작 후 자체 시계(HWTime)가 돌고 있음.
    ///   UDP 패킷의 ElapsedTime이 HWTime보다 과거이면 무시됨.
    ///   따라서 ElapsedTime = HWTime + CSV의 Time 으로 보내야 함.
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

        // ── 이벤트 ──
        public event Action<long, double, double, double, double, int> OnPacketSent;
        public event Action<string> OnError;
        public event Action<long> OnRouteFinished;

        /// <summary>
        /// UDP HIL 클라이언트 생성
        /// </summary>
        /// <param name="targetIp">장비 IP (169.254.2.20)</param>
        /// <param name="port">UDP 포트 (7755)</param>
        /// <param name="localIp">PC의 로컬 IP (169.254.2.21). 
        /// 빈 문자열이면 자동 라우팅.</param>
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
        /// <param name="hwTimeOffset">장비의 HWTime 값. 
        /// ElapsedTime = hwTimeOffset + CSV의 Time</param>
        /// <param name="cancelToken">취소 토큰</param>
        public async Task StartAsync(
            CsvRouteReader route,
            int intervalMs,
            double hwTimeOffset,
            CancellationToken cancelToken)
        {
            if (intervalMs < 20)
                throw new ArgumentException(
                    "전송 주기는 최소 20ms (매뉴얼 p.246)");

            if (route == null || route.Count == 0)
                throw new ArgumentException("경로 데이터가 없습니다.");

            // 로컬 IP 바인딩 (link-local 대역 라우팅 문제 해결)
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
                _udp = new UdpClient();  // 바인딩 실패하면 자동 라우팅
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

                    // ③ ElapsedTime = HWTime + CSV의 Time
                    //    장비의 시뮬레이션 시간 기준으로 보내야 인식됨
                    double elapsedSec = hwTimeOffset + _totalStopwatch.Elapsed.TotalSeconds;

                    // ④ 216바이트 패킷 생성 (16바이트 예약 포함)
                    byte[] packet = HilPacket.Build(
                        x, y, z,
                        0, 0, 0,
                        0, 0, 0,
                        elapsedSec);

                    // ⑤ UDP 전송
                    await _udp.SendAsync(packet, packet.Length, _endpoint);

                    // ⑥ 통계 + 이벤트
                    _packetCount++;
                    int remaining = route.Count - route.CurrentIndex;

                    if (true)
                    {
                        OnPacketSent?.Invoke(
                            _packetCount,
                            _totalStopwatch.Elapsed.TotalMilliseconds,
                            pt.Latitude, pt.Longitude, pt.Altitude,
                            remaining);
                    }

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
