using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ultimate_Splinterlands_Bot_API.Model;
using System.Globalization;
using Npgsql;
using Dapper;
using Ultimate_Splinterlands_Bot_API.Util;

namespace Ultimate_Splinterlands_Bot_API.Api
{
    internal static class TeamGeneration
    {
        private static CardSettings DefaultCardSettings = new CardSettings("USE_CARD_SETTINGS=false");
        private static Dictionary<string, string> Summoners = new Dictionary<string, string>();
        public static Dictionary<(int, int), int> CardIds = new();
        public static JArray CardsDetails;
        private static List<DetailedCard> DetailedCards;
        private static DetailedCard[] StarterCards;
        private static string[] MobsWithSneak;
        private static string[] MobsWithSnipe;
        private static int[] ZeroManaMobs;
        private static string ConnectionString;
        private static string TeamSelectQuery;

        // Note from PC Jones:
        // This is the method where most of the logic happens, and it's quite a mess
        // as I had to add new features or workarounds often without really having the
        // time to do it in a proper way 
        // I've tried to added comments explaining what happens when I was able to actually
        // remember why I did certain things lol

        // It would probably best to code this again from scratch to make this readable and
        // understandable as well as to get rid of the weird
        // TeamResponseProperty (NO idea why I did it this way tbh) and to use proper prepared
        // SQL statements (I tried to do it back when I created the API, but for some reason
        // it didn't work with prepared statements - keep in mind I basically coded this in
        // one night and really wanted to finish it :D)

