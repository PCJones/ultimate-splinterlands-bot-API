using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace HistoryCrawler.Util
{
    internal static class Helper
    {
        public static void WriteLogLine(string value)
        {
            Console.WriteLine($"[{ DateTime.Now }] {value}");
        }

        public static void WriteErrorLogLine(string value)
        {
            Console.Error.WriteLine($"[{ DateTime.Now }] {value}");
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

        public static Task ForEachAsync<T>(this IEnumerable<T> source, int dop, Func<T, Task> body)
        {
            return Task.WhenAll(
                from partition in Partitioner.Create(source).GetPartitions(dop)
                select Task.Run(async delegate
                {
                    using (partition)
                        while (partition.MoveNext())
                            await body(partition.Current).ContinueWith(t =>
                            {
                                //observe exceptions
                            });

                }));
        }
    }
}
