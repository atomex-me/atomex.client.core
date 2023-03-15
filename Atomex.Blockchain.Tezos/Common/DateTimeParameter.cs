using System;
using System.Collections;
using System.Collections.Generic;

namespace Atomex.Blockchain.Tezos.Common
{
    public enum EqualityType
    {
        Eq,
        Ne,
        Gt,
        Ge,
        Lt,
        Le,
        In,
        Ni
    }

    public struct DateTimeParameter : IEnumerable<(DateTimeOffset, EqualityType)>
    {
        public List<(DateTimeOffset, EqualityType)> TimeStamps { get; set; }

        public DateTimeParameter()
        {
            TimeStamps = new List<(DateTimeOffset, EqualityType)>();
        }

        public DateTimeParameter(DateTimeOffset timeStamp)
        {
            TimeStamps = new List<(DateTimeOffset, EqualityType)> { (timeStamp, EqualityType.Eq) };
        }

        public DateTimeParameter(DateTimeOffset timeStamp, EqualityType type)
        {
            TimeStamps = new List<(DateTimeOffset, EqualityType)> { (timeStamp, type) };
        }

        public DateTimeParameter(IEnumerable<(DateTimeOffset, EqualityType)> timeStamps)
        {
            TimeStamps = new List<(DateTimeOffset, EqualityType)>(timeStamps);
        }

        public void Add(DateTimeOffset timeStamp, EqualityType type)
        {
            TimeStamps.Add((timeStamp, type));
        }

        public string ToString(string paramName, Func<DateTimeOffset, string> format)
        {
            var parameters = new string[TimeStamps.Count];

            for (var i = 0; i < TimeStamps.Count; ++i)
                parameters[i] = $"{paramName}.{TimeStamps[i].Item2.ToString().ToLowerInvariant()}={format(TimeStamps[i].Item1)}";

            return string.Join('&', parameters);
        }

        public IEnumerator<(DateTimeOffset, EqualityType)> GetEnumerator() => TimeStamps.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => TimeStamps.GetEnumerator();
    }
}