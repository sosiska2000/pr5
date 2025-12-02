using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Server
{
    class Program
    {
        static IPAddress ServerIPAddress;
        static int ServerPort;
        static int MaxClient;
        static int Duration;
        static int CheckInterval;

        static List<ClientData> ConnectedClients = new List<ClientData>();
        static SQLiteConnection dbConnection;

        static void Main(string[] args)
        {
            InitializeDatabase();
            OnSettings();

            Thread tListener = new Thread(ConnectServer);
            tListener.Start();

            Thread tDisconnect = new Thread(CheckDisconnectClient);
            tDisconnect.Start();

            while (true) SetCommand();
        }

        static void InitializeDatabase()
        {
            string dbPath = "licensing.db";
            bool dbExists = File.Exists(dbPath);

            dbConnection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            dbConnection.Open();

            if (!dbExists)
            {
                // Создаем таблицы
                string createUsersTable = @"
                    CREATE TABLE Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT UNIQUE NOT NULL,
                        Password TEXT NOT NULL,
                        IsBanned BOOLEAN DEFAULT 0
                    )";

                string createBlacklistTable = @"
                    CREATE TABLE Blacklist (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL,
                        IpAddress TEXT,
                        Reason TEXT,
                        BannedDate DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";

                string createSessionsTable = @"
                    CREATE TABLE Sessions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Token TEXT UNIQUE NOT NULL,
                        Username TEXT NOT NULL,
                        IpAddress TEXT,
                        Port INTEGER,
                        ConnectDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        LastCheck DATETIME DEFAULT CURRENT_TIMESTAMP,
                        IsActive BOOLEAN DEFAULT 1
                    )";

                using (SQLiteCommand cmd = new SQLiteCommand(createUsersTable, dbConnection))
                    cmd.ExecuteNonQuery();

                using (SQLiteCommand cmd = new SQLiteCommand(createBlacklistTable, dbConnection))
                    cmd.ExecuteNonQuery();

                using (SQLiteCommand cmd = new SQLiteCommand(createSessionsTable, dbConnection))
                    cmd.ExecuteNonQuery();

                // Добавляем тестового пользователя
                string insertTestUser = @"
                    INSERT INTO Users (Username, Password) 
                    VALUES ('admin', 'admin123'), 
                           ('user1', 'pass123'),
                           ('user2', 'pass123')";

                using (SQLiteCommand cmd = new SQLiteCommand(insertTestUser, dbConnection))
                    cmd.ExecuteNonQuery();

                Console.WriteLine("Database initialized with test users: admin/admin123, user1/pass123, user2/pass123");
            }
        }

        static void CheckDisconnectClient()
        {
            while (true)
            {
                try
                {
                    lock (ConnectedClients)
                    {
                        for (int i = ConnectedClients.Count - 1; i >= 0; i--)
                        {
                            int clientDuration = (int)DateTime.Now.Subtract(ConnectedClients[i].ConnectDate).TotalSeconds;

                            if (clientDuration > Duration)
                            {
                                string token = ConnectedClients[i].Token;

                                // Обновляем статус в базе данных
                                string updateQuery = "UPDATE Sessions SET IsActive = 0 WHERE Token = @token";
                                using (SQLiteCommand cmd = new SQLiteCommand(updateQuery, dbConnection))
                                {
                                    cmd.Parameters.AddWithValue("@token", token);
                                    cmd.ExecuteNonQuery();
                                }

                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Client: {token} disconnected from server due to timeout");
                                ConnectedClients.RemoveAt(i);
                            }
                        }
                    }

                    // Проверяем базу данных на предмет неактивных сессий
                    string cleanupQuery = "DELETE FROM Sessions WHERE IsActive = 0";
                    using (SQLiteCommand cmd = new SQLiteCommand(cleanupQuery, dbConnection))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error in CheckDisconnectClient: {ex.Message}");
                }

                Thread.Sleep(CheckInterval * 1000);
            }
        }

        static void SetCommand()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Server> ");
            Console.ForegroundColor = ConsoleColor.White;
            string command = Console.ReadLine();

            if (command == "/config")
            {
                File.Delete("server.config");
                OnSettings();
            }
            else if (command.StartsWith("/disconnect"))
            {
                DisconnectClient(command);
            }
            else if (command == "/status")
            {
                GetStatus();
            }
            else if (command == "/help")
            {
                Help();
            }
            else if (command.StartsWith("/ban"))
            {
                BanUser(command);
            }
            else if (command.StartsWith("/unban"))
            {
                UnbanUser(command);
            }
            else if (command.StartsWith("/users"))
            {
                ListUsers();
            }
            else if (command == "/clear")
            {
                Console.Clear();
            }
        }

        static void BanUser(string command)
        {
            try
            {
                string[] parts = command.Split(' ');
                if (parts.Length < 2)
                {
                    Console.WriteLine("Usage: /ban <username> [reason]");
                    return;
                }

                string username = parts[1];
                string reason = parts.Length > 2 ? command.Substring(command.IndexOf(' ', command.IndexOf(' ') + 1)) : "No reason specified";

                // Добавляем в черный список
                string insertQuery = "INSERT INTO Blacklist (Username, Reason) VALUES (@username, @reason)";
                using (SQLiteCommand cmd = new SQLiteCommand(insertQuery, dbConnection))
                {
                    cmd.Parameters.AddWithValue("@username", username);
                    cmd.Parameters.AddWithValue("@reason", reason);
                    cmd.ExecuteNonQuery();
                }

                // Обновляем статус пользователя
                string updateQuery = "UPDATE Users SET IsBanned = 1 WHERE Username = @username";
                using (SQLiteCommand cmd = new SQLiteCommand(updateQuery, dbConnection))
                {
                    cmd.Parameters.AddWithValue("@username", username);
                    cmd.ExecuteNonQuery();
                }

                // Отключаем активные сессии пользователя
                lock (ConnectedClients)
                {
                    ConnectedClients.RemoveAll(c => c.Username == username);
                }

                string deleteSessionQuery = "DELETE FROM Sessions WHERE Username = @username";
                using (SQLiteCommand cmd = new SQLiteCommand(deleteSessionQuery, dbConnection))
                {
                    cmd.Parameters.AddWithValue("@username", username);
                    cmd.ExecuteNonQuery();
                }

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"User {username} has been banned. Reason: {reason}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error banning user: {ex.Message}");
            }
        }

        static void UnbanUser(string command)
        {
            try
            {
                string[] parts = command.Split(' ');
                if (parts.Length < 2)
                {
                    Console.WriteLine("Usage: /unban <username>");
                    return;
                }

                string username = parts[1];

                // Удаляем из черного списка
                string deleteQuery = "DELETE FROM Blacklist WHERE Username = @username";
                using (SQLiteCommand cmd = new SQLiteCommand(deleteQuery, dbConnection))
                {
                    cmd.Parameters.AddWithValue("@username", username);
                    cmd.ExecuteNonQuery();
                }

                // Обновляем статус пользователя
                string updateQuery = "UPDATE Users SET IsBanned = 0 WHERE Username = @username";
                using (SQLiteCommand cmd = new SQLiteCommand(updateQuery, dbConnection))
                {
                    cmd.Parameters.AddWithValue("@username", username);
                    cmd.ExecuteNonQuery();
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"User {username} has been unbanned");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error unbanning user: {ex.Message}");
            }
        }

        static void ListUsers()
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n=== Registered Users ===");

                string query = "SELECT Username, IsBanned FROM Users";
                using (SQLiteCommand cmd = new SQLiteCommand(query, dbConnection))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string username = reader["Username"].ToString();
                        bool isBanned = Convert.ToBoolean(reader["IsBanned"]);

                        Console.ForegroundColor = isBanned ? ConsoleColor.Red : ConsoleColor.Green;
                        Console.WriteLine($"{username} {(isBanned ? "[BANNED]" : "[ACTIVE]")}");
                    }
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=== Active Sessions ===");

                string sessionQuery = "SELECT Username, Token, IpAddress, ConnectDate FROM Sessions WHERE IsActive = 1";
                using (SQLiteCommand cmd = new SQLiteCommand(sessionQuery, dbConnection))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"{reader["Username"]}: {reader["Token"]} ({reader["IpAddress"]}) - Connected: {reader["ConnectDate"]}");
                    }
                }

                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error listing users: {ex.Message}");
            }
        }

        static void DisconnectClient(string command)
        {
            try
            {
                string[] parts = command.Split(' ');
                if (parts.Length < 2)
                {
                    Console.WriteLine("Usage: /disconnect <token>");
                    return;
                }

                string token = parts[1];

                lock (ConnectedClients)
                {
                    ClientData client = ConnectedClients.Find(x => x.Token == token);
                    if (client != null)
                    {
                        ConnectedClients.Remove(client);

                        // Обновляем базу данных
                        string updateQuery = "UPDATE Sessions SET IsActive = 0 WHERE Token = @token";
                        using (SQLiteCommand cmd = new SQLiteCommand(updateQuery, dbConnection))
                        {
                            cmd.Parameters.AddWithValue("@token", token);
                            cmd.ExecuteNonQuery();
                        }

                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Client: {token} disconnected from server");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Client with token {token} not found");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static string ProcessClientCommand(string message, string clientIp, int clientPort)
        {
            try
            {
                string[] parts = message.Split(' ');
                string command = parts[0];

                if (command == "/auth")
                {
                    if (parts.Length < 3)
                        return "/error Invalid authentication format";

                    string username = parts[1];
                    string password = parts[2];

                    // Проверяем черный список
                    string checkBlacklistQuery = "SELECT COUNT(*) FROM Blacklist WHERE Username = @username";
                    using (SQLiteCommand cmd = new SQLiteCommand(checkBlacklistQuery, dbConnection))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        long blacklisted = (long)cmd.ExecuteScalar();

                        if (blacklisted > 0)
                            return "/banned";
                    }

                    // Проверяем аутентификацию
                    string authQuery = "SELECT COUNT(*) FROM Users WHERE Username = @username AND Password = @password AND IsBanned = 0";
                    using (SQLiteCommand cmd = new SQLiteCommand(authQuery, dbConnection))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.Parameters.AddWithValue("@password", password);
                        long count = (long)cmd.ExecuteScalar();

                        if (count == 0)
                            return "/auth_fail";
                    }

                    // Проверяем лимит клиентов
                    string activeSessionsQuery = "SELECT COUNT(*) FROM Sessions WHERE IsActive = 1";
                    using (SQLiteCommand cmd = new SQLiteCommand(activeSessionsQuery, dbConnection))
                    {
                        long activeCount = (long)cmd.ExecuteScalar();

                        if (activeCount >= MaxClient)
                            return "/limit";
                    }

                    // Генерируем токен
                    string token = GenerateToken();

                    // Сохраняем сессию в базе данных
                    string insertSessionQuery = @"
                        INSERT INTO Sessions (Token, Username, IpAddress, Port, ConnectDate, LastCheck, IsActive) 
                        VALUES (@token, @username, @ip, @port, @connectDate, @lastCheck, 1)";

                    using (SQLiteCommand cmd = new SQLiteCommand(insertSessionQuery, dbConnection))
                    {
                        cmd.Parameters.AddWithValue("@token", token);
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.Parameters.AddWithValue("@ip", clientIp);
                        cmd.Parameters.AddWithValue("@port", clientPort);
                        cmd.Parameters.AddWithValue("@connectDate", DateTime.Now);
                        cmd.Parameters.AddWithValue("@lastCheck", DateTime.Now);
                        cmd.ExecuteNonQuery();
                    }

                    // Добавляем в список активных клиентов
                    ClientData newClient = new ClientData
                    {
                        Token = token,
                        Username = username,
                        IpAddress = clientIp,
                        Port = clientPort,
                        ConnectDate = DateTime.Now
                    };

                    lock (ConnectedClients)
                    {
                        ConnectedClients.Add(newClient);
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"New client connection: {username} ({clientIp}:{clientPort}) - Token: {token}");

                    return $"/token {token}";
                }
                else if (command == "/check")
                {
                    if (parts.Length < 2)
                        return "/error Invalid check format";

                    string token = parts[1];

                    // Проверяем активность токена
                    string checkQuery = "SELECT COUNT(*) FROM Sessions WHERE Token = @token AND IsActive = 1";
                    using (SQLiteCommand cmd = new SQLiteCommand(checkQuery, dbConnection))
                    {
                        cmd.Parameters.AddWithValue("@token", token);
                        long count = (long)cmd.ExecuteScalar();

                        if (count == 0)
                            return "/disconnect";
                    }

                    // Обновляем время последней проверки
                    string updateQuery = "UPDATE Sessions SET LastCheck = @lastCheck WHERE Token = @token";
                    using (SQLiteCommand cmd = new SQLiteCommand(updateQuery, dbConnection))
                    {
                        cmd.Parameters.AddWithValue("@token", token);
                        cmd.Parameters.AddWithValue("@lastCheck", DateTime.Now);
                        cmd.ExecuteNonQuery();
                    }

                    return "/alive";
                }
                else if (command == "/status")
                {
                    string statusQuery = "SELECT COUNT(*) FROM Sessions WHERE IsActive = 1";
                    using (SQLiteCommand cmd = new SQLiteCommand(statusQuery, dbConnection))
                    {
                        long count = (long)cmd.ExecuteScalar();
                        return $"/clients {count}/{MaxClient}";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error processing client command: {ex.Message}");
                return $"/error {ex.Message}";
            }

            return "/error Unknown command";
        }

        static string GenerateToken()
        {
            Random random = new Random();
            string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            char[] token = new char[32];

            for (int i = 0; i < 32; i++)
            {
                token[i] = chars[random.Next(chars.Length)];
            }

            return new string(token);
        }

        static void ConnectServer()
        {
            try
            {
                IPEndPoint endPoint = new IPEndPoint(ServerIPAddress, ServerPort);
                Socket socketListener = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);

                socketListener.Bind(endPoint);
                socketListener.Listen(10);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Server started on {ServerIPAddress}:{ServerPort}");
                Console.WriteLine($"Max clients: {MaxClient}, Session duration: {Duration} seconds");

                while (true)
                {
                    Socket handler = socketListener.Accept();

                    // Получаем IP и порт клиента
                    IPEndPoint clientEndPoint = (IPEndPoint)handler.RemoteEndPoint;
                    string clientIp = clientEndPoint.Address.ToString();
                    int clientPort = clientEndPoint.Port;

                    Thread clientThread = new Thread(() => HandleClient(handler, clientIp, clientPort));
                    clientThread.Start();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Server error: {ex.Message}");
            }
        }

        static void HandleClient(Socket handler, string clientIp, int clientPort)
        {
            try
            {
                byte[] bytes = new byte[1024];
                int bytesRec = handler.Receive(bytes);
                string message = Encoding.UTF8.GetString(bytes, 0, bytesRec);

                string response = ProcessClientCommand(message, clientIp, clientPort);
                handler.Send(Encoding.UTF8.GetBytes(response));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error handling client {clientIp}:{clientPort}: {ex.Message}");
            }
            finally
            {
                handler.Shutdown(SocketShutdown.Both);
                handler.Close();
            }
        }

        static void GetStatus()
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n=== Server Status ===");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"Server IP: {ServerIPAddress}");
                Console.WriteLine($"Server Port: {ServerPort}");
                Console.WriteLine($"Max Clients: {MaxClient}");
                Console.WriteLine($"Session Duration: {Duration} seconds");
                Console.WriteLine($"Check Interval: {CheckInterval} seconds");

                string activeQuery = "SELECT COUNT(*) FROM Sessions WHERE IsActive = 1";
                using (SQLiteCommand cmd = new SQLiteCommand(activeQuery, dbConnection))
                {
                    long activeCount = (long)cmd.ExecuteScalar();
                    Console.WriteLine($"\nConnected Clients: {activeCount}/{MaxClient}");
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n=== Active Clients ===");

                string query = @"
                    SELECT s.Token, s.Username, s.IpAddress, s.Port, s.ConnectDate, 
                           (julianday('now') - julianday(s.ConnectDate)) * 86400 as Duration
                    FROM Sessions s
                    WHERE s.IsActive = 1
                    ORDER BY s.ConnectDate DESC";

                using (SQLiteCommand cmd = new SQLiteCommand(query, dbConnection))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"Token: {reader["Token"]}");
                        Console.WriteLine($"  User: {reader["Username"]}");
                        Console.WriteLine($"  IP: {reader["IpAddress"]}:{reader["Port"]}");
                        Console.WriteLine($"  Connected: {reader["ConnectDate"]}");
                        Console.WriteLine($"  Duration: {Math.Round(Convert.ToDouble(reader["Duration"]), 0)} seconds");
                        Console.WriteLine();
                    }
                }

                if (!reader.HasRows)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("No active clients");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error getting status: {ex.Message}");
            }
        }

        static void Help()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=== Server Commands ===");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/config");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Configure server settings");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/disconnect <token>");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Disconnect specific client");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/status");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Show server status and connected clients");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/ban <username> [reason]");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Ban user and add to blacklist");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/unban <username>");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Unban user");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/users");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - List registered users and active sessions");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/help");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Show this help message");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/clear");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - Clear console");
        }

        static void OnSettings()
        {
            string configPath = "server.config";

            if (File.Exists(configPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configPath);
                    if (lines.Length >= 5)
                    {
                        ServerIPAddress = IPAddress.Parse(lines[0]);
                        ServerPort = int.Parse(lines[1]);
                        MaxClient = int.Parse(lines[2]);
                        Duration = int.Parse(lines[3]);
                        CheckInterval = int.Parse(lines[4]);

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
            Console.WriteLine("\n=== Server Configuration ===");

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
            Console.Write("Enter maximum number of clients [5]: ");
            Console.ForegroundColor = ConsoleColor.Green;
            string maxInput = Console.ReadLine();
            MaxClient = string.IsNullOrEmpty(maxInput) ? 5 : int.Parse(maxInput);

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Enter session duration in seconds [300]: ");
            Console.ForegroundColor = ConsoleColor.Green;
            string durationInput = Console.ReadLine();
            Duration = string.IsNullOrEmpty(durationInput) ? 300 : int.Parse(durationInput);

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Enter check interval in seconds [10]: ");
            Console.ForegroundColor = ConsoleColor.Green;
            string intervalInput = Console.ReadLine();
            CheckInterval = string.IsNullOrEmpty(intervalInput) ? 10 : int.Parse(intervalInput);

            // Сохраняем настройки
            string[] configLines = {
                ServerIPAddress.ToString(),
                ServerPort.ToString(),
                MaxClient.ToString(),
                Duration.ToString(),
                CheckInterval.ToString()
            };

            File.WriteAllLines(configPath, configLines);

            DisplaySettings();
        }

        static void DisplaySettings()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=== Current Server Settings ===");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Server IP Address: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(ServerIPAddress.ToString());

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Server Port: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(ServerPort.ToString());

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Maximum Clients: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(MaxClient.ToString());

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Session Duration: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(Duration.ToString() + " seconds");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Check Interval: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(CheckInterval.ToString() + " seconds");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("\nTo change settings, use command: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("/config\n");
        }
    }

    class ClientData
    {
        public string Token { get; set; }
        public string Username { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public DateTime ConnectDate { get; set; }
    }
}