        // All in all GetTeamAsync should probably be recoded to a non static class so that
        // an object can be created for every request - that will avoid the recursion
        // (GetTeamAsync calling itself multiple times) 
        public static async Task<TeamResponseProperty[]?> GetTeamAsync(string postData, int ratingBracket, int rating, string format, bool forceSingleRuleset, ApiRequestData? apiRequestData = null, CardSettings? cardSettings = null, int runCount = 1, bool ignoreFocus = false)
        {
            if (apiRequestData == null)
            {
                apiRequestData = JsonConvert.DeserializeObject<ApiRequestData>(postData);
                if (forceSingleRuleset)
                {
                    // TODO: if one of these rulesets is present use these instead to avoid invalid teams
                    /*
                        Broken Arrows
                        Even Stevens
                        Keep Your Distance
                        Little League
                        Lost Legendaries
                        Lost Magic
                        Odd Ones Out
                        Rise of the Commons
                        Taking Sides
                    */
                    apiRequestData.Rulesets = apiRequestData.Rulesets.Contains('|') ? apiRequestData.Rulesets.Split('|')[1] : "Standard";
                }
            }

            // on 4th try force to play starter cards if they are disabled in card settings
            bool forceStarterCards = runCount == 4 && !cardSettings!.PLAY_STARTER_CARDS;

            // Abort team generation after 3 tries unless force starter cards is true (because it will try one more time without starter cards, see above)
            if (runCount >= 4 && !forceStarterCards || runCount > 4)
            {
                return null;
            }
            else if (runCount == 3 && cardSettings != null)
            {
                // On third try set preferred summoner elements to all
                // (if there is a 4th try with forced starter cards preferred summoners will still be set to all from this)
                cardSettings.PREFERRED_SUMMONER_ELEMENTS = DefaultCardSettings.PREFERRED_SUMMONER_ELEMENTS;
                cardSettings.PREFERRED_SUMMONER_ELEMENTS_WINRATE_THRESHOLD = DefaultCardSettings.PREFERRED_SUMMONER_ELEMENTS_WINRATE_THRESHOLD;
            }

            if (cardSettings == null)
            {
                // on first try get card settings from the request or use default settings
                cardSettings = apiRequestData.CardSettings ?? DefaultCardSettings;
            }

            if (forceStarterCards)
            {
                cardSettings.PLAY_STARTER_CARDS = true;
            }

            // Accounts with a rating under 550 will cap WINRATE_MODIFIER_OWNED_CARD_PERCENTAGE to 6 and FLAT_NEGATIVE_MODIFIER_PER_UNOWNED_CARD to 0.25
            // This is because there is no need to prefer own cards as you will earn almost nothing below 550 rating.
            if (rating < 550)
            {
                cardSettings.WINRATE_MODIFIER_OWNED_CARD_PERCENTAGE = Math.Min(6, cardSettings.WINRATE_MODIFIER_OWNED_CARD_PERCENTAGE);
                cardSettings.FLAT_NEGATIVE_MODIFIER_PER_UNOWNED_CARD = Math.Min(0.25, cardSettings.FLAT_NEGATIVE_MODIFIER_PER_UNOWNED_CARD);
            }

            bool chestTierReached = (bool)(apiRequestData.ChestTierReached == null ? false : apiRequestData.ChestTierReached);

            string[] allowedColors = GetAllowedColorsAndQuestPriority(apiRequestData, cardSettings, ignoreFocus, out bool questPriority);
            if (runCount == 1)
            {
                if (!questPriority)
                {
                    // if no quest priority in try 1 then we pretend to already be in the second try
                    runCount++;
                }
            }

            if (allowedColors.Length == 0)
            {
                // set runCount +1 and try again!
                return await GetTeamAsync(postData, ratingBracket, rating, format, forceSingleRuleset, apiRequestData, cardSettings, ++runCount, ignoreFocus: true);
            }

            PlayerCard[] playerCards;
            // filter non starter cards if activated
            if (!cardSettings.PLAY_STARTER_CARDS)
            {
                var playerCardsWithoutStarters = apiRequestData.Cards.Where(x => !x.starter).ToArray();
                playerCards = playerCardsWithoutStarters;
            }
            else
            {
                playerCards = apiRequestData.Cards;
            }

            // get all cards in allowedColors + all neutral ones
            PlayerCard[] playableCards = playerCards.Where(
                x => x.card_detail_id != ""
                && (x.GetCardColor() == "neutral" || allowedColors.Contains(x.GetCardColor()))
            ).ToArray();

            // build a string of the card ids for the database query
            string internalCardIds = "";
            foreach (var card in playableCards)
            {
                int cardIdInt = Convert.ToInt32(card.card_detail_id);
                int minLevel = 1;
                for (int i = card.level; i >= minLevel; i--)
                {
                    if (CardIds.TryGetValue((cardIdInt, i), out int internalCardId))
                    {
                        internalCardIds += "(" + internalCardId + "),";
                    }
                }
            }

            // actual database stuff happens inside GetTeamsFromDatabase()
            var teamResponsesFiltered = GetTeamsFromDatabase(ratingBracket, apiRequestData, internalCardIds, playableCards, cardSettings, questPriority, chestTierReached, format);

            // no valid team found
            if (teamResponsesFiltered == null || teamResponsesFiltered.Length == 0)
            {
                return await GetTeamAsync(postData, ratingBracket, rating, format, forceSingleRuleset, apiRequestData, cardSettings, ++runCount, ignoreFocus: true);
            }

            int TAKE_AMOUNT = 10; // only take the best N teams in consideration
            int teamAmount = teamResponsesFiltered.Length;
            int teamRank = Globals.Random.Next(0, Math.Min(teamAmount, TAKE_AMOUNT)); // pick a random team between 0 and TAKE_AMOUNT

            // Add bias towards better ranked teams
            // basically if the team picked is a lower ranked one then we will try again up to two times
            for (int i = 0; i < 2; i++)
            {
                var temp = teamRank > teamAmount / 3;
                if (teamRank > teamAmount / 3)
                {
                    teamRank = Globals.Random.Next(0, Math.Min(teamAmount, TAKE_AMOUNT));
                }
                else
                {
                    break;
                }
            }

            // Okay, we finally got a team!
            // Now let's prepare the response
            var finalTeam = CreateTeamResponsePropertyTeam(teamResponsesFiltered[teamRank], teamRank, questPriority, cardSettings, playableCards, apiRequestData, forceSingleRuleset);
            return finalTeam;
        }

