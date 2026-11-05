namespace Horizon.Builder.Core;

/// <summary>The public application ID used by the Horizon launcher.</summary>
public static class HorizonIdentity
{
    public static string ClientId { get; } = ReadClientId();

    private static string ReadClientId()
    {
        using var stream = typeof(HorizonIdentity).Assembly.GetManifestResourceStream("Horizon.Builder.Core.HorizonClientId.txt")
            ?? throw new InvalidOperationException("The Horizon public client ID is missing.");
        using var reader = new StreamReader(stream);
        var value = reader.ReadToEnd().Trim();
        if (!Guid.TryParse(value, out var id)) throw new InvalidDataException("The Horizon public client ID is invalid.");
        return id.ToString();
    }
}
