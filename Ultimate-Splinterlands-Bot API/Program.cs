using System;
using System.Net;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using Ultimate_Splinterlands_Bot_API.Api;
using Ultimate_Splinterlands_Bot_API.Util;

namespace Ultimate_Splinterlands_Bot_API
{
    class Program
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.FirstChanceException += (sender, eventArgs) =>
            {
                Console.WriteLine("Unhandled error" + eventArgs.Exception.ToString() + Environment.NewLine + eventArgs.Exception.Source + Environment.NewLine + eventArgs.Exception.StackTrace);
            };

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = cancellationTokenSource.Token;

            TeamGeneration.Initialize();
            Task.Run(async () => await WebApi.ResetIPsLoop(token)).ConfigureAwait(false);
            WebApi.StartServer(token);
            string command = "";
            do
            {
                Helper.WriteToConsole($"Current load: {WebApi.CurrentRequests}/{WebApi.MaxConcurrentRequests}");
                Helper.WriteToConsole("Commands: stop, maxrequests:n, ratelimit:n, ratereset:n");
                command = Console.ReadLine();
                switch (command.Split(':')[0])
                {
                    case "setmaxrequests":
                        WebApi.MaxConcurrentRequests = Convert.ToInt32(command.Split(':')[1]);
                        Console.WriteLine("New Max Concurrent Requests: " + WebApi.MaxConcurrentRequests);
                        break;
                    case "ratelimit":
                        WebApi.RateLimit = Convert.ToInt32(command.Split(':')[1]); ;
                        Console.WriteLine("New Rate Limit: " + WebApi.RateLimit);
                        break;
                    case "ratereset":
                        WebApi.RateLimitReset = Convert.ToInt32(command.Split(':')[1]); ;
                        Console.WriteLine("New Rate Reset every n minutes: " + WebApi.RateLimitReset);
                        break;
                    default:
                        break;
                }
            } while (command != "stop");
            cancellationTokenSource.Cancel();
            Console.ReadLine();
        }
    }
}