using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SMBVB_GNSS
{
    internal class SMBVTCP
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;


        private const int COMMAND_DELAY_MS = 50;// AI said -> RST(장비 초기화)후 대기 → 명시 없음 (관례상 500ms)/   


        public bool IsConnected =>
            _client != null && _client.Connected && _reader != null;

        public async Task ConnectAsync(string ip, int port, int timeoutMs=300)
        {
            _client = new TcpClient();
            _client.ReceiveTimeout = timeoutMs;
            _client.SendTimeout = timeoutMs;

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
        public async Task SendAsync(string command)
        {
            if (!IsConnected)
                throw new InvalidOperationException("장비가 연결되지 않았습니다.");

            await _writer.WriteLineAsync(command);// 비동기 writeLine -> await 때문에 작업이 완료되면 다음 줄 수행(백그라운 스레드에서)   UI 스레드는 동작 가능
            await Task.Delay(COMMAND_DELAY_MS); // pc 쪽에서 잠시 기다리는 것
        }
        public async Task<string> QueryAsync(string command)// 반환값 string
        {
            await SendAsync(command);
            string response = await _reader.ReadLineAsync();
            return response?.Trim() ?? string.Empty;
        }

        //장비 식별
        public async Task<string> GetIdentityAsync()
            => await QueryAsync("*IDN?");

        // 설치 옵션 조회
        public async Task<string> GetOptionsAsync()
            => await QueryAsync("*OPT?");

        //장비 초기화 (이후 500ms 대기)
        public async Task ResetAsync()
        {
            await SendAsync("*RST");
            await SendAsync("*WAI"); //  장비가 이전  명령 처리 완료될 때까지 다음 명령 안 받겠다는 신호 //  SMBV100B의 매뉴얼 700page
        }
        //  에러 큐 초기화
        public async Task ClearStatusAsync()
            => await SendAsync("*CLS");

        //Remote → Local 전환 (UDP 수신 전 필수)
        public async Task GoToLocalAsync()
            => await SendAsync("&GTL"); //장비 터치스크린이 잠김 (조작 불가)→ 터치스크린 다시 사용 가능→ UDP 수신 가능 상태가 됨

        public async Task InitGnssAsync(
           string mode,              // "STAT" | "MOV" | "REM"
           double lat,
           double lon,
           double alt,
           int udpPort = 7755,
           double latency = 0.02)
        {
            // 1. 장비 초기화
            await ResetAsync();       // *RST + 500ms 대기
            await ClearStatusAsync(); // *CLS

            // 2. Navigation 모드 설정  주의: 모드 먼저 설정 → 그 다음 위성/위치  (모드 전환 시 위성 파라미터 초기화됨)
            await SendAsync(":SOURce1:BB:GNSS:TMODe NAV");

            // 3. GPS 활성화
            await SendAsync(":SOURce1:BB:GNSS:SYSTem:GPS:STATe 1");

            // 4. 수신기 위치 타입 설정
            await SendAsync($":SOURce1:BB:GNSS:RECeiver:V1:POSition {mode}");

            // 5. 초기 위치 설정
            //    주의: 순서가 경도(Lon) 먼저, 위도(Lat) 나중 (매뉴얼 307p)
            await SendAsync(
                $":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:DECimal:WGS" +
                $" {lon},{lat},{alt}");

            // 6. HIL(REM) 모드일 때 추가 설정
            if (mode == "REM")
            {
                // 인터페이스 UDP로 설정
                await SendAsync(":SOURce1:BB:GNSS:RECeiver:V1:HIL:ITYPe UDP");

                // UDP 포트 설정
                await SendAsync($":SOURce1:BB:GNSS:RECeiver:V1:HIL:PORT {udpPort}");

                // 시스템 레이턴시 설정 (기본 0.02 = 20ms)
                await SendAsync(
                    $":SOURce1:BB:GNSS:RECeiver:V1:HIL:SLATency {latency:F3}");
            }
            // 7. GNSS 시작
            await SendAsync(":SOURce1:BB:GNSS:STATe 1");

            // 8. RF 출력 ON
            await SendAsync(":OUTPut1:STATe 1");

            // 9. &GTL — Remote 상태 해제 (UDP 수신 전 필수)  매뉴얼 255p: "send the &GTL command after the query"
            await GoToLocalAsync();
        }

      
        // PDOP 조회 (위성 정밀도 지표) 매뉴얼 514p / PDOP < 5 이면 양호
        public async Task<double> GetPdopAsync()
        {
            string response = await QueryAsync(":SOURce1:BB:GNSS:RT:PDOP?");
            return double.TryParse(response, out double pdop) ? pdop : 99.0;
        }
        //시뮬레이션 경과 시간 조회 HIL 레이턴시 캘리브레이션에 사용    매뉴얼 264p
        public async Task<double> GetHwTimeAsync()
        {
            string response = await QueryAsync(":SOURce1:BB:GNSS:RT:HWTime?");
            return double.TryParse(response, out double t) ? t : 0.0;
        }
        public async Task StopGnssAsync()
        {
            await SendAsync(":SOURce1:BB:GNSS:STATe 0");
            await SendAsync(":OUTPut1:STATe 0");
        }
    }

}