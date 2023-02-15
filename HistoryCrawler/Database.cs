using Dapper;
using HistoryCrawler.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using Npgsql.PostgresTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HistoryCrawler.Util;

namespace HistoryCrawler
{

    internal class Database
    {
        public readonly NpgsqlConnection ConnectionCrawler;
        public readonly NpgsqlConnection ConnectionDatabase;
        private readonly CancellationToken CancellationToken;
        private readonly StringBuilder stringBuilder;
        public readonly ConcurrentQueue<(User user, List<Game> games)> GameQueue;
        private static readonly string[] GAME_TYPE_PREFIXES = { "modern", "wild" };

        public Database(NpgsqlConnection connection1, NpgsqlConnection connection2, CancellationToken cancellationToken)
        {
            ConnectionCrawler = connection1 ?? throw new ArgumentNullException(nameof(connection1));
            ConnectionDatabase = connection2 ?? throw new ArgumentNullException(nameof(connection2));
            GameQueue = new();
            CancellationToken = cancellationToken;
            SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL);
            stringBuilder = new StringBuilder();
        }

        public async Task QueueLoop()
        {
            try
            {

                Stopwatch stopwatch = new();
                int counter = 0;
                while (GameQueue.Any() || !CancellationToken.IsCancellationRequested)
                // TODO: add while CurrentConnections > 0
                // TODO: Crawler would probably a better place for this method
                {
                    // You probably want to lower this if your RAM is less than 24GB
                    if (GameQueue.Count < 50)
                    {
                        await Task.Delay(1000);
                    }
                    else
                    {
                        // it might be better to put this all into one transaction but I'm not that good with sql :D
                        ConnectionDatabase.Open();
                        Helper.WriteLogLine($"GameQueue Count: " + GameQueue.Count);
                        var result = GameQueue.DequeueChunk(100).ToArray();
                        stopwatch.Restart();
                        var games = InsertNewGames(result);
                        Helper.WriteLogLine($"InsertNewGames: {stopwatch.ElapsedMilliseconds} MS for {games.Count} games");
                        stopwatch.Restart();
                        //var team_Games = InsertTeams(games);
                        //Helper.WriteLogLine($"InserTeams: {stopwatch.ElapsedMilliseconds} MS for {team_Games.Count} teams");
                        //stopwatch.Restart();
                        //int inserted = AddTeamCardsLegacy(games);
                        int inserted = AddTeamCards(games);
                        Helper.WriteLogLine($"AddTeamCards: {stopwatch.ElapsedMilliseconds} MS for {inserted} cards");
                        stopwatch.Restart();
                        int updated = AddOrUpdateUsers(result);
                        Helper.WriteLogLine($"AddOrUpdateUsers: {stopwatch.ElapsedMilliseconds} MS for {updated} users");
                        stopwatch.Stop();
                        ConnectionDatabase.Close();
                        //gameIdPairs = null;

                        if (counter++ > 10 || (GameQueue.Count >= 100 && counter >= 3))
                        {
                            var memory = GC.GetTotalMemory(false);
                            //if (memory > 22000000000)
                            // 7000000000 = 7GB
                            // you probably want to lower this if you have less than 24GB RAM
                            // this is probably the only time I've ever had a case where it was needed to call the garbage collector manually
                            // the reason for this is that we are using large objects:
                            // https://stackoverflow.com/questions/10016541/garbage-collection-not-happening-even-when-needed
                            if (memory > 7000000000)
                            {
                                Console.WriteLine("Memory used before collection:       {0:N0}",
                                GC.GetTotalMemory(false));
                                GC.Collect(2);
                                Console.WriteLine("Memory used after full collection:   {0:N0}",
                                GC.GetTotalMemory(true));
                            }

                            counter = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Helper.WriteErrorLogLine(ex.ToString());
            }
            finally
            {
                CloseConnection();
            }
            Helper.WriteLogLine("Stopped!");
        }

        private int AddTeamCards(List<Game> games)
        {
            var affectedRows = 0;
            if (games == null || games.Count == 0) return 0;
            int maxGamesPerInsert = 100;
            string startSql = @"WITH input_rows(""GameId"", ""TeamHash"", ""Result"", ""Position"", ""CardId"", ""Level"") AS (VALUES";
            foreach (var gameType in GAME_TYPE_PREFIXES)
            {
                var gamesFiltered = games.Where(x => x.Format == gameType).ToArray();
                if (!gamesFiltered.Any())
                {
                    continue;
                }
                string endSql = @")
	        , ins1 AS (
	           INSERT INTO """ + gameType + @"_Team_Game"" (""GameId"", ""TeamHash"", ""Result"") 
	           SELECT DISTINCT ""GameId"", ""TeamHash"", ""Result""
	           FROM   input_rows
                ON CONFLICT(""GameId"", ""Result"", ""TeamHash"") DO NOTHING
	           )
	        , ins2 AS (
	           INSERT INTO ""Card"" (""CardId"", ""Level"") 
	           SELECT DISTINCT ""CardId"", ""Level""
	           FROM   input_rows
	           ON     CONFLICT (""CardId"", ""Level"") DO NOTHING
	           RETURNING ""Id"", ""CardId"", ""Level""
	           )
           , sel1 AS (
		        SELECT ""Id"", ""CardId"", ""Level""
		        FROM ins2
		        UNION ALL
		        SELECT c.""Id"", c.""CardId"", c.""Level""
		        FROM input_rows
		        JOIN ""Card"" c USING (""CardId"", ""Level"")
           )
	        INSERT INTO """ + gameType + @"_Team_Card"" (""TeamHash"", ""CardId"", ""Position"")
	        SELECT DISTINCT d.""TeamHash"", sel1.""Id"", d.""Position""
	        FROM   input_rows d
	        JOIN   sel1 USING (""CardId"", ""Level"")
            ON CONFLICT (""TeamHash"", ""CardId"", ""Position"") DO NOTHING;";

                HashSet<string> duplicates = new();

                stringBuilder.Clear();
                stringBuilder.AppendLine("BEGIN TRANSACTION;");
                bool firstRow = true;
                for (int i = 0; i < gamesFiltered.Length; i++)
                {
                    if (i % maxGamesPerInsert == 0)
                    {
                        if (i != 0)
                        {
                            stringBuilder.AppendLine(endSql);
                        }
                        stringBuilder.AppendLine(startSql);
                        firstRow = true;
                    }
                    var game = gamesFiltered[i];
                    //if (duplicates.Contains(game.QueueIdHash))
                    //{
                    //    continue;
                    //}
                    //duplicates.Add(game.QueueIdHash);

                    JToken details = game.DetailsJson;

                    string[] teamStrings = { "team1", "team2" };
                    foreach (var team in teamStrings)
                    {
                        string result;
                        if (game.Result == "D")
                        {
                            result = "D";
                        }
                        else if (game.Winner == (string)details[team]!["player"]!)
                        {
                            result = "W";
                        }
                        else
                        {
                            result = "L";
                        }

                        if (details[team] == null)
                        {
                            continue;
                        }
                        int[][] monsters = ((JArray)details[team]["monsters"]).Select(x => new int[] { (int)x["card_detail_id"], (int)x["level"] }).ToArray();
                        int[] summoner = new int[] { (int)details[team]["summoner"]["card_detail_id"], (int)details[team]["summoner"]["level"] };

                        string teamHashString = "";
                        for (int ii = -1; ii < monsters.Length; ii++)
                        {
                            var cardId = Convert.ToInt32(ii == -1 ? summoner[0] : monsters[ii][0]);
                            var level = ii == -1 ? summoner[1] : monsters[ii][1];
                            var position = ii + 1;

                            teamHashString += $"{cardId}-{level}-{position};";
                        }
                        long teamHash = Helper.GetInt64HashCode(teamHashString);

                        for (int ii = -1; ii < monsters.Length; ii++)
                        {
                            var cardId = Convert.ToInt32(ii == -1 ? summoner[0] : monsters[ii][0]);
                            var level = ii == -1 ? summoner[1] : monsters[ii][1];
                            var position = ii + 1;

                            if (firstRow)
                            {
                                firstRow = false;
                                stringBuilder.AppendLine($"(integer '{game.Id}', bigint '{teamHash}', text '{result}', integer '{position}', integer '{cardId}', integer '{level}')");
                            }
                            else
                            {
                                stringBuilder.AppendLine($",({game.Id}, {teamHash}, '{result}', {position}, {cardId}, {level})");
                            }
                        }
                    }
                }

                stringBuilder.AppendLine(endSql);
                stringBuilder.AppendLine("COMMIT;");

                affectedRows += ConnectionDatabase.Execute(stringBuilder.ToString());
            }
            return affectedRows;
        }

        //private List<Team_Game> InsertTeams(List<Game> games)
        //{
        //    List<Team_Game> teamGame = new List<Team_Game>();
        //    if (games.Count == 0) return teamGame;
        //    var sqlEnd = @" RETURNING ""Id""";

        //    // // https://stackoverflow.com/a/42217872
        //    string sql = @"INSERT INTO ""Team"" (""Player"") VALUES";

        //    foreach (var game in games)
        //    {
        //        sql += $"('{game.Winner}'),('{game.Loser}'),";
        //    }

        //    sql = sql[..^1] + sqlEnd;
        //    var teamIds = GetQueryList<int>(ConnectionDatabase, sql);

        //    var index = 0;
        //    foreach (var game in games)
        //    {
        //        var result = game.Result == "D" ? "D1" : "W";
        //        teamGame.Add(new() { Game = game, GameId = game.Id, Result = result, TeamId = teamIds[index++] });
        //        result = game.Result == "D" ? "D2" : "L";
        //        teamGame.Add(new() { Game = game, GameId = game.Id, Result = result, TeamId = teamIds[index++] });
        //    }

        //    return teamGame;
        //}

        public List<T> GetQueryList<T>(NpgsqlConnection connection, string query = "")
        {
            query = (query.ToUpper().StartsWith("SELECT") || query.ToUpper().StartsWith("UPDATE") || query.ToUpper().StartsWith("INSERT") || 
                query.ToUpper().StartsWith("WITH")) ? query : "SELECT * FROM \"" + typeof(T).Name + "\" " + query;
            var result = connection.Query<T>(query);
            return result.ToList();
        }

        public void CloseConnection()
        {
            ConnectionCrawler.Close();
            ConnectionDatabase.Close();
        }

        public List<Game> InsertNewGames(IEnumerable<(User user, List<Game> games)> ps)
        {
            try
            {
                ConcurrentDictionary<string, ConcurrentDictionary<string, Game>> games = new();

                Dictionary<string, string> sqlEnd = new();
                Dictionary<string, string> sql = new();
                for (int i = 0; i < GAME_TYPE_PREFIXES.Length; i++)
                {
                    var gameType = GAME_TYPE_PREFIXES[i];
                    games.TryAdd(gameType, new());

                    sqlEnd.Add(gameType, @")
            , ins AS (
               INSERT INTO """ + GAME_TYPE_PREFIXES[i] + @"_Game"" (""QueueIdHash"", ""QueueId1"", ""Winner"", ""Loser"", ""Rating"", ""CreatedDate"", ""MatchType"", ""ManaCap"", ""Ruleset1"", ""Ruleset2"", ""Inactive"", ""TournamentSettings"") 
               SELECT * FROM input_rows
               ON CONFLICT (""QueueIdHash"", ""QueueId1"") DO NOTHING
               RETURNING ""Id"", ""QueueIdHash""
               )
                SELECT 'i' AS ""Source""
                        , ""Id"", ""QueueIdHash""
                FROM   ins
                UNION  ALL
                SELECT 's' AS ""Source""
                        , g.""Id"", g.""QueueIdHash""
                FROM   input_rows
                JOIN   """ + GAME_TYPE_PREFIXES[i] + @"_Game"" g USING (""QueueIdHash"", ""QueueId1"");");

                    // https://stackoverflow.com/a/42217872
                    sql.Add(gameType, @"WITH input_rows(""QueueIdHash"", ""QueueId1"", ""Winner"", ""Loser"", ""Rating"", ""CreatedDate"", ""MatchType"", ""ManaCap"", ""Ruleset1"", ""Ruleset2"", ""Inactive"", ""TournamentSettings"") AS (
                                VALUES");
                }

                //bool firstRow = true;

                // first row
                for (int i = 0; i < ps.Count(); i++)
                {
                    if (games.All(x => x.Value.Any()))
                    {
                        break;
                    }
                    if (ps.ElementAt(i).games.Any())
                    {
                        for (int ii = 0; ii < GAME_TYPE_PREFIXES.Length; ii++)
                        {
                            var gameType = GAME_TYPE_PREFIXES[ii];
                            if (games[gameType].IsEmpty)
                            {
                                var firstGame = ps.ElementAt(i).games.Find(x => x.Format == gameType);
                                if (firstGame == null)
                                {
                                    continue;
                                }
                                sql[gameType] += $"(text '{firstGame.QueueIdHash}', text '{firstGame.QueueId1}', text '{firstGame.Winner}', text '{firstGame.Loser}', integer '{firstGame.Rating}', timestamp '{firstGame.CreatedDate}', text '{firstGame.MatchType}', integer '{firstGame.ManaCap}', text '{firstGame.Ruleset1}', text '{firstGame.Ruleset2}', text '{firstGame.Inactive}', text '{firstGame.TournamentSettings}')";
                                games[gameType].TryAdd(firstGame.QueueIdHash, firstGame);
                                ps.ElementAt(i).games.Remove(firstGame);
                            }
                        }
                    }
                }

                //Stopwatch stopwatch = new();
                //stopwatch.Start();
                Parallel.ForEach(ps, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, userGames =>
                {
                    userGames.games.ForEach(game =>
                    {
                        if (game.IsSurrender)
                        {
                            return;
                        }

                        sql[game.Format] += $",('{game.QueueIdHash}','{game.QueueId1}','{game.Winner}','{game.Loser}',{game.Rating},'{game.CreatedDate}','{game.MatchType}',{game.ManaCap},'{game.Ruleset1}','{game.Ruleset2}','{game.Inactive}','{game.TournamentSettings}')";

                        games[game.Format].TryAdd(game.QueueIdHash, game);
                    });
                });


                var insertedGames = new List<Game>();
                foreach (var gameType in GAME_TYPE_PREFIXES)
                {
                    if (games[gameType].IsEmpty)
                    {
                        continue;
                    }
                    sql[gameType] += sqlEnd[gameType];

                    var gamesWithId = GetQueryList<BulkInsertId>(ConnectionDatabase, sql[gameType]);

                    for (int i = 0; i < gamesWithId.Count; i++)
                    {
                        if (gamesWithId[i].Source == "i")
                        {
                            var game = games[gameType][gamesWithId[i].QueueIdHash];
                            game.Id = gamesWithId[i].Id;
                            insertedGames.Add(game);
                        }
                    }
                }
                return insertedGames;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return new();
        }

        public int AddOrUpdateUsers(IEnumerable<(User user, List<Game> games)> userWithGames)
        {
            if (userWithGames == null || !userWithGames.Any()) return 0;
            var now = DateTime.Now;
            ConcurrentDictionary<string, string> usersWild = new();
            ConcurrentDictionary<string, string> usersModern = new();

            Parallel.ForEach(userWithGames, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, (userGames) =>
            {
                foreach (var game in userGames.games)
                {
                    var name = game.Winner == userGames.user.Name ? game.Loser : game.Winner;
                    var rating = game.Rating;
                    var ratingLevel = game.RatingLevel;
                    var addedByUsername = "CRAWLER_AUTO";
                    var lastRatingUpdate = now;
                    //var lastCrawlTime = null;
                    var sql = $"('{name}',{rating},{ratingLevel},'{addedByUsername}','{lastRatingUpdate}'),";
                    if (game.IsWild)
                    {
                        usersWild.TryAdd(name, sql);
                    }
                    else if (game.IsModern)
                    {
                        usersModern.TryAdd(name, sql);
                    }
                }
            });

            var sqlWild = @"INSERT INTO ""User"" (""Name"", ""RatingWild"", ""RatingLevelWild"", ""AddedByUsername"", ""LastRatingUpdate"") VALUES ";
            var sqlModern = @"INSERT INTO ""User"" (""Name"", ""RatingModern"", ""RatingLevelModern"", ""AddedByUsername"", ""LastRatingUpdate"") VALUES ";
            var affectedRows = 0;
            if (usersWild.Any())
            {
                foreach (var value in usersWild.Values)
                {
                    sqlWild += value;
                }
                sqlWild = sqlWild[..^1] + "ON CONFLICT(\"Name\") DO UPDATE SET \"RatingWild\" = EXCLUDED.\"RatingWild\", \"RatingLevelWild\" = EXCLUDED.\"RatingLevelWild\", \"LastRatingUpdate\"=EXCLUDED.\"LastRatingUpdate\"";

                affectedRows += ConnectionDatabase.Execute(sqlWild);
            }

            if (usersModern.Any())
            {
                foreach (var value in usersModern.Values)
                {
                    sqlModern += value;
                }
                sqlModern = sqlModern[..^1] + "ON CONFLICT(\"Name\") DO UPDATE SET \"RatingModern\" = EXCLUDED.\"RatingModern\", \"RatingLevelModern\" = EXCLUDED.\"RatingLevelModern\", \"LastRatingUpdate\"=EXCLUDED.\"LastRatingUpdate\"";

                affectedRows += ConnectionDatabase.Execute(sqlModern);
            }

            return affectedRows;
        }

    //    public void BulkInsertTeamCardsLegacy(List<Card> teamCards)
    //    {
    //        using (var writer = ConnectionDatabase.BeginBinaryImport(
    //@"copy ""Card""(""GameId"", ""Result"", ""CardId"", ""Gold"", ""Level"", ""Position"") from STDIN (FORMAT BINARY)"))
    //        {
    //            foreach (var card in teamCards)
    //            {
    //                if (card.GameId == null)
    //                {
    //                    continue;
    //                }
    //                writer.StartRow();
    //                writer.Write((int)card.GameId);
    //                writer.Write(card.Result);
    //                writer.Write(card.CardId);
    //                writer.Write(card.Gold);
    //                writer.Write(card.Level);
    //                writer.Write(card.Position);
    //                //writer.Write(record.HireDate, NpgsqlTypes.NpgsqlDbType.Date);
    //            }

    //            writer.Complete();
    //        }
    //    }

        //public int AddTeamCardsLegacy(List<Game> games)
        //{
        //    var teamCardList = new List<Card>();
        //    foreach (var game in games)
        //    {
        //        JToken details = game.DetailsJson;
        //        int[][] monstersTeam1 = ((JArray)details["team1"]["monsters"]).Select(x => new int[] { (int)x["card_detail_id"], (string)x["gold"] == "true" ? 1 : 0, (int)x["level"] }).ToArray();
        //        int[][] monstersTeam2 = ((JArray)details["team2"]["monsters"]).Select(x => new int[] { (int)x["card_detail_id"], (string)x["gold"] == "true" ? 1 : 0, (int)x["level"] }).ToArray();
        //        int[] summonerTeam1 = new int[] { (int)details["team1"]["summoner"]["card_detail_id"], (string)details["team1"]["summoner"]["gold"] == "true" ? 1 : 0, (int)details["team1"]["summoner"]["level"] };
        //        int[] summonerTeam2 = new int[] { (int)details["team2"]["summoner"]["card_detail_id"], (string)details["team2"]["summoner"]["gold"] == "true" ? 1 : 0, (int)details["team2"]["summoner"]["level"] };

        //        var result = (string)details["team1"]!["player"]! == game.Winner ? "W" : "L";
        //        for (int i = -1; i < monstersTeam1.Length; i++)
        //        {
        //            teamCardList.Add(new()
        //            {
        //                GameId = game.Id,
        //                CardId = Convert.ToInt32(i == -1 ? summonerTeam1[0] : monstersTeam1[i][0]),
        //                Gold = i == -1 ? summonerTeam1[1] : monstersTeam1[i][1],
        //                Level = i == -1 ? summonerTeam1[2] : monstersTeam1[i][2],
        //                Position = i + 1,
        //                Result = result
        //            });
        //        }

        //        result = result == "L" ? "W" : "L"; // umdrehen damit es bei draws W und L gibt
        //        for (int i = -1; i < monstersTeam2.Length; i++)
        //        {
        //            teamCardList.Add(new()
        //            {
        //                GameId = game.Id,
        //                CardId = Convert.ToInt32(i == -1 ? summonerTeam2[0] : monstersTeam2[i][0]),
        //                Gold = i == -1 ? summonerTeam2[1] : monstersTeam2[i][1],
        //                Level = i == -1 ? summonerTeam2[2] : monstersTeam2[i][2],
        //                Position = i + 1,
        //                Result = result
        //            });
        //        }
        //    }
        //    BulkInsertTeamCardsLegacy(teamCardList);
        //    return teamCardList.Count;
        //}

        /*public async Task AddTeamCardsLegacy(Dictionary<Game, int> gameIdPairs)
        {
            // we can bulk insert here because we don't need the ids returned
            using (var transaction = ConnectionDatabase.BeginTransaction())
            {
                var command = ConnectionDatabase.CreateCommand();
                command.CommandText =
                @"
        INSERT INTO ""Card"" (""GameId"", ""Result"", ""CardId"", ""Gold"", ""Level"", ""Position"")
        VALUES ($1,$2,$3,$4,$5,$6)
    ";

                var tableName = command.CreateParameter();
                //tableName.ParameterName = "tableName";
                tableName.Value = "Card";
                var game = command.CreateParameter();
                //game.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text;
                //game.ParameterName = "1";
                var result = command.CreateParameter();
                //result.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text;
                //result.ParameterName = "3";
                var card = command.CreateParameter();

                //card.ParameterName = "2";
                //card.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer;
                var gold = command.CreateParameter();
                //card.ParameterName = "4";
                //gold.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer;
                var level = command.CreateParameter();
                //card.ParameterName = "5";
                //level.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer;
                var position = command.CreateParameter();
                //position.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer;
                //position.ParameterName = "position";
                command.Parameters.Add(game);
                command.Parameters.Add(result);
                command.Parameters.Add(card);
                command.Parameters.Add(gold);
                command.Parameters.Add(level);
                command.Parameters.Add(position);

                foreach (var gameIdPair in gameIdPairs)
                {
                    JToken details = gameIdPair.Key.DetailsJson;
                    int[][] monstersTeam1 = ((JArray)details["team1"]["monsters"]).Select(x => new int[] { (int)x["card_detail_id"], (string)x["gold"] == "true" ? 1 : 0, (int)x["level"] }).ToArray();
                    int[][] monstersTeam2 = ((JArray)details["team2"]["monsters"]).Select(x => new int[] { (int)x["card_detail_id"], (string)x["gold"] == "true" ? 1 : 0, (int)x["level"] }).ToArray();
                    int[] summonerTeam1 = new int[] { (int)details["team1"]["summoner"]["card_detail_id"], (string)details["team1"]["summoner"]["gold"] == "true" ? 1 : 0, (int)details["team1"]["summoner"]["level"] };
                    int[] summonerTeam2 = new int[] { (int)details["team2"]["summoner"]["card_detail_id"], (string)details["team2"]["summoner"]["gold"] == "true" ? 1 : 0, (int)details["team2"]["summoner"]["level"] };

                    game.Value = gameIdPair.Value;
                    result.Value = (string)details["team1"]!["player"]! == gameIdPair.Key.Winner ? "W" : "L";
                    for (int i = -1; i < monstersTeam1.Length; i++)
                    {
                        card.Value = Convert.ToInt32(i == -1 ? summonerTeam1[0] : monstersTeam1[i][0]);
                        gold.Value = i == -1 ? summonerTeam1[1] : monstersTeam1[i][1];
                        level.Value = i == -1 ? summonerTeam1[2] : monstersTeam1[i][2];
                        position.Value = i + 1;
                        await command.ExecuteNonQueryAsync();
                    }

                    result.Value = (string)result.Value == "L" ? "W" : "L"; // umdrehen damit es bei draws W und L gibt
                    for (int i = -1; i < monstersTeam2.Length; i++)
                    {
                        card.Value = i == -1 ? summonerTeam2[0] : monstersTeam2[i][0];
                        gold.Value = i == -1 ? summonerTeam2[1] : monstersTeam2[i][1];
                        level.Value = i == -1 ? summonerTeam2[2] : monstersTeam2[i][2];
                        position.Value = i + 1;
                        await command.ExecuteNonQueryAsync();
                    }
                }

                await transaction.CommitAsync();
            }
        }
        */
    }
}
