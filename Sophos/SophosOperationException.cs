namespace DnsWeaverApi.Sophos;

public class SophosOperationException : Exception
{
    public SophosOperationException(string message) : base(message) { }
}
