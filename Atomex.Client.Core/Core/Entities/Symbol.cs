using System.Globalization;
using System.Linq;
using Atomex.Abstract;
using Microsoft.Extensions.Configuration;

namespace Atomex.Core.Entities
{
    public class Symbol
    {
        public const int MaxNameLength = 32;

        public int Id { get; set; }
        public string Name { get; set; }
        public decimal MinimumQty { get; set; }
        public int BaseId { get; set; }
        public Currency Base { get; set; }
        public int QuoteId { get; set; }
        public Currency Quote { get; set; }

        public Symbol()
        {   
        }

        public Symbol(
            IConfiguration configuration,
            ICurrencies currencies)
        {
            Id = int.Parse(configuration["Id"]);
            Name = configuration["Name"];
            MinimumQty = decimal.Parse(configuration["MinimumQty"], CultureInfo.InvariantCulture);

            var baseName = Name.Substring(0, Name.IndexOf('/'));
            var quoteName = Name.Substring(Name.IndexOf('/') + 1);

            Base = currencies.First(c => c.Name == baseName);
            Quote = currencies.First(c => c.Name == quoteName);
        }
    }
}