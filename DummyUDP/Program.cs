using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

     class DummyServer
    {
        static async Task Main(string[] args)
        {
            int listenPort = 5000;

            using UdpClient udpServer = new UdpClient(listenPort);

            Console.WriteLine($"UDP Dummy Server 시작");
            Console.WriteLine($"수신 포트: {listenPort}");
            Console.WriteLine("종료하려면 Ctrl + C");

            while (true)
            {
                try
                {
                    // UDP 데이터 수신
                    UdpReceiveResult result = await udpServer.ReceiveAsync();

                    string receivedText = Encoding.UTF8.GetString(result.Buffer);

                    Console.WriteLine("--------------------------------------------------");
                    Console.WriteLine($"수신 시간 : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"보낸 곳   : {result.RemoteEndPoint.Address}:{result.RemoteEndPoint.Port}");
                    Console.WriteLine($"수신 길이 : {result.Buffer.Length} byte");
                    Console.WriteLine($"수신 데이터 : {receivedText}");

                    // 더미 응답 데이터
                    string responseText = $"ACK | 받은 데이터: {receivedText}";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseText);

                    // 송신자에게 응답
                    await udpServer.SendAsync(
                        responseBytes,
                        responseBytes.Length,
                        result.RemoteEndPoint);

                    Console.WriteLine($"응답 데이터 : {responseText}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"에러: {ex.Message}");
                }
            }
        }
    }
