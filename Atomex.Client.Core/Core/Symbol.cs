using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Atomex.Core
{
    public class Symbol
    {
        public const int MaxNameLength = 32;

        public int Id { get; set; }
        public string Name { get; set; }
        public decimal MinimumQty { get; set; }
        public string Base { get; set; }
        public string Quote { get; set; }

        public Symbol()
        {
        }

        public Symbol(IConfiguration configuration)
        {
            Id = int.Parse(configuration["Id"]);
            Name = configuration["Name"];
            MinimumQty = decimal.Parse(configuration["MinimumQty"], CultureInfo.InvariantCulture);
            Base = Name.Substring(0, Name.IndexOf('/'));
            Quote = Name.Substring(Name.IndexOf('/') + 1);
        }
    }
}