using System;
using System.Drawing;
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
        private SMBVTCP _tcp ;

        // 연결 여부 확인
        private bool IsConnected => _cts != null && !_cts.IsCancellationRequested;

        private static readonly string CFG_PATH =
            System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "config.ini");// 얍 exe 파일이 있는 경로로 

        // ════════════════════════════════════════════
        // 생성자
        // ════════════════════════════════════════════
        public Form1()
        {
            InitializeComponent();
            _tcp = new SMBVTCP();
            LoadIni();
            DrawStatusDot(Color.FromArgb(198, 40, 40)); // 초기: 빨강
            btnDisconnect.Enabled = false;
        }

        // ════════════════════════════════════════════
        // INI 로드 / 저장
        // ════════════════════════════════════════════
        private void LoadIni()
        {
            try
            {
                var cfg = IniParser.Load(CFG_PATH);
                txtIP.EditValue = cfg.Get("Network", "IP", "192.168.1.100");
                txtScpiPort.EditValue = cfg.Get("Network", "ScpiPort", "5025");
                textEdit3.EditValue = cfg.Get("Network", "UdpPort", "7755");
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
                cfg.Set("Network", "UdpPort", textEdit3.Text.Trim());
                cfg.Save(CFG_PATH);
            }
            catch { }
        }

        // ════════════════════════════════════════════
        // 연결 버튼
        // ════════════════════════════════════════════
        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (!ValidateInput()) return;

            btnConnect.Enabled = false;
            btnConnect.Text = "연결 중...";
            DrawStatusDot(Color.FromArgb(230, 81, 0)); // 주황

            try
            {
                string ip = txtIP.Text.Trim();
                int scpiPort = int.Parse(txtScpiPort.Text.Trim());

                await _tcp.ConnectAsync(ip, scpiPort);

                string idn = await _tcp.GetIdentityAsync();
                string opts = await _tcp.GetOptionsAsync();

                // 연결 성공 → CancellationTokenSource 생성
                _cts = new CancellationTokenSource();

                DrawStatusDot(Color.FromArgb(46, 125, 50)); // 초록

                btnConnect.Enabled = false;
                btnDisconnect.Enabled = true;

                XtraMessageBox.Show(
                    $"연결 성공!\n\n장비: {idn}\n옵션: {opts}",
                    "연결 성공",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _tcp.Disconnect();

                _cts?.Cancel();
                _cts = null;

                DrawStatusDot(Color.FromArgb(198, 40, 40)); // 빨강

                btnConnect.Enabled = true;
                btnConnect.Text = "연결";

                XtraMessageBox.Show(
                    $"연결 실패\n\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // ════════════════════════════════════════════
        // 해제 버튼
        // ════════════════════════════════════════════
        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            _cts = null;

            _tcp.Disconnect();

            DrawStatusDot(Color.FromArgb(198, 40, 40)); // 빨강

            btnConnect.Enabled = true;
            btnConnect.Text = "연결";
            btnDisconnect.Enabled = false;
        }

        // ════════════════════════════════════════════
        // 입력값 검증
        // ════════════════════════════════════════════
        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtIP.Text))
            {
                XtraMessageBox.Show("IP 주소를 입력하세요.", "입력 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtIP.Focus();
                return false;
            }

            if (!int.TryParse(txtScpiPort.Text, out int sp) || sp < 1 || sp > 65535)
            {
                XtraMessageBox.Show("SCPI Port는 1~65535 숫자를 입력하세요.", "입력 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtScpiPort.Focus();
                return false;
            }

            if (!int.TryParse(textEdit3.Text, out int up) || up < 1 || up > 65535)
            {
                XtraMessageBox.Show("UDP Port는 1~65535 숫자를 입력하세요.", "입력 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                textEdit3.Focus();
                return false;
            }

            return true;
        }

        // ════════════════════════════════════════════
        // 상태 도트 그리기 (pictureEdit1)
        // ════════════════════════════════════════════
        private void DrawStatusDot(Color color)
        {
            var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
            pictureEdit1.Image = bmp;
        }

        // ════════════════════════════════════════════
        // 폼 종료
        // ════════════════════════════════════════════
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            _cts = null;
            _tcp?.Disconnect();
            SaveIni();
            base.OnFormClosing(e);
        }
    }
}
