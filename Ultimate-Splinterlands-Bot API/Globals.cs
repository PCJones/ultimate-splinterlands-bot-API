using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ultimate_Splinterlands_Bot_API.Util;

namespace Ultimate_Splinterlands_Bot_API
{
    internal class Globals
    {
        public static readonly Random Random = new Random();
        public static readonly HttpClient HttpClient = Helper.CreateHttpClient();
    }
}
