using System.Security.Cryptography;
using System.Text;

namespace DnsWeaverApi.Security;

public static class SecureCompare
{
    public static bool Equals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
