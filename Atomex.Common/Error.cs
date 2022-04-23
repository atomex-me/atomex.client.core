using System.Collections.Generic;

namespace Atomex.Common
{
    public class Error
    {
        public int Code { get; set; }
        public string Description { get; set; }
        public List<Error> InternalErrors { get; set; }

        public Error() { }

        public Error(int code, string description)
        {
            Code = code;
            Description = description;
        }

        public Error(int code, string description, params Error[] internalErrors)
        {
            Code = code;
            Description = description;

            if (internalErrors != null)
                InternalErrors = new List<Error>(internalErrors);
        }

        public override string ToString() =>
            $"Code: {Code}. Description: {Description}";
    }
}