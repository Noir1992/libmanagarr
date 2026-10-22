namespace LibManagarr.Api.Services;

public static class UuidV7
{
    public static string Create()
    {
        return Guid.CreateVersion7().ToString("D");
    }

    public static bool IsUuidV7(string? value)
    {
        if (!Guid.TryParse(value, out Guid guid))
        {
            return false;
        }

        Span<char> formattedGuid = stackalloc char[36];
        bool formatted = guid.TryFormat(formattedGuid, out int charsWritten, "D");

        char variant = char.ToLowerInvariant(formattedGuid[19]);

        return formatted &&
            charsWritten == 36 &&
            formattedGuid[14] == '7' &&
            variant is '8' or '9' or 'a' or 'b';
    }
}
