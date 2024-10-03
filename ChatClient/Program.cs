using System.Net;
using System.Net.Sockets;
using System.Text;


namespace ChatClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var serverAddress = await DiscoverServer();
            if (serverAddress == null)
            {
                Console.WriteLine("Server not found.");
                return;
            }

            Console.WriteLine($"Server found: {serverAddress}");

            var tcpClient = new TcpClient();
            var serverInfo = serverAddress.Split(':');
            var serverIp = serverInfo[0];
            var serverPort = int.Parse(serverInfo[1]);
            tcpClient.Connect(IPAddress.Parse(serverIp), serverPort);

            var stream = tcpClient.GetStream();
            var reader = new StreamReader(stream);
            var writer = new StreamWriter(stream);

            Console.Write("Enter your name: ");
            string username = Console.ReadLine();

            await writer.WriteLineAsync(username);
            await writer.FlushAsync();

            var readerTask = Task.Run(() =>
            {
                string? message = string.Empty;
                do
                {
                    message = reader.ReadLine();
                    Console.WriteLine(message);
                }
                while (!string.IsNullOrEmpty(message));
            });

            var writerTask = Task.Run(() =>
            {
                do
                {
                    var message = Console.ReadLine();
                    writer.WriteLine(message);
                    writer.Flush();
                }
                while (true);
            });

            await Task.WhenAll(readerTask, writerTask);
        }

        private static async Task<string?> DiscoverServer()
        {
            using (var udpClient = new UdpClient())
            {
                udpClient.EnableBroadcast = true;
                var requestData = Encoding.UTF8.GetBytes("DISCOVER_SERVER");
                var serverEndpoint = new IPEndPoint(IPAddress.Broadcast, 9902);

                await udpClient.SendAsync(requestData, requestData.Length, serverEndpoint);

                var result = await udpClient.ReceiveAsync();
                string responseMessage = Encoding.UTF8.GetString(result.Buffer);
                if (responseMessage.StartsWith("SERVER"))
                {
                    return responseMessage.Split(':')[1] + ":" + responseMessage.Split(':')[2];
                }
            }

            return null;
        }
    }
}
