using Dapper;
using HistoryCrawler.Model;
using HistoryCrawler.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HistoryCrawler
{
    internal class Crawler
    {
        // An account can only be crawled every n hours
        private const int BLOCK_HOURS_AFTER_CRAWLING = -4; // make sure this value is negative
        // only query accounts that had their rating updated by the crawler in the last N days
        // if an account doesn't have their rating updated for a long time it's either because the crawler didn't run
        // or the account is inactive
        private const int OLDEST_ALLOWED_RATING_UPDATE_DAYS = -90; // make sure this value is negative
        private const int USERS_PER_LOOP = 950;
        private WebRequest Request;
        private Database Database;
        private readonly CancellationToken CancellationToken;

        public Crawler(WebRequest request, Database database, CancellationToken cancellationToken)
        {
            Request = request;
            Database = database;
            CancellationToken = cancellationToken;
        }

        public async Task Loop()
        {
            int blockHoursModified = BLOCK_HOURS_AFTER_CRAWLING;
            int oldestAllowedRatingUpdateDaysModified = OLDEST_ALLOWED_RATING_UPDATE_DAYS;
            while (!CancellationToken.IsCancellationRequested)
            {
                try
                {
                    // automatic gamecache update removed because it takes too long to execute the query
                    // if the database is too large
                    //UpdateCache();
                    var timeNow = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    Database.ConnectionCrawler.Open();

                    //NO MIN RATING for users to query
                    string query = @"UPDATE ""User""
                            SET ""LastCrawlTime"" = '" + timeNow + @"'
                            WHERE ""Id"" IN (SELECT ""Id""
                                         FROM ""User""
                                         WHERE (""LastCrawlTime"" <= " + $"'{ DateTime.Now.AddHours(blockHoursModified):yyyy-MM-dd HH:mm:ss}'" + @" OR ""LastCrawlTime"" IS NULL)
                                         AND ""LastRatingUpdate"" >= " + $"'{ DateTime.Now.AddDays(oldestAllowedRatingUpdateDaysModified):yyyy-MM-dd HH:mm:ss}'" + @"
                                         LIMIT " + USERS_PER_LOOP.ToString() + @")
                            RETURNING *;";

                    // with 275 min Rating
                    //string query = @"UPDATE ""User""
                    //        SET ""LastCrawlTime"" = '" + timeNow + @"'
                    //        WHERE ""Id"" IN (SELECT ""Id""
                    //                     FROM ""User""
                    //                     WHERE (""LastCrawlTime"" <= " + $"'{ DateTime.Now.AddHours(blockHoursModified):yyyy-MM-dd HH:mm:ss}'" + @" OR ""LastCrawlTime"" IS NULL)
                    //                     AND ""LastRatingUpdate"" >= " + $"'{ DateTime.Now.AddDays(oldestAllowedRatingUpdateDaysModified):yyyy-MM-dd HH:mm:ss}'" + @"
                    //                     AND (""RatingWild"" > 275 OR ""RatingModern"" > 275)
                    //                     LIMIT " + USERS_PER_LOOP.ToString() + @")
                    //        RETURNING *;";

                    var users = Database.GetQueryList<User>(Database.ConnectionCrawler, query);
                    if (users.Count == 0)
                    {
                        Helper.WriteLogLine("no users to crawl");
                        await Task.Delay(5000, CancellationToken);
                        blockHoursModified = Math.Min(-1, ++blockHoursModified);
                        oldestAllowedRatingUpdateDaysModified = oldestAllowedRatingUpdateDaysModified - 10;
                        continue;
                    }
                    else
                    {
                        blockHoursModified = BLOCK_HOURS_AFTER_CRAWLING;
                        oldestAllowedRatingUpdateDaysModified = OLDEST_ALLOWED_RATING_UPDATE_DAYS;
                    }

                    int allStarterGames = 0;
                    await Helper.ForEachAsync(users, 20, async (user) =>
                    {
                        var battleHistoryJson = await GetBattleHistoryAsync(user.Name);

                        List<Game>? games = new();
                        if (battleHistoryJson != null && battleHistoryJson["battles"] != null)
                        {
                            foreach (JToken gameJson in (JArray)battleHistoryJson["battles"]!)
                            {
                                int rating = (int)gameJson["player_1_rating_initial"];
                                // !!!
                                // skip games with less than 350 rating
                                if (rating > 350)
                                {
                                    var game = new Game(gameJson);
                                    string type = (string)game.DetailsJson["type"];

                                    // skip surrenders and challenges
                                    if (type == "Surrender" || game.MatchType == "Challenge")
                                    {
                                        continue;
                                    }

                                    // skip non-ranked games (e.g. tournaments)
                                    if (!game.IsWild && !game.IsModern)
                                    {
                                        continue;
                                    }

                                    string[] monstersTeam1 = (game.DetailsJson["team1"]["monsters"] as JArray).Select(x => (string)x["uid"]).ToArray();
                                    string[] monstersTeam2 = (game.DetailsJson["team2"]["monsters"] as JArray).Select(x => (string)x["uid"]).ToArray();

                                    // only add games that have at least one card that is no starter card
                                    if (monstersTeam1.Any(x => !x.StartsWith("start")) || monstersTeam2.Any(x => !x.StartsWith("start")))
                                    {
                                        games.Add(game);
                                    }
                                    else
                                    {
                                        allStarterGames++;
                                    }
                                }
                            }
                        }

                        Database.GameQueue.Enqueue((user, games));
                        while (Database.GameQueue.Count > 150)
                        {
                            await Task.Delay(5000);
                            //Helper.WriteLogLine("GameQueue: " + Database.GameQueue.Count);
                        }
                    }).ConfigureAwait(false);
                    Helper.WriteLogLine("Ignored All Starter Games: " + allStarterGames.ToString());
                    Helper.WriteLogLine("UserLoop finished");
                }
                catch (Exception ex)
                {
                    Helper.WriteLogLine("CrawlerLoop Error: " + ex.ToString());
                    await Task.Delay(15000);
                }
                finally
                {
                    Database.ConnectionCrawler.Close();
                }
            }
        }


        private async Task<JToken?> GetBattleHistoryAsync(string username)
        {
            try
            {
                string data = await Request.GetPageAsync($"https://api2.splinterlands.com/battle/history?player={ username }&format=wild").ConfigureAwait(false);
                if (data == "")
                {
                    await Task.Delay(10000);
                    data = await Request.GetPageAsync($"https://api2.splinterlands.com/battle/history?player={ username }&format=wild").ConfigureAwait(false);
                }
                if (data == "")
                {
                    return null;
                }
                JToken userHistory = JToken.Parse(data);
                return userHistory;
            }
            catch (Exception ex)
            {
                Helper.WriteLogLine(ex.ToString());
                Thread.Sleep(5000);
                return null;
            }
        }
    }
}
