namespace Horizon.Builder.Core;

public sealed record SignedPackageOutput(string Client, string Certificate, string? ResourceFramework, string InstallationGuide);

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
        var guide = Path.Combine(output, "Install.md");
        await using var guideSource = typeof(PackageOutput).Assembly.GetManifestResourceStream("Horizon.Builder.Core.InstallGuide.md")
            ?? throw new InvalidOperationException("The installation guide is missing.");
        await using var guideOutput = new FileStream(guide, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await guideSource.CopyToAsync(guideOutput);
        return new(client, certificate, framework, guide);
    }
}
