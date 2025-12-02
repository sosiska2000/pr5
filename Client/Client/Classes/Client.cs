using System;

namespace Client.Classes
{
    public class Client
    {
        public string Token { get; set; }
        public string Username { get; set; }
        public DateTime ConnectDate { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }

        public Client()
        {
            ConnectDate = DateTime.Now;
        }
    }
}