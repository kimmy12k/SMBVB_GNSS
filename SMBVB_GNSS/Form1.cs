using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;

namespace SMBVB_GNSS
{
    public partial class Form1 : XtraForm
    {
        private CancellationTokenSource _cts = null;
        private CancellationTokenSource _hilCts = null;
        private SMBVTCP _tcp;
        private UdpHilClient _hil;
        private CsvRouteReader _route;

        private string _localIp = "169.254.2.21";
        private string _deviceIp = "";
        private int _scpiPort = 5025;

        private bool IsConnected => _tcp != null && _tcp.IsConnected;

        private static readonly string CFG_PATH =
            System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "config.ini");

        // ════════════════════════════════════════════
        // 생성자
        // ════════════════════════════════════════════
        public Form1()
        {
            InitializeComponent();
            _tcp = new SMBVTCP();
            InitCombos();
            LoadIni();
            SetControlState(false);
            ClearStatus();
        }

        // ════════════════════════════════════════════
        // 초기화
        // ════════════════════════════════════════════
        private void InitCombos()
        {
            comboTestMode.SelectedIndex = 0;
            comboPosition.SelectedIndex = 0;
            txtLat.Text = "0";
            txtLon.Text = "0";
            txtAlt.Text = "0";
        }

        private void SetControlState(bool connected)
        {
            btnConnect.Enabled = !connected;
            btnDisconnect.Enabled = connected;

            grGnssConfig.Enabled = connected;
            btnInitialize.Enabled = connected;
            btnConfig.Enabled = connected;
            btnGnssOn.Enabled = connected;
            btnGnssOff.Enabled = connected;
            btnRfOn.Enabled = connected;
            btnRfOff.Enabled = connected;

            bool isHil = comboPosition.Text == "HIL";
            btnLoadCsv.Enabled = connected && isHil;
            btnHilStart.Enabled = false;
            btnHilStop.Enabled = false;
        }

        // ════════════════════════════════════════════
        // INI 로드 / 저장
        // ════════════════════════════════════════════
        private void LoadIni()
        {
            try
            {
                var cfg = IniParser.Load(CFG_PATH);
                txtIP.EditValue = cfg.Get("Network", "IP", "169.254.2.20");
                txtScpiPort.EditValue = cfg.Get("Network", "ScpiPort", "5025");
                txtUdpPort.EditValue = cfg.Get("Network", "UdpPort", "7755");

                string lat = cfg.Get("Location", "Latitude", "");
                string lon = cfg.Get("Location", "Longitude", "");
                string alt = cfg.Get("Location", "Altitude", "");
                if (!string.IsNullOrEmpty(lat)) txtLat.Text = lat;
                if (!string.IsNullOrEmpty(lon)) txtLon.Text = lon;
                if (!string.IsNullOrEmpty(alt)) txtAlt.Text = alt;
            }
            catch { }
        }

        private void SaveIni()
        {
            try
            {
                var cfg = IniParser.Load(CFG_PATH);
                cfg.Set("Network", "IP", txtIP.Text.Trim());
                cfg.Set("Network", "ScpiPort", txtScpiPort.Text.Trim());
                cfg.Set("Network", "UdpPort", txtUdpPort.Text.Trim());
                cfg.Set("Location", "Latitude", txtLat.Text.Trim());
                cfg.Set("Location", "Longitude", txtLon.Text.Trim());
                cfg.Set("Location", "Altitude", txtAlt.Text.Trim());
                cfg.Save(CFG_PATH);
            }
            catch { }
        }

