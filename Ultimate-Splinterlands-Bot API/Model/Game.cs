using Newtonsoft.Json.Linq;
using Ultimate_Splinterlands_Bot_API.Util;

namespace Ultimate_Splinterlands_Bot_API.Model
{
    internal record Game
    {
        public int? Id { get; set; }
        public string QueueIdHash { get; init; }
        public string QueueId1 { get; init; }
        public int Rating { get; init; }
        public DateTime CreatedDate{ get; init; }
        public string MatchType { get; init; }
        public int ManaCap { get; init; }
        public string Ruleset1 { get; init; }
        public string Ruleset2 { get; init; }
        public string Inactive { get; init; }
        public string? TournamentSettings { get; init; }
        public bool IsSurrender { get; init; }
        public int RatingLevel { get; init; }
        public JToken DetailsJson { get; init; }
        public string Result { get; init; }
        public string Winner { get; init; }
        public string Loser { get; init; }

        public Game()
        {
            IsSurrender = false;
        }
        public Game(JToken game)
        {
            DetailsJson = JToken.Parse((string)game["details"]!) ?? throw new ArgumentNullException(nameof(game));
            IsSurrender = ((bool?)game["is_surrender"]) ?? true;
            string queueId1 = (string?)game["battle_queue_id_1"] ?? throw new ArgumentNullException(nameof(game));
            string queueId2 = (string?)game["battle_queue_id_2"] ?? throw new ArgumentNullException(nameof(game));

            // check if this is correct
            string queueIdsCombined = String.Compare(queueId1, queueId2) < 0 ? queueId1 + queueId2 : queueId2 + queueId1;
            QueueIdHash = Helper.GetInt64HashCode(queueIdsCombined).ToString();
            QueueId1 = queueId1;
            if (IsSurrender)
            {
                return;
            }
            //CreatedDate = new DateTimeOffset((DateTime)game["created_date"]).ToUnixTimeMilliseconds().ToString();
            CreatedDate = (DateTime)game["created_date"];
            MatchType = (string?)game["match_type"] ?? throw new ArgumentNullException(nameof(game));
            TournamentSettings = MatchType == "Ranked" ? null : (string)game["settings"]!;
            RatingLevel = game["settings"] == null || MatchType != "Ranked" ? -1 : Convert.ToInt32(((string)game["settings"]!)[16].ToString()); // {"rating_level":2}
            ManaCap = ((int?)game["mana_cap"]) ?? -1;
            Inactive = (string?)game["inactive"] ?? throw new ArgumentNullException(nameof(game));
            Rating = (int?)game["player_1_rating_initial"] ?? throw new ArgumentNullException(nameof(game));
            string[] rulesets = ((string?)game["ruleset"] ?? throw new ArgumentNullException(nameof(game))).Split('|');
            Ruleset1 = rulesets[0];
            Ruleset2 = rulesets.Length > 1 ? rulesets[1] : "";
            string winner = (string?)game["winner"] ?? throw new ArgumentNullException(nameof(game));
            string player1 = (string?)game["player_1"] ?? throw new ArgumentNullException(nameof(game));
            string player2 = (string?)game["player_2"] ?? throw new ArgumentNullException(nameof(game));
            if (winner == "DRAW")
            {
                Result = "D";
                Winner = player1;
                Loser = player2;
            }
            else if (winner == player1)
            {
                Result = "W";
                Winner = player1;
                Loser = player2;
            }
            else
            {
                Result = "W";
                Winner = player2;
                Loser = player1;
            }
        }
    }
}
