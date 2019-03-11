using System.Security;
using System.Text.RegularExpressions;

namespace Atomix.Common
{
    public class PasswordAdvisor
    {
        public enum PasswordScore
        {
            Blank = 0,
            TooShort = 1,
            Weak = 2,
            Medium = 3,
            Strong = 4,
            VeryStrong = 5
        }

        public static PasswordScore CheckStrength(string password)
        {
            if (password == null)
                return PasswordScore.Blank;

            var score = 1;

            if (password.Length < 1)
                return PasswordScore.Blank;
            if (password.Length < 6)
                return PasswordScore.TooShort;
            if (password.Length >= 8)
                score++;
            if (password.Length >= 12)
                score++;
            if (Regex.IsMatch(password, @"[0-9]+(\.[0-9][0-9]?)?"))
                score++;
            if (Regex.IsMatch(password, @"^(?=.*[a-z])(?=.*[A-Z]).+$"))
                score++;
            if (Regex.IsMatch(password, @"[!,@,#,$,%,^,&,*,?,_,~,-,£,(,)]"))
                score++;

            return score < (int)PasswordScore.VeryStrong
                ? (PasswordScore)score
                : PasswordScore.VeryStrong;
        }

        public static PasswordScore CheckStrength(SecureString password)
        {
            if (password == null)
                return PasswordScore.Blank;

            var score = 1;

            if (password.Length < 1)
                return PasswordScore.Blank;
            if (password.Length < 6)
                return PasswordScore.TooShort;
            if (password.Length >= 8)
                score++;
            if (password.Length >= 12)
                score++;
            if (password.ContainsDigit())
                score++;
            if (password.ContainsLower() && password.ContainsUpper())
                score++;
            if (password.ContainsSpecials())
                score++;

            return score < (int)PasswordScore.VeryStrong
                ? (PasswordScore)score
                : PasswordScore.VeryStrong;
        }
    }
}