        // ════════════════════════════════════════════
        // 로그
        // ════════════════════════════════════════════
        private void Log(string msg)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Log(msg)));
                return;
            }
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            memoLog.Text += line + Environment.NewLine;
            memoLog.SelectionStart = memoLog.Text.Length;
            memoLog.ScrollToCaret();
        }

        // ════════════════════════════════════════════
        // 연결 / 해제
        // ════════════════════════════════════════════
        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (!ValidateInput()) return;

            btnConnect.Enabled = false;
            btnConnect.Text = "연결 중...";
            DrawStatusDot(Color.FromArgb(230, 81, 0));

            try
            {
                _deviceIp = txtIP.Text.Trim();
                _scpiPort = int.Parse(txtScpiPort.Text.Trim());

                await _tcp.ConnectAsync(_deviceIp, _scpiPort);

                string idn = await _tcp.GetIdentityAsync();
                string opts = await _tcp.GetOptionsAsync();

                double currentLevel = await _tcp.GetLevelAsync();
                txtLevel.Text = currentLevel.ToString("F1");
                Log($"현재 Level: {currentLevel} dBm");

                _cts = new CancellationTokenSource();

                DrawStatusDot(Color.FromArgb(46, 125, 50));
                SetControlState(true);

                Log($"Connected: {idn}");
                Log($"Options: {opts}");
                Log($"Local IP: {_localIp}");
            }
            catch (Exception ex)
            {
                _tcp.Disconnect();
                _cts?.Cancel();
                _cts = null;

                DrawStatusDot(Color.FromArgb(198, 40, 40));
                btnConnect.Enabled = true;
                btnConnect.Text = "연결";

                Log($"연결 실패: {ex.Message}");
                XtraMessageBox.Show($"연결 실패\n\n{ex.Message}",
                    "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            StopHil();
            _cts?.Cancel();
            _cts = null;
            _tcp.Disconnect();
            DrawStatusDot(Color.FromArgb(198, 40, 40));
            SetControlState(false);
            btnConnect.Text = "연결";
            ClearStatus();
            Log("Disconnected");
        }

        private void Form1_Load(object sender, EventArgs e)
        {
        }

        // ════════════════════════════════════════════
        // Position 드롭다운
        // ════════════════════════════════════════════
        private void comboPosition_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool isHil = comboPosition.Text == "HIL";
            btnLoadCsv.Enabled = IsConnected && isHil;
            btnHilStart.Enabled = false;
            grHilMonitor.Enabled = isHil;
        }

        // ════════════════════════════════════════════
        // Initialize 버튼
        // ════════════════════════════════════════════
        private async void btnInitialize_Click(object sender, EventArgs e)
        {
            if (!IsConnected) return;
            if (!ValidateCoordinates()) return;

            btnInitialize.Enabled = false;
            btnInitialize.Text = "초기화 중...";

            try
            {
                string mode = comboPosition.Text;
                double lat = double.Parse(txtLat.Text, CultureInfo.InvariantCulture);
                double lon = double.Parse(txtLon.Text, CultureInfo.InvariantCulture);
                double alt = double.Parse(txtAlt.Text, CultureInfo.InvariantCulture);
                int udpPort = int.Parse(txtUdpPort.Text.Trim());

                Log("Initialize 시작...");
                await _tcp.InitGnssAsync(mode, lat, lon, alt, udpPort);

                // Level 설정 (*RST가 초기화하므로 재설정)
                string level = txtLevel.Text.Trim();
                await _tcp.SendAsync($":SOURce1:BB:GNSS:POWer:REFerence {level}");
                Log($"RF Level → {level} dBm");

                string info = await _tcp.GetSimInfoAsync();
                Log($"Initialize 완료: {info}");
                UpdateStatus("ON", "ON", comboTestMode.Text, info);
            }
            catch (Exception ex)
            {
                Log($"Initialize 실패: {ex.Message}");
                XtraMessageBox.Show($"초기화 실패\n\n{ex.Message}",
                    "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnInitialize.Enabled = true;
                btnInitialize.Text = "Initialize";
            }
        }

        // ════════════════════════════════════════════
        // Config 버튼
        // ════════════════════════════════════════════
        private async void btnConfig_Click(object sender, EventArgs e)
        {
            if (!IsConnected) return;
            if (!ValidateCoordinates()) return;
            btnConfig.Enabled = false;
            try
            {
                double lat = double.Parse(txtLat.Text, CultureInfo.InvariantCulture);
                double lon = double.Parse(txtLon.Text, CultureInfo.InvariantCulture);
                double alt = double.Parse(txtAlt.Text, CultureInfo.InvariantCulture);

                Log("좌표 변경 중...");
                await _tcp.ChangePositionAsync(lat, lon, alt);
                Log($"좌표 변경 완료: {lat}, {lon}, {alt}");
            }
            catch (Exception ex)
            {
                Log($"좌표 변경 실패: {ex.Message}");
            }
            finally
            {
                btnConfig.Enabled = true;
            }
        }

        // ════════════════════════════════════════════
        // GNSS ON / OFF
        // ════════════════════════════════════════════
        private async void btnGnssOn_Click(object sender, EventArgs e)
        {
            if (!IsConnected) return;
            try
            {
                await _tcp.SendAsync(":SOURce1:BB:GNSS:STATe 1");
                Log("GNSS State → ON");
                lblGnssState.Text = "ON";
                lblGnssState.ForeColor = Color.FromArgb(46, 125, 50);
            }
            catch (Exception ex) { Log($"GNSS ON 실패: {ex.Message}"); }
        }

        private async void btnGnssOff_Click(object sender, EventArgs e)
        {
            if (!IsConnected) return;
            try
            {
                await _tcp.SendAsync(":SOURce1:BB:GNSS:STATe 0");
                Log("GNSS State → OFF");
                lblGnssState.Text = "OFF";
                lblGnssState.ForeColor = Color.FromArgb(198, 40, 40);
            }
            catch (Exception ex) { Log($"GNSS OFF 실패: {ex.Message}"); }
        }

        // ════════════════════════════════════════════
        // RF ON / OFF
        // ════════════════════════════════════════════
        private async void btnRfOn_Click(object sender, EventArgs e)
        {
            if (!IsConnected) return;
            try
            {
                await _tcp.SendAsync(":OUTPut1:STATe 1");
                Log("RF Output → ON");
                lblRfState.Text = "ON";
                lblRfState.ForeColor = Color.FromArgb(46, 125, 50);
            }
            catch (Exception ex) { Log($"RF ON 실패: {ex.Message}"); }
        }

        private async void btnRfOff_Click(object sender, EventArgs e)
        {
            if (!IsConnected) return;
            try
            {
                await _tcp.SendAsync(":OUTPut1:STATe 0");
                Log("RF Output → OFF");
                lblRfState.Text = "OFF";
                lblRfState.ForeColor = Color.FromArgb(198, 40, 40);
            }
            catch (Exception ex) { Log($"RF OFF 실패: {ex.Message}"); }
        }

        // ════════════════════════════════════════════
        // CSV 파일 로드 버튼
        // ════════════════════════════════════════════
        private void btnLoadCsv_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "경로 CSV 파일 선택",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            try
            {
                _route = new CsvRouteReader();
                _route.Load(dlg.FileName);

                var first = _route.GetAt(0);
                txtLat.Text = first.Latitude.ToString("F6", CultureInfo.InvariantCulture);
                txtLon.Text = first.Longitude.ToString("F6", CultureInfo.InvariantCulture);
                txtAlt.Text = first.Altitude.ToString("F0", CultureInfo.InvariantCulture);
                _route.ResetIndex();

                btnHilStart.Enabled = true;
                Log($"CSV 로드: {System.IO.Path.GetFileName(dlg.FileName)}" +
                    $" ({_route.Count}개 포인트, {_route.TotalDuration:F1}초)");
            }
            catch (Exception ex)
            {
                Log($"CSV 로드 실패: {ex.Message}");
                XtraMessageBox.Show($"CSV 로드 실패\n\n{ex.Message}",
                    "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ════════════════════════════════════════════
        // HIL Start — SCPI HIL 방식
        // ════════════════════════════════════════════
        //
        // 핵심: TCP 연결을 유지한 채로 SCPI 명령으로 위치 전송
        //
        // 장점 (vs UDP 방식):
        //   ① TCP 닫기/열기 없음 → DSP 부하 없음
        //   ② HWTime을 매번 읽음 → drift 없음
        //   ③ &GTL 불필요 → Remote 유지
        //   ④ 캘리브레이션 불필요 → 코드 단순
        //   ⑤ Initialize 직후 바로 전송 가능
        //
        // 단점:
        //   SCPI 명령이 UDP보다 느림 (최대 약 10Hz)
        //   → intervalMs를 1000ms(1Hz)로 사용하면 문제 없음
        //
        private async void btnHilStart_Click(object sender, EventArgs e)
        {
            if (!IsConnected) return;
            if (_route == null || _route.Count == 0)
            {
                XtraMessageBox.Show("경로 CSV 파일을 먼저 로드하세요.",
                    "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 버튼 상태 전환
            btnHilStart.Enabled = false;
            btnHilStop.Enabled = true;
            btnInitialize.Enabled = false;
            btnLoadCsv.Enabled = false;
            btnConfig.Enabled = false;
            btnGnssOn.Enabled = false;
            btnGnssOff.Enabled = false;
            btnRfOn.Enabled = false;
            btnRfOff.Enabled = false;

            _hilCts = new CancellationTokenSource();
            int intervalMs = 1000; // 1Hz (SCPI HIL 권장 속도)

            _route.ResetIndex();
            lblPacketCount.Text = "0";
            lblLatency.Text = "-";
            lblHilStatus.Text = "RUNNING";
            lblHilStatus.ForeColor = Color.FromArgb(46, 125, 50);
            lblUpdateRate.Text = $"{1000 / intervalMs} Hz";
            lblUdpPort.Text = "SCPI";  // UDP가 아니라 SCPI 방식

            long packetCount = 0;
            var totalWatch = new Stopwatch();//전체 HIL 실행 시간
            var loopWatch = new Stopwatch();//루프 1회 소요 시간
            totalWatch.Start();

            Log($"HIL 시작 (SCPI): {_route.Count}개 포인트 / {intervalMs}ms 주기");

            try // 247page
            {
                while (!_route.IsFinished &&
                       !_hilCts.Token.IsCancellationRequested)
                {
                    loopWatch.Restart();

                    // ① CSV에서 다음 좌표 읽기
                    var pt = _route.GetNext();

                    // ② WGS84 → ECEF 변환
                    CoordConverter.ToECEF(
                        pt.Latitude, pt.Longitude, pt.Altitude,
                        out double x, out double y, out double z);

                    // ③ HWTime 읽기 (매번! → drift 없음)  // Step4
                    double hwTime = await _tcp.GetHwTimeAsync();

                    // ④ ElapsedTime = HWTime + 0.2초 (살짝 미래)
                    //    매번 현재 시간을 읽으니까 drift가 누적되지 않음 
                    double elapsed = hwTime + 0.2; // 현재 시간을 장비에 알려주기 위해 // 매뉴얼에 정의된 변수명 

                    // ⑤ SCPI HIL 명령 전송 (TCP 유지!)
                    await _tcp.SendHilPositionAsync(elapsed, x, y, z);

                //매뉴얼:  전송 → 통계 조회 → 오프셋 재보정 → 전송 → 통계 조회 → ...
                //우리: 전송 → 전송 → 전송 → ... → 끝나고 통계 조회(1번만)
                    // ⑥ UI 업데이트
                    packetCount++;
                    int remaining = _route.Count - _route.CurrentIndex;//CSV에 총 몇 줄이 있는지-지금까지 몇 줄 읽었는지

                    BeginInvoke(new Action(() =>
                    {
                        lblPacketCount.Text = packetCount.ToString("N0");
                        lblLatency.Text = $"{loopWatch.ElapsedMilliseconds} ms";
                        lblHilStatus.Text = $"{remaining} left";

                        txtLat.Text = pt.Latitude.ToString("F6", CultureInfo.InvariantCulture);
                        txtLon.Text = pt.Longitude.ToString("F6", CultureInfo.InvariantCulture);
                        txtAlt.Text = pt.Altitude.ToString("F0", CultureInfo.InvariantCulture);
                    }));

                    // ⑦ 정확한 주기 유지
                    int loopMs = (int)loopWatch.ElapsedMilliseconds;
                    int delay = intervalMs - loopMs;// 목표시간 - 이번에 걸린시간
                    if (delay > 0)
                        await Task.Delay(delay, _hilCts.Token);
                }

                // CSV 끝 도달 → 완료
                Log($"경로 재생 완료: 총 {packetCount:N0} 패킷 전송");
                BeginInvoke(new Action(() =>
                {
                    lblHilStatus.Text = "FINISHED";
                    lblHilStatus.ForeColor = Color.FromArgb(21, 101, 192);
                }));
            }
            catch (OperationCanceledException)
            {
                Log("HIL 사용자 중지");
            }
            catch (Exception ex)
            {
                Log($"HIL 에러: {ex.Message}");
            }
            finally
            {
                totalWatch.Stop();

                if (lblHilStatus.Text != "FINISHED")
                {
                    lblHilStatus.Text = "STOPPED";
                    lblHilStatus.ForeColor = Color.FromArgb(198, 40, 40);
                }
                Log($"HIL 종료: {packetCount:N0} 패킷 / {totalWatch.Elapsed.TotalSeconds:F1}초");

                // TCP 유지 중이므로 바로 통계 확인 가능!
                try
                {
                    string stats = await _tcp.GetHilLatencyStatsAsync();
                    Log($"HIL 통계: {stats}");

                    string err = await _tcp.GetErrorAsync();
                    Log($"장비 에러: {err}");
                }
                catch (Exception ex)
                {
                    Log($"통계 확인 실패: {ex.Message}");
                }

                // 버튼 복구
                btnHilStart.Enabled = true;
                btnHilStop.Enabled = false;
                btnInitialize.Enabled = true;
                btnLoadCsv.Enabled = true;
                btnConfig.Enabled = true;
                btnGnssOn.Enabled = true;
                btnGnssOff.Enabled = true;
                btnRfOn.Enabled = true;
                btnRfOff.Enabled = true;
            }
        }

        // ════════════════════════════════════════════
        // HIL Stop
        // ════════════════════════════════════════════
        private void btnHilStop_Click(object sender, EventArgs e)
        {
            StopHil();
        }
        private void StopHil()
        {
            _hilCts?.Cancel();
            _hilCts = null;
        }

        // ════════════════════════════════════════════
        // HIL Check
        // ════════════════════════════════════════════
        private async void btnHilCheck_Click(object sender, EventArgs e)
        {
            if (!IsConnected) return;
            try
            {
                string stats = await _tcp.GetHilLatencyStatsAsync();
                Log($"HIL 통계: {stats}");

                double hwTime = await _tcp.GetHwTimeAsync();
                Log($"시뮬레이션 시간: {hwTime:F2}초");

                string err = await _tcp.GetErrorAsync();
                Log($"장비 에러: {err}");
            }
            catch (Exception ex)
            {
                Log($"HIL 확인 실패: {ex.Message}");
            }
        }
        // ════════════════════════════════════════════
        // Log Clear
        // ════════════════════════════════════════════
        private void btnLogClear_Click(object sender, EventArgs e)
        {
            memoLog.Text = string.Empty;
        }

        // ════════════════════════════════════════════
        // Status
        // ════════════════════════════════════════════
        private void UpdateStatus(string gnss, string rf, string mode, string info)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatus(gnss, rf, mode, info)));
                return;
            }
            lblGnssState.Text = gnss;
            lblGnssState.ForeColor = gnss == "ON"
                ? Color.FromArgb(46, 125, 50) : Color.FromArgb(198, 40, 40);
            lblRfState.Text = rf;
            lblRfState.ForeColor = rf == "ON"
                ? Color.FromArgb(46, 125, 50) : Color.FromArgb(198, 40, 40);
            lblTestMode.Text = mode;
            lblSimInfo.Text = info;
        }

        private void ClearStatus()
        {
            lblGnssState.Text = "-"; lblGnssState.ForeColor = Color.Gray;
            lblRfState.Text = "-"; lblRfState.ForeColor = Color.Gray;
            lblTestMode.Text = "-"; lblSimInfo.Text = "-"; lblPdop.Text = "-";
            lblPacketCount.Text = "0"; lblUpdateRate.Text = "-";
            lblLatency.Text = "-"; lblHilStatus.Text = "-"; lblUdpPort.Text = "-";
        }

        // ════════════════════════════════════════════
        // 검증
        // ════════════════════════════════════════════
        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtIP.Text))
            { XtraMessageBox.Show("IP를 입력하세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtIP.Focus(); return false; }
            if (!int.TryParse(txtScpiPort.Text, out int sp) || sp < 1 || sp > 65535)
            { XtraMessageBox.Show("SCPI Port: 1~65535", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtScpiPort.Focus(); return false; }
            if (!int.TryParse(txtUdpPort.Text, out int up) || up < 1 || up > 65535)
            { XtraMessageBox.Show("UDP Port: 1~65535", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtUdpPort.Focus(); return false; }
            return true;
        }

        private bool ValidateCoordinates()
        {
            if (!double.TryParse(txtLat.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) || lat < -90 || lat > 90)
            { XtraMessageBox.Show("위도: -90~90", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtLat.Focus(); return false; }
            if (!double.TryParse(txtLon.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) || lon < -180 || lon > 180)
            { XtraMessageBox.Show("경도: -180~180", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtLon.Focus(); return false; }
            if (!double.TryParse(txtAlt.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            { XtraMessageBox.Show("고도: 숫자 입력", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtAlt.Focus(); return false; }
            return true;
        }

        private void DrawStatusDot(Color color)
        {
            var old = picStatus.Image;
            var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
            picStatus.Image = bmp;
            old?.Dispose();
        }

        // ════════════════════════════════════════════
        // 폼 종료
        // ════════════════════════════════════════════
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopHil();
            _cts?.Cancel();
            _cts = null;
            _tcp?.Disconnect();
            SaveIni();
            base.OnFormClosing(e);
        }
    }
}
