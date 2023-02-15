using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ultimate_Splinterlands_Bot_API.Model
{
    public record PlayerCard
    {
        public string card_detail_id { get; init; }
        public int level { get; init; }
        public bool gold { get; init; }
        public bool starter { get; init; }

        public int GetRarity(List<DetailedCard> detailedCards)
        {
            return detailedCards[Convert.ToInt32(card_detail_id) - 1].rarity;
        }

        public PlayerCard(string cardId, int _level, bool _gold, bool _starter)
        {
            card_detail_id = cardId;
            level = _level;
            gold = _gold;
            starter = _starter;
        }

        public PlayerCard()
        {

        }

        public bool IsMelee(List<DetailedCard> detailedCards)
        {
            return GetAttackPower(detailedCards) > 0;
        }

        public int GetAttackPower(List<DetailedCard> detailedCards)
        {
            var card = detailedCards[Convert.ToInt32(card_detail_id) - 1];
            if (card.stats.attack is Newtonsoft.Json.Linq.JArray attack)
            {
                int attackValue = (int)attack[level - 1];
                return attackValue;
            }
            else
            {
                return 0;
            }
        }

        public int SortValue()
        {
            return gold ? 11 : Convert.ToInt32(level);
        }
    }
}
