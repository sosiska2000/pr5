using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Client
{
    public class Program
    {
        static IPAddress ServerIPAddress;
        static int ServerPort;
        static int CheckInterval;

        static string ClientToken;
        static string ClientUsername;
        static DateTime ClientConnectDate;
        static bool IsConnected = false;

        static void Main(string[] args)
        {
            Console.Title = "License Client";
            OnSettings();

            while (true)
            {
                SetCommand();
            }
        }

        static void SetCommand()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Client> ");
            Console.ForegroundColor = ConsoleColor.White;

            string command = Console.ReadLine();

            if (command == "/config")
            {
                File.Delete("client.config");
                OnSettings();
            }
            else if (command == "/connect" || command == "/auth")
            {
                Authenticate();
            }
            else if (command == "/disconnect")
            {
                Disconnect();
            }
            else if (command == "/status")
            {
                GetStatus();
            }
            else if (command == "/help")
            {
                Help();
            }
            else if (command == "/clear")
            {
                Console.Clear();
            }
            else if (command == "/server")
            {
                GetServerStatus();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Unknown command. Type /help for available commands.");
            }
        }

        public static void Authenticate()
        {
            if (IsConnected)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Already connected. Disconnect first.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Username: ");
            Console.ForegroundColor = ConsoleColor.Green;
            string username = Console.ReadLine();

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Password: ");
            Console.ForegroundColor = ConsoleColor.Green;

            // Скрываем ввод пароля
            string password = "";
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey(true);

                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    password += key.KeyChar;
                    Console.Write("*");
                }
                else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password.Substring(0, (password.Length - 1));
                    Console.Write("\b \b");
                }
            } while (key.Key != ConsoleKey.Enter);

            Console.WriteLine();

            try
            {
                IPEndPoint endPoint = new IPEndPoint(ServerIPAddress, ServerPort);
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.Connect(endPoint);

                    string message = $"/auth {username} {password}";
                    socket.Send(Encoding.UTF8.GetBytes(message));

                    byte[] buffer = new byte[1024];
                    int size = socket.Receive(buffer);
                    string response = Encoding.UTF8.GetString(buffer, 0, size);
                    string[] responseParts = response.Split(' ');

                    if (responseParts[0] == "/auth_fail")
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Authentication failed: Invalid username or password.");
                        return;
                    }

                    if (response == "/banned")
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Your account is banned!");
                        return;
                    }

                    if (response == "/limit")
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("No available licenses.");
                        return;
                    }

                    if (responseParts[0] == "/error")
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Server error: {response.Substring(6)}");
                        return;
                    }

                    if (responseParts[0] == "/token")
                    {
                        ClientToken = responseParts[1];
                        ClientUsername = username;
                        ClientConnectDate = DateTime.Now;
                        IsConnected = true;

                        // Запускаем поток для проверки токена
                        Thread checkThread = new Thread(CheckToken);
                        checkThread.Start();

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\nSuccessfully connected!");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"Username: {username}");
                        Console.WriteLine($"Token: {ClientToken}");
                        Console.WriteLine($"Connection time: {ClientConnectDate.ToString("HH:mm:ss dd.MM.yyyy")}");

                        // Запускаем поток для периодической проверки статуса
                        Thread statusThread = new Thread(() => PeriodicallyCheckServerStatus(CheckInterval));
                        statusThread.Start();
                    }
                }
            }
            catch (SocketException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Cannot connect to server. Check server address and port.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public static void CheckToken()
        {
            while (IsConnected)
            {
                try
                {
                    Thread.Sleep(CheckInterval * 1000);

                    if (!IsConnected || string.IsNullOrEmpty(ClientToken))
                        break;

                    using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        socket.Connect(new IPEndPoint(ServerIPAddress, ServerPort));
                        string message = $"/check {ClientToken}";
                        socket.Send(Encoding.UTF8.GetBytes(message));

                        byte[] buffer = new byte[1024];
                        int size = socket.Receive(buffer);
                        string response = Encoding.UTF8.GetString(buffer, 0, size);

                        if (response == "/disconnect")
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("\n[SYSTEM] Server disconnected your session.");
                            Disconnect();
                            break;
                        }
                        else if (response == "/alive")
                        {
                            // Токен валиден, продолжаем работу
                            Console.ForegroundColor = ConsoleColor.DarkGreen;
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Token check: OK");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Token check error: {ex.Message}");

                    // Если не удалось подключиться к серверу, пытаемся переподключиться
                    if (ex is SocketException)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("[SYSTEM] Server unavailable. Trying to reconnect in 30 seconds...");
                        Thread.Sleep(30000);
                    }
                }
            }
        }

        static void PeriodicallyCheckServerStatus(int intervalSeconds)
        {
            while (IsConnected)
            {
                Thread.Sleep(intervalSeconds * 1000);
                GetServerStatus();
            }
        }

        public static void GetServerStatus()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.Connect(new IPEndPoint(ServerIPAddress, ServerPort));
                    string message = "/status";
                    socket.Send(Encoding.UTF8.GetBytes(message));

                    byte[] buffer = new byte[1024];
                    int size = socket.Receive(buffer);
                    string response = Encoding.UTF8.GetString(buffer, 0, size);

                    if (response.StartsWith("/clients"))
                    {
                        string[] parts = response.Split(' ');
                        string[] counts = parts[1].Split('/');

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Server status: {counts[0]}/{counts[1]} clients connected");
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки при проверке статуса
            }
        }

        public static void Disconnect()
        {
            ClientToken = string.Empty;
            ClientUsername = string.Empty;
            IsConnected = false;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Disconnected from server.");
        }

        public static void GetStatus()
        {
            if (!IsConnected || string.IsNullOrEmpty(ClientToken))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Not connected to server.");
                return;
            }

            int duration = (int)DateTime.Now.Subtract(ClientConnectDate).TotalSeconds;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=== Client Status ===");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Username: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(ClientUsername);

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Token: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(ClientToken);

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Server: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{ServerIPAddress}:{ServerPort}");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Connection time: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(ClientConnectDate.ToString("HH:mm:ss dd.MM.yyyy"));

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Duration: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{duration} seconds ({TimeSpan.FromSeconds(duration):hh\\:mm\\:ss})");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Status: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Connected ✓\n");
        }

        public static void OnSettings()
        {
            string configPath = "client.config";

            if (File.Exists(configPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configPath);
                    if (lines.Length >= 3)
                    {
                        ServerIPAddress = IPAddress.Parse(lines[0]);
                        ServerPort = int.Parse(lines[1]);
                        CheckInterval = int.Parse(lines[2]);

                        DisplaySettings();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error reading config: {ex.Message}");
                }
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=== Client Configuration ===");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Enter server IP address [127.0.0.1]: ");
            Console.ForegroundColor = ConsoleColor.Green;
            string ipInput = Console.ReadLine();
            ServerIPAddress = string.IsNullOrEmpty(ipInput) ? IPAddress.Loopback : IPAddress.Parse(ipInput);

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Enter server port [8888]: ");
            Console.ForegroundColor = ConsoleColor.Green;
            string portInput = Console.ReadLine();
            ServerPort = string.IsNullOrEmpty(portInput) ? 8888 : int.Parse(portInput);

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Enter check interval in seconds [10]: ");
            Console.ForegroundColor = ConsoleColor.Green;
            string intervalInput = Console.ReadLine();
            CheckInterval = string.IsNullOrEmpty(intervalInput) ? 10 : int.Parse(intervalInput);

            // Сохраняем настройки
            string[] configLines = {
                ServerIPAddress.ToString(),
                ServerPort.ToString(),
                CheckInterval.ToString()
            };

            File.WriteAllLines(configPath, configLines);

            DisplaySettings();
        }

        static void DisplaySettings()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=== Current Client Settings ===");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Server IP Address: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(ServerIPAddress.ToString());

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Server Port: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(ServerPort.ToString());

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Check Interval: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(CheckInterval.ToString() + " seconds");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("\nTo change settings, use command: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("/config\n");
        }

        static void Help()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=== Client Commands ===");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/connect or /auth");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Connect to license server with authentication");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/disconnect");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Disconnect from server");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/status");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Show client status and connection info");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/server");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Check server status");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/config");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Configure client settings");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/help");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Show this help message");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/clear");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Clear console");
        }
    }
}