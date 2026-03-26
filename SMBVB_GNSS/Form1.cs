using System;
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
        // ── 상태 변수 ─────────────────────────────────
        private CancellationTokenSource _cts = null;
        private CancellationTokenSource _hilCts = null;
        private SMBVTCP _tcp;
        private UdpHilClient _hil;
        private CsvRouteReader _route;     // CSV 경로 데이터

        private bool IsConnected => _tcp != null && _tcp.IsConnected;

        private static readonly string CFG_PATH =
            System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "config.ini");//exe가 어디에 있든 같은 폴더에서 config.ini를 찾도록

        // ════════════════════════════════════════════
        // 생성자
        // ════════════════════════════════════════════
        public Form1()
        {
            InitializeComponent();
            _tcp = new SMBVTCP();
            LoadIni();
            InitCombos();
            SetControlState(false);
        }

        // ════════════════════════════════════════════
        // 초기화
        // ════════════════════════════════════════════
        private void InitCombos()
        {
            comboTestMode.SelectedIndex = 0;    // NAV
            comboPosition.SelectedIndex = 0;    // STAT
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
            btnGnssOn.Enabled = connected;
            btnGnssOff.Enabled = connected;
            btnRfOn.Enabled = connected;
            btnRfOff.Enabled = connected;

            bool isHil = comboPosition.Text == "HIL";
            btnLoadCsv.Enabled = connected && isHil;
            btnHilStart.Enabled = false;        // CSV 로드 후에만 활성화
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
            if (InvokeRequired)//
            {
                Invoke(new Action(() => Log(msg)));
                return;
            }
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";// 시간
            memoLog.Text += line + Environment.NewLine;//
            memoLog.SelectionStart = memoLog.Text.Length;
            memoLog.ScrollToCaret();// 현 위치까지 스크롤
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
                string ip = txtIP.Text.Trim();
                int scpiPort = int.Parse(txtScpiPort.Text.Trim());

                await _tcp.ConnectAsync(ip, scpiPort);

                string idn = await _tcp.GetIdentityAsync();
                string opts = await _tcp.GetOptionsAsync();

                _cts = new CancellationTokenSource();

                DrawStatusDot(Color.FromArgb(46, 125, 50));
                SetControlState(true);

                Log($"Connected: {idn}");
                Log($"Options: {opts}");
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
        //
        // HIL Start 전에 반드시 경로 파일을 먼저 로드해야 함
        // CSV 형식: Time,Latitude,Longitude,Altitude
        //
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

                // 첫 번째 포인트 좌표를 UI에 표시
                var first = _route.GetAt(0);
                txtLat.Text = first.Latitude.ToString("F6", CultureInfo.InvariantCulture);
                txtLon.Text = first.Longitude.ToString("F6", CultureInfo.InvariantCulture);
                txtAlt.Text = first.Altitude.ToString("F0", CultureInfo.InvariantCulture);
                _route.Reset();

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
        // HIL Start
        // ════════════════════════════════════════════
        //
        // 흐름:
        //   CSV 한 줄 읽기
        //   → WGS84→ECEF 변환 (CoordConverter.cs)
        //   → 216B 패킷 생성 (HilPacket.cs)
        //   → UDP 전송 (UdpHilClient.cs)
        //   → 100ms 대기
        //   → 다음 줄 ...
        //   → CSV 끝 → 자동 종료
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

            int udpPort = int.Parse(txtUdpPort.Text.Trim());

            // UdpHilClient 생성
            _hil = new UdpHilClient(txtIP.Text.Trim(), udpPort);

            // UI 업데이트 이벤트
            _hil.OnPacketSent += (count, elapsed, lat, lon, alt, remaining) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() =>
                    {
                        lblPacketCount.Text = count.ToString("N0");
                        lblLatency.Text = $"{(elapsed / count):F1} ms";
                        lblHilStatus.Text = $"{remaining} left";

                        // 현재 전송 중인 좌표를 UI에 실시간 표시
                        txtLat.Text = lat.ToString("F6", CultureInfo.InvariantCulture);
                        txtLon.Text = lon.ToString("F6", CultureInfo.InvariantCulture);
                        txtAlt.Text = alt.ToString("F0", CultureInfo.InvariantCulture);
                    }));
                }
            };

            // 경로 재생 완료 이벤트
            _hil.OnRouteFinished += (totalPackets) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() =>
                    {
                        Log($"경로 재생 완료: 총 {totalPackets:N0} 패킷 전송");
                        lblHilStatus.Text = "FINISHED";
                        lblHilStatus.ForeColor = Color.FromArgb(21, 101, 192);
                    }));
                }
            };

            _hil.OnError += (msg) => Log($"HIL Error: {msg}");

            // 버튼 상태
            btnHilStart.Enabled = false;
            btnHilStop.Enabled = true;
            btnInitialize.Enabled = false;
            btnLoadCsv.Enabled = false;

            _hilCts = new CancellationTokenSource();
            int intervalMs = 100; // 10Hz

            Log($"HIL 시작: {_route.Count}개 포인트 / {intervalMs}ms 주기");
            lblHilStatus.Text = "RUNNING";
            lblHilStatus.ForeColor = Color.FromArgb(46, 125, 50);
            lblUpdateRate.Text = $"{1000 / intervalMs} Hz";
            lblUdpPort.Text = udpPort.ToString();

            // CSV 경로 처음부터
            _route.Reset();

            try
            {
                await _hil.StartAsync(_route, intervalMs, _hilCts.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (lblHilStatus.Text != "FINISHED")
                {
                    lblHilStatus.Text = "STOPPED";
                    lblHilStatus.ForeColor = Color.FromArgb(198, 40, 40);
                }
                Log($"HIL 종료: {_hil.PacketCount:N0} 패킷 전송");

                btnHilStart.Enabled = true;
                btnHilStop.Enabled = false;
                btnInitialize.Enabled = true;
                btnLoadCsv.Enabled = true;

                _hil?.Dispose();
                _hil = null;
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
            // 이전 이미지 메모리 해제
            var old = picStatus.Image;

            var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);

            picStatus.Image = bmp;
            old?.Dispose();  // 이전 이미지 해제
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
