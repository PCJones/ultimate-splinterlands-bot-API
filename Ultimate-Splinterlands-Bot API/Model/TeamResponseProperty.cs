using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ultimate_Splinterlands_Bot_API.Model
{
    public record TeamResponseProperty
    {
        public string Property { get; set; }
        public string Value { get; set; }

        public TeamResponseProperty(string property, string value)
        {
            Property = property;
            Value = value;
        }
    }
}
