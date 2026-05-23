namespace Misty.Application.Common.Exceptions;

public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException()
        : base("The resource was modified by another request. Fetch the latest version and retry.") { }
}
