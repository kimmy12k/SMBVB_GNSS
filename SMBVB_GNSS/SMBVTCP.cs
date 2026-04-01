using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SMBVB_GNSS
{
    /// <summary>
    /// SMBV100B TCP/SCPI 통신 클래스
    /// 
    /// 변경 이력:
    ///   - COMMAND_DELAY: 50→100ms (장비 처리 여유)
    ///   - *RST 대기: 3→5초 (DSP 초기화 충분 대기)
    ///   - PRESet 제거 (*RST와 중복, DSP 이중 부하)
    ///   - STATe 1 대기: 2→5초 (HIL 초기화 충분 대기)
    ///   - CheckErrorAsync 추가 (에러 상태에서 명령 누적 방지)
    ///   - SendHilPositionAsync 추가 (SCPI HIL 위치 전송)
    ///   - &GTL 제거 (SCPI HIL은 TCP 유지 → 불필요)
    /// </summary>
    internal class SMBVTCP
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;

        // 50→100ms: 장비가 이전 명령을 처리할 시간 확보
        // 50ms에서는 명령이 겹쳐서 무시될 수 있음
        private const int COMMAND_DELAY_MS = 100;

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

        public async Task SendAsync(string command)
        {
            if (!IsConnected)
                throw new InvalidOperationException("장비가 연결되지 않았습니다.");

            await _writer.WriteLineAsync(command);
            await Task.Delay(COMMAND_DELAY_MS);
        }

        public async Task<string> QueryAsync(string command)
        {
            await SendAsync(command);
            string response = await _reader.ReadLineAsync();
            return response?.Trim() ?? string.Empty;
        }

        // ════════════════════════════════════════════
        // 에러 체크 (신규)
        // ════════════════════════════════════════════
        //
        // DSP가 에러 상태인데 명령을 계속 보내면 FIFO가 넘침
        // → "FIFO is not empty" → 장비 재부팅 필요
        // 주요 명령 후 에러를 확인하고, 에러가 있으면 즉시 중단

        /// <summary>
        /// 에러 큐 확인. 에러가 있으면 예외를 던짐.
        /// </summary>
        public async Task CheckErrorAsync()
        {
            string err = await QueryAsync(":SYSTem:ERRor?");
            if (!err.StartsWith("0"))
                throw new Exception($"장비 에러: {err}");
        }

        /// <summary>
        /// 에러 큐 확인 (예외 없이 문자열 반환).
        /// 로그용.
        /// </summary>
        public async Task<string> GetErrorAsync()
        {
            return await QueryAsync(":SYSTem:ERRor?");
        }

        // ════════════════════════════════════════════
        // 공통 명령
        // ════════════════════════════════════════════

        public async Task<string> GetIdentityAsync()
            => await QueryAsync("*IDN?");

        public async Task<string> GetOptionsAsync()
            => await QueryAsync("*OPT?");

        /// <summary>
        /// 장비 전체 초기화.
        /// 3→5초 대기: DSP 초기화에 충분한 시간 확보.
        /// *RST 후 모든 설정이 공장 초기값으로 돌아감.
        /// </summary>
        public async Task ResetAsync()
        {
            await SendAsync("*RST");
            await Task.Delay(5000);  // 3→5초: DSP 초기화 충분 대기
        }

        public async Task ClearStatusAsync()
            => await SendAsync("*CLS");

        public async Task GoToLocalAsync()
            => await SendAsync("&GTL");

        // ════════════════════════════════════════════
        // GNSS 초기화 시퀀스 (최적화)
        // ════════════════════════════════════════════
        //
        // 변경 내역:
        //   - PRESet 제거: *RST가 이미 전체 초기화함
        //     PRESet을 추가하면 DSP가 두 번 초기화 → 부하
        //   - STATe 1 후 5초 대기: HIL 모드는 Static보다 초기화 오래 걸림
        //   - 에러 체크: *RST 후, STATe 1 후 에러 확인
        //   - &GTL 제거: SCPI HIL은 TCP 유지 → Local 전환 불필요
        //

        public async Task InitGnssAsync(
            string mode,
            double lat,
            double lon,
            double alt,
            int udpPort = 7755,
            double latency = 0.15)
        {
            // 1. 장비 초기화 (*RST만, PRESet 안 함)
            await ResetAsync();         // *RST + 5초 대기
            await ClearStatusAsync();   // *CLS

            // *RST 후 에러 확인 — 에러 있으면 여기서 중단
            await CheckErrorAsync();

            // 2. 추가 안정화 대기 (PRESet 제거한 대신)
            await Task.Delay(2000);

            // 3. 테스트 모드 설정 (반드시 좌표 전에!)
            await SendAsync(":SOURce1:BB:GNSS:TMODe NAV");

            // 4. 수신기 타입 설정
            await SendAsync($":SOURce1:BB:GNSS:RECeiver:V1:POSition {mode}");

            // 5. 좌표 설정 (순서: 경도, 위도, 고도)
            await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:LOCation:SELect \"User Defined\"");
            await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:RFRame WGS84");
            await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:FORMat DEC");
            await SendAsync( $":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:DEC:WGS" + $" {lon},{lat},{alt}");

            // 6. HIL 모드 추가 설정
            if (mode == "HIL")
            {
                await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:HIL:ITYPe UDP");
                await SendAsync($":SOURce1:BB:GNSS:RECeiver:V1:HIL:PORT {udpPort}");
                await SendAsync($":SOURce1:BB:GNSS:RECeiver:V1:HIL:SLATency {latency:F3}");
            }

            // 7. 시뮬레이션 시작
            await SendAsync(":SOURce1:BB:GNSS:STATe 1");
            await Task.Delay(5000);  // 2→5초: HIL 초기화 충분 대기

            // STATe 1 후 에러 확인
            await CheckErrorAsync();

            // 8. RF 출력 ON
            await SendAsync(":OUTPut1:STATe 1");

            // ⚠ &GTL 안 보냄!
            // SCPI HIL: TCP 유지 → &GTL 불필요
            // UDP HIL: Form1에서 타이밍 제어 후 보냄
        }

        // ════════════════════════════════════════════
        // SCPI HIL 위치 전송 (신규)
        // ════════════════════════════════════════════
        //
        // 매뉴얼 p.256: MODE:A (ECEF 좌표)
        //
        // 형식: MODE:A elapsed,x,y,z,vx,vy,vz,ax,ay,az,yaw,pitch,roll
        //   - elapsed: 시뮬레이션 경과 시간 [초]
        //   - x,y,z: ECEF 위치 [미터]
        //   - vx,vy,vz: ECEF 속도 [m/s]
        //   - ax,ay,az: 가속도 [m/s²]
        //   - yaw,pitch,roll: 자세 [rad]
        //
        // TCP 연결을 유지한 채로 전송하므로:
        //   - &GTL 불필요
        //   - HWTime을 매번 읽을 수 있음 → drift 없음
        //   - DSP 부하 없음 (Remote 유지)

        /// <summary>
        /// SCPI HIL 위치 명령 전송 (MODE:A, ECEF)
        /// </summary>
        public async Task SendHilPositionAsync(
            double elapsedTime,
            double ecefX, double ecefY, double ecefZ,
            double velX = 0, double velY = 0, double velZ = 0,
            double accX = 0, double accY = 0, double accZ = 0,
            double yaw = 0, double pitch = 0, double roll = 0)
        {
            // CultureInfo.InvariantCulture: 소수점을 항상 .으로
            string cmd =
                $":SOURce1:BB:GNSS:RT:RECeiver:V1:HILPosition:MODE:A " +
                string.Format(CultureInfo.InvariantCulture,
                    "{0:F4},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4}",
                    elapsedTime,
                    ecefX, ecefY, ecefZ,
                    velX, velY, velZ,
                    accX, accY, accZ,
                    yaw, pitch, roll);

            await SendAsync(cmd);
        }

        // ════════════════════════════════════════════
        // 좌표만 변경 (*RST 없이)
        // ════════════════════════════════════════════

        public async Task ChangePositionAsync(double lat, double lon, double alt)
        {
            await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:LOCation:SELect \"User Defined\"");
            await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:RFRame WGS84");
            await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:FORMat DEC");
            await SendAsync( $":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:DEC:WGS {lon},{lat},{alt}");
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

        public async Task<string> GetSimInfoAsync()
            => await QueryAsync(":SOURce1:BB:GNSS:SIMulation:INFO?");

        public async Task<double> GetPdopAsync()
        {
            string response = await QueryAsync(":SOURce1:BB:GNSS:RT:PDOP?");
            return double.TryParse(response, out double pdop) ? pdop : 99.0;
        }

        public async Task<double> GetHwTimeAsync()
        {
            string response = await QueryAsync(":SOURce1:BB:GNSS:RT:HWTime?");
            return double.TryParse(response,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double t) ? t : 0.0;
        }

        public async Task<string> GetHilLatencyStatsAsync()
            => await QueryAsync( ":SOURce1:BB:GNSS:RT:RECeiver:V1:HILPosition:LATency:STATistics?");

        public async Task<double> GetLevelAsync()
        {
            string response = await QueryAsync(":SOURce1:POWer:LEVel:IMMediate:AMPLitude?");//지금 LEVEL
            return double.TryParse(response, out double level) ? level : -999;
        }
    }
}
