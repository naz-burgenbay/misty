using System.Text;

namespace Misty.Application.Common;

public static class CursorCodec
{
    public static string Encode(long ticks, Guid id)
    {
        var raw = $"{ticks}:{id:N}";
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
    }

    public static bool TryDecode(string? cursor, out long ticks, out Guid id)
    {
        ticks = 0;
        id = Guid.Empty;
        if (string.IsNullOrEmpty(cursor))
            return false;

        try
        {
            var raw = Encoding.ASCII.GetString(Convert.FromBase64String(cursor));
            var sep = raw.IndexOf(':');
            if (sep <= 0)
                return false;
            return long.TryParse(raw.AsSpan(0, sep), out ticks)
                && Guid.TryParseExact(raw.AsSpan(sep + 1), "N", out id);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
