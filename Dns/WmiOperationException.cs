namespace DnsWeaverApi.Dns;

public class WmiOperationException : Exception
{
    public WmiOperationException(string message) : base(message) { }
}
