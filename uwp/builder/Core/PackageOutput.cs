namespace Horizon.Builder.Core;

public sealed record SignedPackageOutput(string Client, string Certificate, string? ResourceFramework);

/// <summary>Creates distributable copies of the client and optional private resource framework.</summary>
public static class PackageOutput
{
    public static async Task<SignedPackageOutput> CreateAsync(string unsignedClient, string? unsignedResourceFramework,
        PackageSigningIdentity identity, string signTool, string newOutputDirectory)
    {
        var output = Path.GetFullPath(newOutputDirectory);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new signed output directory.");
        if (!Directory.Exists(Path.GetDirectoryName(output))) throw new DirectoryNotFoundException("The output parent directory does not exist.");
        Directory.CreateDirectory(output);
        var client = Path.Combine(output, "HorizonUWP.appx");
        var certificate = Path.Combine(output, "HorizonUWP.cer");
        await PackageSigning.SignAsync(unsignedClient, client, certificate, identity, signTool);
        string? framework = null;
        if (unsignedResourceFramework is not null)
        {
            framework = Path.Combine(output, "HorizonResources.appx");
            await PackageSigning.SignAsync(unsignedResourceFramework, framework, null, identity, signTool);
        }
        return new(client, certificate, framework);
    }
}
