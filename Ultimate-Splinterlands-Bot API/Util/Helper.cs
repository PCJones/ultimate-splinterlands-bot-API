using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Ultimate_Splinterlands_Bot_API.Util
{
    public static class Helper
    {
        private static readonly object ConsoleLock = new object();
        public static HttpClient CreateHttpClient()
        {
            var clientHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var httpClient = new HttpClient(clientHandler);
            httpClient.Timeout = new TimeSpan(0, 2, 0);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "USB-API");
            httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));

            return httpClient;
        }
        public static void WriteToConsole(string message)
        {
            string output = $"[{DateTime.Today.ToShortDateString()} {DateTime.Now:hh:mm:ss}] {message}";
            lock (ConsoleLock)
            {
                Console.WriteLine(output);
            }
            //File.AppendAllText("log.txt", output + Environment.NewLine);
        }
        public static async Task<string> DownloadPageAsync(string url)
        {
            var result = await Globals.HttpClient.GetAsync(url);
            var response = await result.Content.ReadAsStringAsync();
            if (result.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return "error";
            }
            return response;
        }
        public static long GetInt64HashCode(string inputString)
        {
            long hashCode = 0;
            if (!string.IsNullOrEmpty(inputString))
            {
                using (HashAlgorithm algorithm = SHA256.Create())
                {
                    byte[] hashText = algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
                    hashCode = BitConverter.ToInt64(hashText, 16);
                }
            }
            return (hashCode);
        }
    }
}
