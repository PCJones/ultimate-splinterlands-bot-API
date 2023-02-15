using Ultimate_Splinterlands_Bot_API.Model;
using WatsonWebserver;
using System.Collections.Concurrent;
using Ultimate_Splinterlands_Bot_API.Util;
using HttpMethod = WatsonWebserver.HttpMethod;

namespace Ultimate_Splinterlands_Bot_API.Api
{
    class WebApi
    {
        private static readonly ConcurrentDictionary<string, int> RequestsPerIp = new();
        public static int RateLimit = 9999;
        public static int RateLimitReset = 30; // minutes
        public static int CurrentRequests = 0;
        public static int MaxConcurrentRequests = 60;

        public static void StartServer(CancellationToken token)
        {
            Server server = new("*", 8080, false, DefaultRoute);
            server.StartAsync(token);
        }

        public static async Task ResetIPsLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(RateLimitReset * 60000, token);
                RequestsPerIp.Clear();
            }
        }

        private async static Task<string> GenerateTeamResponseAsync(string postData, int ratingBracket, int rating, string format)
        {
            try
            {
                var responseProperties = await Task.Run(async () => await TeamGeneration.GetTeamAsync(postData, ratingBracket, rating, format, false));
                if (responseProperties == null)
                {
                    // try again with single ruleset;
                    responseProperties = await Task.Run(async () => await TeamGeneration.GetTeamAsync(postData, ratingBracket, rating, format, true));
                }
                return ConvertTeamResponsePropertiesToJson(responseProperties);

            }
            catch (Exception ex)
            {
                Helper.WriteToConsole("GenerateTeamResponseAsync: " + ex.ToString() + Environment.NewLine + "postData: " + Environment.NewLine + postData);
            }
            return "";
        }

        private static string ConvertTeamResponsePropertiesToJson(TeamResponseProperty[] response)
        {
            string json = "{";
            foreach (TeamResponseProperty property in response)
            {
                json += $"\"{property.Property}\":\"{property.Value}\",";
            }

            json = json[..^1] + "}";

            return json;
        }

        static async Task DefaultRoute(HttpContext ctx)
        {
            string responseString = "<a href='https://github.com/PCJones/ultimate-splinterlands-bot-V2'>Splinderlands Bot</a>";
            ctx.Response.StatusCode = 200;
            await ctx.Response.Send(responseString);
        }

        [StaticRoute(HttpMethod.GET, "/rate_limited")]
        public static async Task RateLimitedRoute(HttpContext ctx)
        {
            string errorData = "";
            string responseString = "<a href='https://github.com/PCJones/ultimate-splinterlands-bot-V2'>Splinderlands Bot</a>";
            ctx.Response.StatusCode = 200;
            try
            {
                string clientIP = ctx.Request.Source.IpAddress;
                Helper.WriteToConsole($"Incoming {ctx.Request.Url.Full} from {clientIP}");
                errorData = ctx.Request.Url.Full + Environment.NewLine;

                // Logic starts here
                if (RequestsPerIp.ContainsKey(clientIP))
                {
                    if (RequestsPerIp[clientIP]++ > RateLimit)
                    {
                        responseString = "api limit reached (rate limit! Current maximum: " + RateLimit.ToString()
                            + " requests per " + RateLimitReset.ToString() + " minutes)";
                    }
                }
            }
            catch (Exception ex)
            {
                Helper.WriteToConsole("Error: " + ex.Message);
                Helper.WriteToConsole("ErrorData: " + errorData);
                responseString = "API Error";
                ctx.Response.StatusCode = 500;
            }

            await ctx.Response.Send(responseString);
        }

        [ParameterRoute(HttpMethod.POST, "/v3/get_team/{format}/{rating}")]
        public static async Task GetTeamRouteV3(HttpContext ctx)
        {
            CurrentRequests++;
            string errorData = "";
            string responseString = "";
            ctx.Response.StatusCode = 200;
            try
            {
                string clientIP = ctx.Request.Source.IpAddress;
                Helper.WriteToConsole($"Incoming {ctx.Request.Url.Full} from {clientIP}");
                errorData = ctx.Request.Url.Full + Environment.NewLine;
                string postData = ctx.Request.DataAsString;
                errorData += postData;

                // Logic starts here
                bool rateLimited = false;
                if (RequestsPerIp.ContainsKey(clientIP))
                {
                    if (RequestsPerIp[clientIP]++ > RateLimit)
                    {
                        responseString = $"api limit reached (rate limit! Current maximum: {RateLimit} requests per {RateLimitReset} minutes)";
                        Helper.WriteToConsole("API Rate Limit: " + clientIP);
                        rateLimited = true;
                    }
                }
                else
                {
                    RequestsPerIp.TryAdd(clientIP, 1);
                }

                if (CurrentRequests >= MaxConcurrentRequests)
                {
                    responseString = "api limit reached (overload)";
                }
                else if (!rateLimited)
                {
                    int rating = Convert.ToInt32(ctx.Request.Url.Parameters["rating"]);
                    int ratingBracket = GetRatingBracket(rating);
                    string format = GetFormat(ctx.Request.Url.Parameters);
                    responseString = await GenerateTeamResponseAsync(postData, ratingBracket, rating, format);
                }
                Helper.WriteToConsole($"Response {ctx.Request.Url.Full}");
            }
            catch (Exception ex)
            {
                Helper.WriteToConsole("Error: " + ex.Message);
                Helper.WriteToConsole("ErrorData: " + errorData);
                responseString = "API Error";
                ctx.Response.StatusCode = 500;
            }
            finally
            {
                CurrentRequests--;
            }

            await ctx.Response.Send(responseString);
        }

        // this probably shouldn't be hard coded as it also needs to be changed in the SQL query files
        private static int GetRatingBracket(int rating)
        {
            return rating switch
            {
                var x when x >= 0 && x <= 120 => 1, //changed
                var x when x >= 121 && x <= 400 => 1, //changed
                var x when x >= 401 && x <= 950 => 2,
                var x when x >= 951 && x <= 1400 => 3,
                var x when x >= 1401 && x <= 2050 => 4,
                _ => 5,
            };
        }

        private static string GetFormat(Dictionary<string, string> parameters)
        {
            string format = parameters["format"].ToLower();
            if (format == "modern")
            {
                return format;
            }
            else
            {
                return "wild";
            }
        }
    }
}
