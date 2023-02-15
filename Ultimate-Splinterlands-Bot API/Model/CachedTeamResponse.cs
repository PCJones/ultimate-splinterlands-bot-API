using Npgsql;
using System.Data;

namespace Ultimate_Splinterlands_Bot_API.Model
{
    internal record CachedTeamResponse
    {
        public double WinRate { get; init; }
        public int GamesPlayed { get; init; }
        public long TeamHash { get; init; }
        public int RatingBracket { get; init; }
        public double SortWinRate { get; set; }
        public int OwnedCards { get; set; }

        public List<CardResponse> Cards { get; set; }

        public void GetCards(NpgsqlConnection connection, CardSettings cardSettings, PlayerCard[] cards, bool chestTierReached, string format)
        {
            OwnedCards = 0;
            Cards = new();
            using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT """ + format + @"_Team_Card"".""Position"", ""Card"".""CardId"" FROM """ + format + @"_Team_Card"", ""Card"" WHERE """ + format + @"_Team_Card"".""TeamHash"" = '" + TeamHash + "' "
                + @"AND """ + format + @"_Team_Card"".""CardId"" = ""Card"".""Id"";";

            using NpgsqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var cardId = reader[1];
                var card = cards.Where(x => x.card_detail_id == cardId.ToString()).First();
                if (!card.starter)
                {
                    OwnedCards++;
                }

                Cards.Add(new CardResponse() { Position = Convert.ToInt32(reader[0]), CardId = (int)cardId });
            }

            var ownCardPercentage = (double)OwnedCards / Cards.Count;
            double modifier;
            if (cardSettings.DISABLE_OWNED_CARDS_PREFERENCE_BEFORE_CHEST_LEAGUE_RATING && !chestTierReached)
            {
                modifier = 0;
            }
            else
            {
                // if winrate below 51% only use 1/3 of card modifier
                int divideAmount = WinRate >= 0.51 ? 1 : 3;
                int ownCardPercentageModifier = cardSettings.WINRATE_MODIFIER_OWNED_CARD_PERCENTAGE / divideAmount;
                double flatNegativeModifier = cardSettings.FLAT_NEGATIVE_MODIFIER_PER_UNOWNED_CARD / divideAmount;
                modifier = ownCardPercentage * ownCardPercentageModifier;
                modifier -= flatNegativeModifier * (Cards.Count - OwnedCards);
            }

            SortWinRate = Math.Max(WinRate + modifier, 0);
        }
    }
}
