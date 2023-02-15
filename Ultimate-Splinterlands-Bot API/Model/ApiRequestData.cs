#nullable enable
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ultimate_Splinterlands_Bot_API.Model
{
    public partial class ApiRequestData
    {
        [JsonProperty("mana")]
        public int ManaCap { get; set; }

        [JsonProperty("rules")]
        public string Rulesets { get; set; }

        [JsonProperty("splinters")]
        public string[] Splinters { get; set; }

        [JsonProperty("myCardsV2")]
        private string CardsRawJson { init; get; }
        [JsonIgnore]
        public PlayerCard[] Cards => JsonConvert.DeserializeObject<PlayerCard[]>(CardsRawJson) ?? Array.Empty<PlayerCard>();
        
        [JsonProperty("focus")]
        public string? Focus { get; set; }

        [JsonProperty("chest_tier_reached")]
        public bool? ChestTierReached { get; set; }

        [JsonProperty("card_settings")]
        private string? CardSettingsRawJson { init; get; }
        [JsonIgnore]
        public CardSettings? CardSettings => CardSettingsRawJson != null ? JsonConvert.DeserializeObject<CardSettings>(CardSettingsRawJson) : null;
    }
}
