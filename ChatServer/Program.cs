using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ChatServer
{
    class ChatClient : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        public TcpClient Client => _tcpClient;
        public string? Username { get; private set; }

        public ChatClient(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
            _stream = _tcpClient.GetStream();
            _reader = new StreamReader(_stream);
            _writer = new StreamWriter(_stream);
        }

        public void Dispose()
        {
            _stream.Dispose();
        }

        public async Task<bool> Authenticate(List<ChatClient> clients)
        {
            while (true) 
            {
                await SendMessage("Please enter your username:");
                Username = await _reader.ReadLineAsync();

                if (string.IsNullOrEmpty(Username))
                {
                    await SendMessage("Username cannot be empty. Try again.");
                    continue;
                }

                if (clients.Any(c => c.Username == Username))
                {
                    await SendMessage("This name is already in use. Try another one.");
                }
                else
                {
                    Console.WriteLine($"User {Username} has joined the chat.");
                    await SendMessage("You have successfully connected to the chat.");
                    return true; 
                }
            }
        }


        public async Task Run()
        {
            Console.WriteLine($"User {Username} has joined the chat.");

            string? message = string.Empty;
            do
            {
                message = await _reader.ReadLineAsync();
                Log(message);
                MessageReceived?.Invoke(this, new ChatMessageEventArgs(message));
            }
            while (!string.IsNullOrEmpty(message));
        }

        private void Log(string? message)
        {
            Console.WriteLine($"[{_tcpClient.Client.RemoteEndPoint}]: {message}");
        }

        public async Task SendMessage(string message)
        {
            await _writer.WriteLineAsync(message);
            await _writer.FlushAsync();
        }

        public event EventHandler<ChatMessageEventArgs>? MessageReceived;
    }

    class ChatMessageEventArgs : EventArgs
    {
        public string? Message { get; }

        public ChatMessageEventArgs(string? message)
        {
            Message = message;
        }
    }


    class UdpServerDiscovery
    {
        private const int DiscoveryPort = 9902;
        private readonly UdpClient _udpClient;

        public UdpServerDiscovery()
        {
            _udpClient = new UdpClient(DiscoveryPort);
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    var result = await _udpClient.ReceiveAsync();
                    string requestMessage = Encoding.UTF8.GetString(result.Buffer);

                    if (requestMessage == "DISCOVER_SERVER")
                    {
                        string responseMessage = $"SERVER:{GetLocalIPAddress()}:9901";
                        byte[] responseData = Encoding.UTF8.GetBytes(responseMessage);
                        await _udpClient.SendAsync(responseData, responseData.Length, result.RemoteEndPoint);
                    }
                }
            });
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }
    }

    internal class Program
    {
        private const int port = 9901;
        private static List<ChatClient> _clients = new List<ChatClient>();
        private const string MessageHistoryFile = "chat_history.txt";

        static async Task Main(string[] args)
        {
            InitializeChatHistoryFile();

            var server = new TcpListener(System.Net.IPAddress.Any, port);
            server.Start();

            var udpDiscovery = new UdpServerDiscovery();
            udpDiscovery.Start();

            try
            {
                while (true)
                {
                    var tcpClient = await server.AcceptTcpClientAsync();
                    var client = new ChatClient(tcpClient);
                    await SendChatHistoryToClient(client);

                    if (await client.Authenticate(_clients))
                    {
                        client.MessageReceived += Client_MessageReceived;
                        _clients.Add(client);
                        var task = client.Run();
                    }
                }
            }
            finally
            {
                server.Stop();
            }
        }

        private static void Client_MessageReceived(object? sender, ChatMessageEventArgs e)
        {
            var client = sender as ChatClient;
            if (client == null) return;

            var message = e.Message;

            // Check key for private chat
            if (message.StartsWith("/private"))
            {
                var parts = message.Split(' ', 3);
                if (parts.Length < 3) return; // If command is incorrect to miss it

                var targetUsername = parts[1];
                var privateMessage = parts[2];

                // Search user for his nickname
                var targetClient = _clients.FirstOrDefault(c => c.Username == targetUsername);
                if (targetClient != null)
                {
                    var t = targetClient.SendMessage($"(Private from {client.Username}): {privateMessage}");
                }
                else
                {
                    var t = client.SendMessage($"User {targetUsername} was not found.");
                }
            }
            else
            {
                // Public message
                var publicMessage = $"[{client.Username}]: {message}";

                foreach (var item in _clients)
                {
                    var t = item.SendMessage(publicMessage);
                }

                SaveMessageToHistory(publicMessage);
            }
        }

        private static void SaveMessageToHistory(string message)
        {
            File.AppendAllText(MessageHistoryFile, message + Environment.NewLine);
        }

        private static async Task SendChatHistoryToClient(ChatClient client)
        {
            if (File.Exists(MessageHistoryFile))
            {
                var history = await File.ReadAllLinesAsync(MessageHistoryFile);
                foreach (var message in history)
                {
                    await client.SendMessage(message);
                }
            }
        }

        private static void InitializeChatHistoryFile()
        {
            if (!File.Exists(MessageHistoryFile))
            {
                File.Create(MessageHistoryFile).Dispose();
                Console.WriteLine("The message history file is created.");
            }
        }
    }
}
