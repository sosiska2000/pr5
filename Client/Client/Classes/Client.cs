using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Classes
{
    class Client
    {
        public string Token { get; set; }
        public DateTime DateConnect { get; set; }

        public Client()
        {
            Random random = new Random();
            string Chars = "QWERTYUIOPASDFGHJKLZXCVBMMqwertyuiopasdfghjklzxcvbmm0123456789";

            Token = new string(Enumerable.Repeat(Chars, 15).Select(x => x[random.Next(Chars.Length)]).ToArray());
            DateConnect = DateTime.Now;
        }
    }
}
