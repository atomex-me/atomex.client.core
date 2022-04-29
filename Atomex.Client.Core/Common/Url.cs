namespace Atomex.Common
{
    public static class Url
    {
        public static string Combine(string url1, string url2) =>
            url1.TrimEnd('/') + "/" + url2.TrimStart('/');
    }
}