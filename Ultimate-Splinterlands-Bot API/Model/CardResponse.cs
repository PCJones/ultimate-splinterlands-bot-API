using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ultimate_Splinterlands_Bot_API.Model
{
    internal record CardResponse
    {
        public int Position { get; init; }
        public int CardId { get; init; }
    }
}