        private static CachedTeamResponse[]? GetTeamsFromDatabase(int ratingBracket, ApiRequestData apiRequestData, string internalCardIds, PlayerCard[] playableCards, CardSettings cardSettings, bool questPriority, bool chestTierReached, string format)
        {
            double winrateThreshold;
            if (questPriority)
            {
                winrateThreshold = cardSettings.USE_FOCUS_ELEMENT_WINRATE_THRESHOLD;
            }
            else if (cardSettings.PREFERRED_SUMMONER_ELEMENTS.Length <= 5) // if not all all elements accepted
            {
                winrateThreshold = cardSettings.PREFERRED_SUMMONER_ELEMENTS_WINRATE_THRESHOLD;
            }
            else
            {
                winrateThreshold = 0;
            }

            int manaCap = apiRequestData.ManaCap;
            string[] rulesets = apiRequestData.Rulesets.Split('|');
            int minGamesPlayed;

            // I really don't quite remember why I changed most of this to 5
            if (rulesets[0] == "Standard")
            {
                if (ratingBracket <= 2)
                {
                    minGamesPlayed = 5;
                    //minGamesPlayed = count >= 2 ? 500 : 5;
                    //minGamesPlayed = 1000;
                }
                else
                {
                    minGamesPlayed = 5;
                }
            }
            else
            {
                if (ratingBracket <= 2)
                {
                    minGamesPlayed = winrateThreshold > 0 ? 50 : 5;
                    //minGamesPlayed = winrateThreshold > 0 ? 800 : 50;
                }
                else
                {
                    //minGamesPlayed = winrateThreshold > 0 ? 5 : 0;
                    minGamesPlayed = 5;
                }
            }
            List<CachedTeamResponse> teamResponses = new();

            using NpgsqlConnection connection = new(ConnectionString);
            connection.Open();
            int variableRatingBracket = ratingBracket;

            // this will first try to get teams for the inital provided rating bracket (e.g. 2)
            // if it won't find a team then it will try again with rating bracket 1,
            // then again with rating bracket -1 which will search in all rating brackets
            // it's probably in this hacky way because I needed to update this asap :D sorry again
            do
            {
                if (variableRatingBracket <= 0)
                {
                    minGamesPlayed = 1;
                    // Try again with no rating limit or min game restrictions
                    winrateThreshold = 0;
                    variableRatingBracket = -1;
                    GetPlayerTeams(variableRatingBracket, manaCap, rulesets, minGamesPlayed, internalCardIds, winrateThreshold, format, teamResponses, connection);
                    if (teamResponses.Count == 0)
                    {
                        // abort
                        return null;
                    }
                }

                GetPlayerTeams(variableRatingBracket, manaCap, rulesets, minGamesPlayed, internalCardIds, winrateThreshold, format, teamResponses, connection);
                variableRatingBracket--;
            } while (teamResponses.Count == 0);

            // A whole bunch of filtering is going on here... I'll try to explain
            // All these numbers can and probably should be changed, they are all
            // based on experience and gut feeling

            // order teams by amount of games played
            var teamResponsesGamesPlayedOrderedQuery = teamResponses.OrderByDescending(x => x.GamesPlayed);

            // get the maximuim amount of played games for the team with the most played games
            int maxGamesPlayed = teamResponsesGamesPlayedOrderedQuery.First().GamesPlayed;

            // set minimum win rate to 20 if maxGamesPlayed are more than 20, otherwise to 0
            int minWinRate = maxGamesPlayed > 20 ? 20 : 0;

            // set minimum games played treshold to (maxGamesPlayed / 8) - 1
            int gamesPlayedThresholdCalculated = (maxGamesPlayed / 8) - 1;

            // if minimum games played treshold > 40 then subtract 20, if not keep it the same
            gamesPlayedThresholdCalculated = gamesPlayedThresholdCalculated > 40 ? gamesPlayedThresholdCalculated - 20 : gamesPlayedThresholdCalculated;

            // cap the games played threshold by 1500 if Standard, otherwise 900
            int maxGamesPlayedThreshold = rulesets[0] == "Standard" ? 1500 : 900;
            int gamesPlayedThreshold = Math.Max(0, Math.Min(maxGamesPlayedThreshold, gamesPlayedThresholdCalculated));

            // I have a feeling the .ToArray() is not needed but then again I remember some fishy stuff going on here...
            // Remove games below the games played threshold and below the minimum win rate
            var teamResponsesAfterTresholds = teamResponsesGamesPlayedOrderedQuery.ToArray()
                .Where(x => x.GamesPlayed >= gamesPlayedThreshold && x.WinRate >= minWinRate)
                .ToList();

            // Get cards:
            // By now we only know the team ids for every team, this will query the database for the actual cards
            // in the future this should probably be moved into a single query
            teamResponsesAfterTresholds.ForEach(x => x.GetCards(connection, cardSettings, playableCards, chestTierReached, format));

            connection.Close();

            // order the teams by SortWinRate
            // what is the SortWinRate? It's the modified WinRate after applying the WINRATE_MODIFIER settings from CardSettings
            // have a look at CachedTeamResponse.GetCards() to see how it's calculated
            var teamResponsesSortWinRateOrderedQuery = teamResponsesAfterTresholds
                .OrderByDescending(x => x.SortWinRate);

            // get the best win rate in all found teams
            double heighestSortWinRate = teamResponsesGamesPlayedOrderedQuery.First().SortWinRate;
            // set the minimum sort winrate...
            // if SortWinRate > 98 then subtract 40, if >= 89 subtract 32 etc
            double minSortWinRate = heighestSortWinRate >= 98 ? heighestSortWinRate - 40 : heighestSortWinRate >= 89 ? heighestSortWinRate - 32 : heighestSortWinRate >= 71 ? heighestSortWinRate - 20 : heighestSortWinRate >= 65 ? heighestSortWinRate - 15 : heighestSortWinRate >= 57 ? heighestSortWinRate - 10 : heighestSortWinRate >= 52 ? heighestSortWinRate - 4 : heighestSortWinRate - 2;

            // sorry if this is confusing... you are not alone!
            // Keep all teams that:
            // (
            // have a >= SortWinRate than the minSortWinRate AND
            // (
            // Less then 40 games played
            // OR
            // Have a real winrate of not less than 5%-points fewer than the minimumSortWinRate
            // )
            // )
            // Also keep all teams that:
            // Have a real winrate that's higher than the minSortWinRate

            // Then order it by SortWinrate
            var teamResponsesFiltered = teamResponsesSortWinRateOrderedQuery
                // SortWinRate >= minWinrate und echte Winrate max. 5%-Punkte weniger als minSortWinRate
                // außer weniger als 40 gamesplayed
                .Where(x => (x.SortWinRate >= minSortWinRate && (x.GamesPlayed < 40 || x.WinRate >= (minSortWinRate - 5)))
                || x.WinRate >= minSortWinRate)
                .OrderByDescending(x => x.SortWinRate)
                .ThenByDescending(x => x.WinRate)
                .ToArray();

            if (teamResponsesFiltered.Length == 0)
            {
                // if there are no teams left after filtering we will just return the 2 most played games
                teamResponsesFiltered = teamResponsesGamesPlayedOrderedQuery.Take(2).ToArray();
            }

            return teamResponsesFiltered;
        }

