using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HistoryCrawler.Model
{
    internal record User
    {
        [Key]
        public string Name { get; init; }
        public int Rating { get; set; }
        public int RatingLevel { get; set; }
        public DateTime? LastCrawlTime { get; set;}
        public DateTime LastRatingUpdate { get; set; }
        public string AddedByUsername { get; set; }
    }
}
