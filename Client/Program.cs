using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client
{
    public class Program
    {
        static IPAddress ServerIpAddress;
        static int ServerPort;

        static string ClientToken;
        static DateTime ClientDateConnection;

        static void Main(string[] args)
        {
            OnSettings();

            Thread tCheckToken = new Thread(CheckToken);
            tCheckToken.Start();

            while (true)
            {
                SetCommand();
            }
        }

        public static void SetCommand()
        {
            Console.ForegroundColor = ConsoleColor.White;
            string Command = Console.ReadLine();

            string[] DataCommand = Command.Split(new string[1] { " " }, StringSplitOptions.None);
            if (Command == "/config")
            {
                File.Delete(Directory.GetCurrentDirectory() + "/.config");
                OnSettings();
            }
            else if (DataCommand[0] == "/connect") ConnectServer(DataCommand[1], DataCommand[2]);
            else if (Command == "/status") GetStatus();
            else if (Command == "/help") Help();
        }

        public static void ConnectServer(string log, string pwd)
        {
            Console.ForegroundColor = ConsoleColor.White;


            IPEndPoint endPoint = new IPEndPoint(ServerIpAddress, ServerPort);
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(endPoint);

            string msg = $"/connect {log} {pwd}";
            socket.Send(Encoding.UTF8.GetBytes(msg));

            byte[] buffer = new byte[1024];
            int size = socket.Receive(buffer);
            string response = Encoding.UTF8.GetString(buffer, 0, size);

            if (response == "/auth_fail")
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Неверный логин или пароль.");
                return;
            }

            if (response == "/banned")
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Ваш аккаунт находится в черном списке!");
                return;
            }

            if (response == "/limit")
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Нет свободных лицензий.");
                return;
            }

            ClientToken = response;
            ClientDateConnection = DateTime.Now;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Успешное подключение. Ваш токен: " + ClientToken);
        }

        public static void CheckToken()
        {
            while (true)
            {
                if (!String.IsNullOrEmpty(ClientToken))
                {
                    IPEndPoint EndPoint = new IPEndPoint(ServerIpAddress, ServerPort);
                    Socket Socket = new Socket(
                        AddressFamily.InterNetwork,
                        SocketType.Stream,
                        ProtocolType.Tcp);

                    try
                    {
                        Socket.Connect(EndPoint); ;

                    }
                    catch (Exception exp)
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("Error: " + exp.Message);
                    }

                    if (Socket.Connected)
                    {

                        Socket.Send(Encoding.UTF8.GetBytes(ClientToken));

                        byte[] Bytes = new byte[10485760];
                        int ByteRec = Socket.Receive(Bytes);

                        string Response = Encoding.UTF8.GetString(Bytes, 0, ByteRec);
                        if (Response == "/disconnect")
                        {
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine("The client is disconnected from server");
                            ClientToken = String.Empty;
                        }
                    }
                }

                Thread.Sleep(1000);
            }


        }

        public static void GetStatus()
        {
            int Duration = (int)DateTime.Now.Subtract(ClientDateConnection).TotalSeconds;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"Client: {ClientToken}, time connection: {ClientDateConnection.ToString("HH:mm:ss dd.MM")}, " +
                $"duration: {Duration}"
                );
        }

        public static void Help()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Commands to the server: ");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/config");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - set initial settings ");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/connect");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - connection to the server ");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("/status");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" - show list users ");
        }

        public static void OnSettings()
        {
            string Path = Directory.GetCurrentDirectory() + "/.config";
            string IpAddress = "";

            if (File.Exists(Path))
            {
                StreamReader streamReader = new StreamReader(Path);
                IpAddress = streamReader.ReadLine();
                ServerIpAddress = IPAddress.Parse(IpAddress);
                ServerPort = int.Parse(streamReader.ReadLine());
                streamReader.Close();

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Server address: ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(IpAddress);

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Server port: ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(ServerPort.ToString());
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Please provide the IP address if the license server: ");
                Console.ForegroundColor = ConsoleColor.Green;
                IpAddress = Console.ReadLine();
                ServerIpAddress = IPAddress.Parse(IpAddress);

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Please specify the license server port: ");
                Console.ForegroundColor = ConsoleColor.Green;
                ServerPort = int.Parse(Console.ReadLine());

                StreamWriter streamWriter = new StreamWriter(Path);
                streamWriter.WriteLine(IpAddress);
                streamWriter.WriteLine(ServerPort.ToString());
                streamWriter.Close();
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("To change, write the command: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("/config");
        }
    }

}