        private static string GetPlayerTeams(int ratingBracket, int manaCap, string[] rulesets, int minGamesPlayed, string internalCardIds, double winrateThreshold, string format, List<CachedTeamResponse> teamResponses, NpgsqlConnection connection)
        {
            string additionalFilters = "";
            if (winrateThreshold > 0)
            {
                additionalFilters += @"AND aggregated_results.""WinRate"" >= " + winrateThreshold.ToString("N2", CultureInfo.InvariantCulture);
            }
            using NpgsqlCommand cmd = connection.CreateCommand();

            internalCardIds = internalCardIds[0..^1];

            cmd.CommandText = TeamSelectQuery
                .Replace("@format", System.Web.HttpUtility.HtmlEncode(format))
                .Replace("@usercards", internalCardIds)
                .Replace("@minGamesPlayed", minGamesPlayed.ToString())
                .Replace("@ratingBracket", ratingBracket == -1 ? "IS NOT NULL" : "= " + ratingBracket.ToString())
                .Replace("@manaCap", manaCap.ToString())
                .Replace("@ruleset1", System.Web.HttpUtility.HtmlEncode(rulesets[0]))
                .Replace("@ruleset2", System.Web.HttpUtility.HtmlEncode(rulesets.Length > 1 ? rulesets[1] : ""))
                .Replace("@additionalFilters", additionalFilters);

            using NpgsqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                teamResponses.Add(new CachedTeamResponse() { WinRate = decimal.ToDouble((decimal)reader[0]), GamesPlayed = Convert.ToInt32(reader[1]), TeamHash = (long)reader[2], RatingBracket = Convert.ToInt32(reader[3]) });
            }

