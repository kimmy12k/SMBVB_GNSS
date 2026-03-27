using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks;

namespace server
{
    internal class udpTest
    {
        private static UdpClient _udpServer;
        private static CancellationTokenSource _udpCts;
        private static bool _udpRunning = false;

        static async Task Main(string[] args)
        {
            int tcpPort = 9000;

            TcpListener tcpListener = new TcpListener(IPAddress.Any, tcpPort);
            tcpListener.Start();

            Console.WriteLine($"TCP 서버 시작 - 포트: {tcpPort}");
            Console.WriteLine("TCP 명령 예시: START_UDP:5000");
            Console.WriteLine("TCP 명령 예시: STOP_UDP");

            while (true)
            {
                TcpClient client = await tcpListener.AcceptTcpClientAsync();
                _ = HandleTcpClientAsync(client);
            }
        }

        private static async Task HandleTcpClientAsync(TcpClient client)
        {
            Console.WriteLine("TCP 클라이언트 접속");

            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] buffer = new byte[1024];
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length);

                    if (read <= 0)
                        return;

                    string command = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                    Console.WriteLine($"TCP 수신: {command}");

                    string response;

                    if (command.StartsWith("START_UDP:", StringComparison.OrdinalIgnoreCase))
                    {
                        string portText = command.Substring("START_UDP:".Length);

                        if (int.TryParse(portText, out int udpPort))
                        {
                            response = StartUdpServer(udpPort);
                        }
                        else
                        {
                            response = "ERROR: 잘못된 UDP 포트";
                        }
                    }
                    else if (command.Equals("STOP_UDP", StringComparison.OrdinalIgnoreCase))
                    {
                        response = StopUdpServer();
                    }
                    else
                    {
                        response = "ERROR: 알 수 없는 명령";
                    }

                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length);

                    Console.WriteLine($"TCP 응답: {response}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TCP 처리 에러: {ex.Message}");
            }
        }

        private static string StartUdpServer(int port)
        {
            try
            {
                if (_udpRunning)
                {
                    return "ERROR: UDP 서버가 이미 실행 중입니다.";
                }

                _udpCts = new CancellationTokenSource();
                _udpServer = new UdpClient(port);
                _udpRunning = true;

                _ = Task.Run(() => ReceiveUdpLoopAsync(_udpCts.Token));

                Console.WriteLine($"UDP 수신 시작 - 포트: {port}");
                return $"OK: UDP STARTED {port}";
            }
            catch (Exception ex)
            {
                _udpRunning = false;
                return $"ERROR: UDP 시작 실패 - {ex.Message}";
            }
        }

        private static string StopUdpServer()
        {
            try
            {
                if (!_udpRunning)
                {
                    return "ERROR: UDP 서버가 실행 중이 아닙니다.";
                }

                _udpRunning = false;
                _udpCts?.Cancel();
                _udpServer?.Close();
                _udpServer = null;

                Console.WriteLine("UDP 수신 중지");
                return "OK: UDP STOPPED";
            }
            catch (Exception ex)
            {
                return $"ERROR: UDP 중지 실패 - {ex.Message}";
            }
        }

        private static async Task ReceiveUdpLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    UdpReceiveResult result = await _udpServer.ReceiveAsync();
                    string text = Encoding.UTF8.GetString(result.Buffer);

                    Console.WriteLine("--------------------------------------------------");
                    Console.WriteLine($"UDP 수신 시간 : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"보낸 곳       : {result.RemoteEndPoint.Address}:{result.RemoteEndPoint.Port}");
                    Console.WriteLine($"수신 길이     : {result.Buffer.Length} byte");
                    Console.WriteLine($"수신 데이터   : {text}");
                }
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("UDP 소켓 종료됨");
            }
            catch (SocketException ex)
            {
                if (_udpRunning)
                {
                    Console.WriteLine($"UDP 소켓 에러: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP 수신 에러: {ex.Message}");
            }
            finally
            {
                _udpRunning = false;
            }
        }
    }
}
