using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string ip = "169.254.2.20";

            Console.WriteLine("=== HIL UDP 통합 테스트 ===");
            Console.WriteLine("1단계: TCP로 SCPI 설정 + HWTime 읽기");
            Console.WriteLine("2단계: &GTL + TCP 닫기");
            Console.WriteLine("3단계: 즉시 UDP 전송 시작");
            Console.WriteLine("\nEnter 누르면 시작");
            Console.ReadLine();

            // ── 1단계: TCP SCPI 설정 ──
            var tcp = new TcpClient();
            await tcp.ConnectAsync(ip, 5025);
            var stream = tcp.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII);
            var writer = new StreamWriter(stream, Encoding.ASCII)
            { AutoFlush = true };

            Console.WriteLine("[TCP] 연결됨");

            await Send(writer, "*RST");
            await Task.Delay(3000);
            await Send(writer, "*CLS");
            await Send(writer, ":SOURce1:BB:GNSS:PRESet");
            await Task.Delay(1000);
            await Send(writer, ":SOURce1:BB:GNSS:TMODe NAV");
            await Send(writer, ":SOURce1:BB:GNSS:RECeiver:V1:POSition HIL");
            await Send(writer, ":SOURce1:BB:GNSS:RECeiver:V1:LOCation:SELect \"User Defined\"");
            await Send(writer, ":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:RFRame WGS84");
            await Send(writer, ":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:FORMat DEC");
            await Send(writer, ":SOURce1:BB:GNSS:RECeiver:V1:LOCation:COORdinates:DEC:WGS 126.8320,37.6584,28");
            await Send(writer, ":SOURce1:BB:GNSS:RECeiver:V1:HIL:ITYPe UDP");
            await Send(writer, ":SOURce1:BB:GNSS:RECeiver:V1:HIL:PORT 7755");
            await Send(writer, ":SOURce1:BB:GNSS:RECeiver:V1:HIL:SLATency 0.020");
            await Send(writer, ":SOURce1:BB:GNSS:STATe 1");
            await Task.Delay(2000);
            await Send(writer, ":OUTPut1:STATe 1");

            // HWTime 읽기
            await writer.WriteLineAsync(":SOURce1:BB:GNSS:RT:HWTime?");
            await Task.Delay(50);
            string hwTimeStr = await reader.ReadLineAsync();
            double hwTime = double.Parse(hwTimeStr.Trim(),
                System.Globalization.CultureInfo.InvariantCulture);
            Console.WriteLine($"[TCP] HWTime = {hwTime:F2}초");

            // &GTL 보내고 TCP 닫기
            await Send(writer, "&GTL");
            Console.WriteLine("[TCP] &GTL 전송");
            await Task.Delay(200);

            reader.Close();
            writer.Close();
            tcp.Close();
            Console.WriteLine("[TCP] 연결 종료 → Local 모드");

            // ── 2단계: 즉시 UDP 전송 ──
            double lat = 37.6584, lon = 126.8320, alt = 28;

            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;
            double a = 6378137.0;
            double e2 = 0.00669437999014;
            double N = a / Math.Sqrt(1 - e2 * Math.Sin(latRad) * Math.Sin(latRad));
            double x = (N + alt) * Math.Cos(latRad) * Math.Cos(lonRad);
            double y = (N + alt) * Math.Cos(latRad) * Math.Sin(lonRad);
            double z = (N * (1 - e2) + alt) * Math.Sin(latRad);

            Console.WriteLine($"[UDP] ECEF: X={x:F0}, Y={y:F0}, Z={z:F0}");

            var udp = new UdpClient(
                new IPEndPoint(IPAddress.Parse("169.254.2.21"), 0));
            var endpoint = new IPEndPoint(IPAddress.Parse(ip), 7755);

            double baseTime = hwTime + 1.0;  // HWTime + 1초부터 시작
            Console.WriteLine($"[UDP] 전송 시작 (baseTime={baseTime:F2})");

            for (int i = 0; i < 30; i++)
            {
                double time = baseTime + i * 1.0;

                var buf = new byte[216];
                int offset = 16;

                WriteBE(buf, offset, time); offset += 8;
                WriteBE(buf, offset, x); offset += 8;
                WriteBE(buf, offset, y); offset += 8;
                WriteBE(buf, offset, z); offset += 8;

                await udp.SendAsync(buf, buf.Length, endpoint);
                Console.WriteLine($"[UDP] #{i + 1} | Time={time:F1}s | 전송");

                await Task.Delay(1000);
            }

            udp.Close();
            Console.WriteLine("\n전송 완료! PuTTY로 통계 확인하세요.");
            Console.ReadLine();
        }

        static async Task Send(StreamWriter w, string cmd)
        {
            await w.WriteLineAsync(cmd);
            await Task.Delay(50);
            Console.WriteLine($"[TCP] → {cmd}");
        }

        static void WriteBE(byte[] buf, int offset, double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, buf, offset, 8);
        }
    }
}