            return internalCardIds;
        }
        private static TeamResponseProperty[] CreateTeamResponsePropertyTeam(CachedTeamResponse team, int teamRank, bool questPriority, CardSettings cardSettings, PlayerCard[] playableCards, ApiRequestData apiRequestData, bool forceSingleRuleset)
        {
            // like I said earlier, NO idea why I created the TeamResponseProperty class and used it...
            TeamResponseProperty[] battleProperties = GenerateEmptyTeamTeamResponseProperty(team, teamRank, questPriority, cardSettings, forceSingleRuleset);

            // count free positions so we cann add 0 mana cards
            int freePositions = 6;
            string teamColor = "";
            List<int> cardIds = new();
            for (int i = 0; i < team.Cards.Count; i++)
            {
                string property;
                if (team.Cards[i].Position == 0)
                {
                    teamColor = GetSummonerColor(team.Cards[i].CardId.ToString());
                    battleProperties.Where(x => x.Property == "color").First().Value = teamColor;
                    property = "summoner_id";
                }
                else
                {
                    freePositions--;
                    property = $"monster_{team.Cards[i].Position}_id";
                }

                battleProperties.Where(x => x.Property == property).First().Value = team.Cards[i].CardId.ToString();
                cardIds.Add(team.Cards[i].CardId);
            }

            if (cardSettings.ADD_ZERO_MANA_CARDS && freePositions > 0)
            {
                AddZeroManaCards(battleProperties, playableCards, apiRequestData, freePositions, teamColor, cardIds);
            }

            return battleProperties;
        }

