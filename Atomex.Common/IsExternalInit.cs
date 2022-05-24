/*
 * Hack for enablimng support of features after .net 5.0 in projects targeted to previous versions:
 * Source: https://developercommunity.visualstudio.com/t/error-cs0518-predefined-type-systemruntimecompiler/1244809#T-N1249582
 */
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit
    {
    }
}
