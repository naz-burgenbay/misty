namespace Misty.Application.Common.Exceptions;

public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException() : base("Invalid credentials.") { }
    public UnauthorizedException(string message) : base(message) { }
}