        private static void AddZeroManaCards(TeamResponseProperty[] battleProperties, PlayerCard[] playableCards, ApiRequestData apiRequestData, int freePositions, string teamColor, List<int> cardIds)
        {
            /*  Keep Your Distance (No Melee allowed) / Going the Distance (Ranged Only) would stop Fiends being used and the Chicken once it gets Melee 
                Odd Ones Out (only odd mana cards can be used) would stop 0 mana cards
                Wands Out (only monsters with magic can be used) would stop 0 mana cards
                Rise of the Commons would stop use of Epics and Legendary's (Fiends)
                Taking Sides (No Neutral Monsters) - No Chicken
            */
            string rulesets = apiRequestData.Rulesets;
            bool keepYourDistance = rulesets.Contains("Keep Your Distance") || rulesets.Contains("Going the Distance");
            bool oddOnesOut = rulesets.Contains("Odd Ones Out");
            bool wandsOut = rulesets.Contains("Wands Out");
            bool takingSides = rulesets.Contains("Taking Sides");
            bool riseOfTheCommons = rulesets.Contains("Rise of the Commons");
            bool lostLegendaries = rulesets.Contains("Lost Legendaries");
            bool upClosePersonal = rulesets.Contains("Up Close & Personal");

            if (oddOnesOut || wandsOut)
            {
                return;
            }

            int maxRarity;
            if (riseOfTheCommons)
            {
                maxRarity = 2;
            }
            else if (lostLegendaries)
            {
                maxRarity = 3;
            }
            else
            {
                maxRarity = 4;
            }
            bool rarityLimited = riseOfTheCommons || lostLegendaries;
            bool neutralCardsAllowed = !takingSides;

            List<PlayerCard> zeroManaCards = playableCards.Where(x => (neutralCardsAllowed && x.GetCardColor() == "neutral") || x.GetCardColor() == teamColor)
                .Where(x => ZeroManaMobs.Contains(Convert.ToInt32(x.card_detail_id)))
                .Where(x => !cardIds.Contains(Convert.ToInt32(x.card_detail_id)))
                .Where(x => !keepYourDistance || (keepYourDistance && !x.IsMelee(DetailedCards)))
                .Where(x => !upClosePersonal || (upClosePersonal && x.IsMelee(DetailedCards)))
                .Where(x => !rarityLimited || (rarityLimited && x.GetRarity(DetailedCards) <= maxRarity))
                .OrderByDescending(x => x.GetAttackPower(DetailedCards))
                .ToList();
            if (zeroManaCards.Any())
            {
                int ownedCards = Convert.ToInt32(battleProperties.Where(x => x.Property == "owned_cards").First().Value);
                int totalCards = Convert.ToInt32(battleProperties.Where(x => x.Property == "total_cards").First().Value);
                while (freePositions > 0 && zeroManaCards.Any())
                {
                    freePositions--;
                    ownedCards++;
                    totalCards++;
                    var card = zeroManaCards[0];
                    zeroManaCards.RemoveAt(0);
                    var property = $"monster_{6 - freePositions}_id";

                    battleProperties.Where(x => x.Property == property).First().Value = card.card_detail_id;
                }

                battleProperties.Where(x => x.Property == "owned_cards").First().Value = ownedCards.ToString();
                battleProperties.Where(x => x.Property == "total_cards").First().Value = totalCards.ToString();
                battleProperties[14].Value = "0 Mana Added";
            }
        }

        private static TeamResponseProperty[] GenerateEmptyTeamTeamResponseProperty(CachedTeamResponse team, int teamRank, bool questPriority, CardSettings cardSettings, bool forceSingleRuleset)
        {
            string cardSettingsString = cardSettings.USE_CARD_SETTINGS ? "Active" : "Inactive";
            if (forceSingleRuleset)
            {
                cardSettingsString += " (Ignored 1st ruleset)";
            }

            return new TeamResponseProperty[]
            {
                new TeamResponseProperty("play_for_quest", questPriority.ToString()),
                new TeamResponseProperty("winrate", (team.WinRate / 100).ToString()),
                new TeamResponseProperty("sort_winrate", (team.SortWinRate / 100).ToString()),
                new TeamResponseProperty("owned_cards", team.OwnedCards.ToString()),
                new TeamResponseProperty("total_cards", team.Cards.Count.ToString()),
                new TeamResponseProperty("summoner_id", ""),
                new TeamResponseProperty("monster_1_id", ""),
                new TeamResponseProperty("monster_2_id", ""),
                new TeamResponseProperty("monster_3_id", ""),
                new TeamResponseProperty("monster_4_id", ""),
                new TeamResponseProperty("monster_5_id", ""),
                new TeamResponseProperty("monster_6_id", ""),
                new TeamResponseProperty("color", ""),
                new TeamResponseProperty("teamRank", (teamRank + 1).ToString() + $" (based on {team.GamesPlayed} games)"),
                new TeamResponseProperty("card_settings", cardSettingsString),
            };
        }

