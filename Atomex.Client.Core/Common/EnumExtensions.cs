using System;
using System.ComponentModel;

namespace Atomex.Common
{
    public static class EnumExtensions
    {
        public static string GetDescription<T>(this T enumerationValue)
            where T : struct
        {
            var type = enumerationValue.GetType();
            if (!type.IsEnum)
            {
                throw new ArgumentException(Resources.EnumerationValueMustBeOfEnumType, nameof(enumerationValue));
            }
            
            var memberInfo = type.GetMember(enumerationValue.ToString());
            if (memberInfo.Length == 0)
            {
                return enumerationValue.ToString();
            }

            var attrs = memberInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);

            return attrs.Length > 0
                ? ((DescriptionAttribute)attrs[0]).Description
                : enumerationValue.ToString();
        }
	}
}
