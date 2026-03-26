using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SMBVB_GNSS
{
    /// <summary>
    /// SMBV100B TCP/SCPI 통신 클래스
    /// 
    /// 역할:
    ///   - TCP 연결/해제 (port 5025)
    ///   - SCPI 명령 송신 (SendAsync)
    ///   - SCPI 쿼리 송수신 (QueryAsync)
    ///   - GNSS 초기화 시퀀스 (InitGnssAsync)
    /// 
    /// 참고 매뉴얼:
    ///   - GNSS User Manual 1178.9403.02-05
    ///   - SMBV100B User Manual 1178.4460.02-15
    /// </summary>
    internal class SMBVTCP
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;

        // 일반 명령 간 최소 대기 시간
        private const int COMMAND_DELAY_MS = 50;

        public bool IsConnected =>
            _client != null && _client.Connected && _reader != null;

        // ════════════════════════════════════════════
        // 연결 / 해제
        // ════════════════════════════════════════════

        public async Task ConnectAsync(string ip, int port, int timeoutMs = 3000)
        {
            _client = new TcpClient();
            _client.ReceiveTimeout = timeoutMs;
            _client.SendTimeout = timeoutMs;

            await _client.ConnectAsync(ip, port);

            _stream = _client.GetStream();
            _reader = new StreamReader(_stream, Encoding.ASCII);
            _writer = new StreamWriter(_stream, Encoding.ASCII)
            {
                AutoFlush = true
            };
        }

        public void Disconnect()
        {
            try
            {
                _writer?.Close();
                _reader?.Close();
                _stream?.Close();
                _client?.Close();
            }
            catch { }
            finally
            {
                _writer = null;
                _reader = null;
                _stream = null;
                _client = null;
            }
        }

        // ════════════════════════════════════════════
        // 송신 / 쿼리
        // ════════════════════════════════════════════

        /// <summary>SCPI 명령 전송 (응답 없음)</summary>
        public async Task SendAsync(string command)
        {
            if (!IsConnected)
                throw new InvalidOperationException("장비가 연결되지 않았습니다.");

            await _writer.WriteLineAsync(command);
            await Task.Delay(COMMAND_DELAY_MS);
        }

        /// <summary>SCPI 쿼리 전송 + 응답 수신 (?로 끝나는 명령)</summary>
        public async Task<string> QueryAsync(string command)
        {
            await SendAsync(command);
            string response = await _reader.ReadLineAsync();
            return response?.Trim() ?? string.Empty;
        }

        // ════════════════════════════════════════════
        // 공통 명령
        // ════════════════════════════════════════════

        /// <summary>장비 식별 정보 조회</summary>
        public async Task<string> GetIdentityAsync()
            => await QueryAsync("*IDN?");

        /// <summary>설치된 옵션 조회</summary>
        public async Task<string> GetOptionsAsync()
            => await QueryAsync("*OPT?");

        /// <summary>장비 전체 초기화 (3초 대기)</summary>
        public async Task ResetAsync()
        {
            await SendAsync("*RST");
            await Task.Delay(3000);
        }

        /// <summary>에러 큐 초기화</summary>
        public async Task ClearStatusAsync()
            => await SendAsync("*CLS");

        /// <summary>Remote → Local 전환. HIL UDP 수신 전 필수 (매뉴얼 p.247)</summary>
        public async Task GoToLocalAsync()
            => await SendAsync("&GTL");

        // ════════════════════════════════════════════
        // GNSS 초기화 시퀀스
        // ════════════════════════════════════════════
        //
        // 매뉴얼 p.276 (Figure 18-1: General workflow) 기반 순서:
        //
        //  Step 1: PRESet          → GNSS 파라미터 초기화 (p.282)
        //  Step 3: TMODe           → 테스트 모드 설정 (p.283)
        //          ⚠ 반드시 위치 설정 전에! (p.32: 모드 전환 시 위성 파라미터 초기화)
        //  Step 5: POSition        → 수신기 타입 설정 (p.305)
        //          LOCation        → 좌표 설정 (p.303, 306-307)
        //  Step 17: STATe 1        → 시뮬레이션 시작 (p.282)
        //           OUTPut1:STATe  → RF 출력 ON (메인 매뉴얼 p.742)
        //
        // mode 파라미터 값: "STAT" | "MOV" | "HIL"
        //   → 매뉴얼 p.305: POSition 값은 STAT | MOV | HIL
        //   → 프론트패널 표시는 "Static" / "Moving" / "Remote Control (HIL)"
        //   → SCPI에서 "REM"이 아니라 "HIL"임에 주의
        //

        public async Task InitGnssAsync(
            string mode,        // "STAT" | "MOV" | "HIL"
            double lat,
            double lon,
            double alt,
            int udpPort = 7755,
            double latency = 0.02)
        {
            // 1. 장비 초기화
            await ResetAsync();             // *RST + 3초 대기
            await ClearStatusAsync();       // *CLS

            // 2. GNSS 파라미터 초기화 (p.282)
            await SendAsync(":SOURce1:BB:GNSS:PRESet");
            await Task.Delay(1000);

            // 3. 테스트 모드 설정 (p.283)
            //    ⚠ 반드시 위치 설정 전에! (p.32)
            await SendAsync(":SOURce1:BB:GNSS:TMODe NAV");

            // 4. 수신기 타입 설정 (p.305)
            await SendAsync($":SOURce1:BB:GNSS:RECeiver:V1:POSition {mode}");

            // 5. 좌표 설정 (p.303 예제, p.306-307)
            //    LOCation:SELect → RFRame → FORMat → 좌표 순서
            await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:LOCation:SELect \"User Defined\"");
            await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:RFRame WGS84");
            await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:FORMat DEC");
            //    주의: 순서가 경도(Lon), 위도(Lat), 고도(Alt) (매뉴얼 p.307)
            await SendAsync(
                $":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:DEC:WGS" +
                $" {lon},{lat},{alt}");

            // 6. HIL 모드 추가 설정 (p.251-256)
            if (mode == "HIL")
            {
                await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:HIL:ITYPe UDP");
                await SendAsync($":SOURce1:BB:GNSS:RECeiver:V1:HIL:PORT {udpPort}");
                await SendAsync(
                    $":SOURce1:BB:GNSS:RECeiver:V1:HIL:SLATency {latency:F3}");
            }

            // 7. GNSS 시뮬레이션 시작 (p.282)
            await SendAsync(":SOURce1:BB:GNSS:STATe 1");
            await Task.Delay(2000);

            // 8. RF 출력 ON (메인 매뉴얼 p.742)
            await SendAsync(":OUTPut1:STATe 1");

            // 9. HIL 모드일 때만 &GTL (p.247)
            if (mode == "HIL")
            {
                await GoToLocalAsync();
            }
        }

        // ════════════════════════════════════════════
        // GNSS 중지
        // ════════════════════════════════════════════

        public async Task StopGnssAsync()
        {
            await SendAsync(":SOURce1:BB:GNSS:STATe 0");
            await SendAsync(":OUTPut1:STATe 0");
        }

        // ════════════════════════════════════════════
        // 모니터링 쿼리
        // ════════════════════════════════════════════

        /// <summary>설정 확인: "L1 / GPS only" 등 (p.283)</summary>
        public async Task<string> GetSimInfoAsync()
            => await QueryAsync(":SOURce1:BB:GNSS:SIMulation:INFO?");

        /// <summary>PDOP 조회. 5 미만이면 양호 (p.514)</summary>
        public async Task<double> GetPdopAsync()
        {
            string response = await QueryAsync(":SOURce1:BB:GNSS:RT:PDOP?");
            return double.TryParse(response, out double pdop) ? pdop : 99.0;
        }

        /// <summary>시뮬레이션 경과 시간. HIL 캘리브레이션용 (p.264)</summary>
        public async Task<double> GetHwTimeAsync()
        {
            string response = await QueryAsync(":SOURce1:BB:GNSS:RT:HWTime?");
            return double.TryParse(response, out double t) ? t : 0.0;
        }

        /// <summary>HIL 레이턴시 통계 (p.256)</summary>
        public async Task<string> GetHilLatencyStatsAsync()
            => await QueryAsync(
                ":SOURce1:BB:GNSS:RT:RECeiver:V1:HILPosition:LATency:STATistics?");
    }
}
