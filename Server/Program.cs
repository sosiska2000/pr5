using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    internal class Program
    {
        static void Main(string[] args)
        {

        }

        public static void ConnectServer()
        {
            Console.ForegroundColor = ConsoleColor.White;

            Console.Write("Login: ");
            string login = Console.ReadLine();

            Console.Write("Password: ");
            string password = Console.ReadLine();

            IPEndPoint endPoint = new IPEndPoint(ServerIpAddress, ServerPort);
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(endPoint);

            string msg = $"/connect {login} {password}";
            socket.Send(Encoding.UTF8.GetBytes(msg));

            byte[] buffer = new byte[1024];
            int size = socket.Receive(buffer);
            string response = Encoding.UTF8.GetString(buffer, 0, size);

            if (response == "/auth_fail")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Неверный логин или пароль.");
                return;
            }

            if (response == "/banned")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Ваш аккаунт находится в черном списке!");
                return;
            }

            if (response == "/limit")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Нет свободных лицензий.");
                return;
            }

            ClientToken = response;
            ClientDateConnection = DateTime.Now;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Успешное подключение. Ваш токен: " + ClientToken);
        }
    }
}