        private static string GetSummonerColor(string id)
        {
            return Summoners.ContainsKey(id) ? Summoners[id] : "";
        }

        public static string GetCardColor(this PlayerCard card)
        {
            return ((string)CardsDetails[Convert.ToInt32(card.card_detail_id) - 1]["color"])
            .Replace("Red", "Fire").Replace("Blue", "Water").Replace("White", "Life").Replace("Black", "Death")
            .Replace("Green", "Earth").Replace("Gray", "Neutral").Replace("Gold", "Dragon").ToLower();
        }

        private static string[] GetAllowedColorsAndQuestPriority(ApiRequestData apiRequestData, CardSettings cardSettings, bool ignoreFocus, out bool questPriority)
        {
            questPriority = false;
            string questSplinter = apiRequestData.Focus;

            // chestTierReached check is client side
            if (!ignoreFocus && questSplinter != null && questSplinter != "")
            {
                // TODO: add logic for new quests, this only works for the old "summoner" quests
                bool focusElementNotRestricted = apiRequestData.Splinters.Any(x => x.ToString() == questSplinter);

                if (focusElementNotRestricted
                    && questSplinter != "sneak" && questSplinter != "snipe" && questSplinter != "neutral")
                {
                    if (questSplinter != "dragon")
                    {
                        // only return the quest splinter if quest priority is true
                        questPriority = true;
                        return new string[] { questSplinter };
                    }
                    else
                    {
                        // TODO: add logic for dragon quest
                    }
                }
            }

            return apiRequestData.Splinters.Where(x => cardSettings.PREFERRED_SUMMONER_ELEMENTS.Contains(x.ToString())).Select(x => x.ToString()).ToArray();
        }

        public static void Initialize()
        {
            string[] starterEditions = new string[] { "7", "12" };
            var cardsDetailsRaw = Helper.DownloadPageAsync("https://api2.splinterlands.com/cards/get_details").Result;
            CardsDetails = JArray.Parse(cardsDetailsRaw);
            DetailedCards = JsonConvert.DeserializeObject<List<DetailedCard>>(cardsDetailsRaw);
            StarterCards = DetailedCards.Where(x => x.rarity <= 2 && starterEditions.Contains(x.editions)).ToArray();
            ZeroManaMobs = DetailedCards.Where(x => x.ManaCost == 0).Select(x => x.id).ToArray();
            Summoners = DetailedCards.Where(x => x.type == "Summoner").ToDictionary(x => x.id.ToString(), x => x.GetCardColor());

            /*MobsWithSnipe = new string[] { "4", "12", "33", "51", "63", "76", "96", "127", "141", "148", "164", "171", "185", "192", "243", "320", "323", "362", "377", "396" };
            MobsWithSneak = new string[] { "3", "15", "24", "35", "47", "62", "77", "115", "138", "165", "175", "179", "216", "242", "301", "312", "337", "351", "357", "401", "411", "446" };
            ZeroManaMobs = new string[] { "422", "366", "408", "394", "131" };*/

            TeamSelectQuery = File.ReadAllText("Resources/config/api_team_select.sql");

            ConnectionString = File.ReadAllText("Resources/config/connection_string.txt");

            LoadInternalCardIds();

            Helper.WriteToConsole("Initializing done!");
        }

        public static void LoadInternalCardIds()
        {
            using NpgsqlConnection connection = new(ConnectionString);

            var result = connection.Query<(int internalId, int splinterlandsId, int level)>(@"SELECT ""Id"", ""CardId"", ""Level"" FROM ""Card"";");
            foreach ((int internalId, int splinterlandsId, int level) in result)
            {
                CardIds.Add((splinterlandsId, level), internalId);
            }

        }
    }
}
