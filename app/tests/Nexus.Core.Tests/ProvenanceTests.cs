using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Nexus.Core;
using Sigstore;
using Xunit;

namespace Nexus.Core.Tests;

/// <summary>
/// The build provenance check on real release bundles (NexusSystem v1.11.1, NexusWindows v3.18.2) and a copy of
/// Sigstore's public trust root, all read from Fixtures: no network, no gh, nothing outside the test's own folder.
/// </summary>
public class ProvenanceTests
{
    const string Engine = "8f68a12408acc28fbfe0aaffb244093e6e488968dc4ba1b0f9703244eec174cf";     // NexusMaintenance-x64.exe, v1.11.1
    const string Installer = "9d5900cc11d74f0e88933ee92a921133393631f28895c12db02aea482288a924";  // NexusWindows' get.ps1, v3.18.2
    static readonly ProvenanceSigner[] EngineSigner = [new("NexusSystem", "refs/tags/v1.11.1")];

    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    static string SystemBundle => Fixture("NexusSystem-1.11.1.intoto.jsonl");
    static string WindowsBundle => Fixture("NexusWindows-3.18.2.intoto.jsonl");
    static ITrustRootProvider TrustRoot => new InMemoryTrustRootProvider(TrustedRoot.Deserialize(Fixture("trusted_root.json")));

    static Task Verify(string bundles, string repository, string tag, string sha256, IReadOnlyList<ProvenanceSigner> signers, ITrustRootProvider? trustRoot = null) =>
        Provenance.VerifyAsync(bundles, repository, tag, sha256, signers, trustRoot ?? TrustRoot, TestContext.Current.CancellationToken);

    sealed class Offline : ITrustRootProvider
    {
        public Task<TrustedRoot> GetTrustRootAsync(CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("fixture: no network");
    }

    sealed class Answer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
    }

    [Fact]
    public async Task TheEngineOfItsOwnTagIsVerified() => await Verify(SystemBundle, "NexusSystem", "v1.11.1", Engine, EngineSigner);

    [Fact]
    public async Task AProductInstallerSignedByTheSharedWorkflowIsVerified() =>
        await Verify(WindowsBundle, "NexusWindows", "v3.18.2", Installer, Provenance.ProductSigners("NexusWindows", "v3.18.2"));

    [Fact]
    public async Task UpperCaseChecksumsAreTheSameFile() => await Verify(SystemBundle, "NexusSystem", "v1.11.1", Engine.ToUpperInvariant(), EngineSigner);

    [Theory]
    [InlineData("NexusBrowser")]  // NexusWindows' authentic attestation, presented as another product's release
    [InlineData("NexusSystem")]
    public async Task AnAttestationOfAnotherRepositoryIsRefused(string repository)
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Verify(WindowsBundle, repository, "v3.18.2", Installer, Provenance.ProductSigners(repository, "v3.18.2")));
        Assert.StartsWith($"Build provenance failed for {repository} v3.18.2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWorkflowOfAnotherRepositoryIsNotAnAcceptedSigner()
    {
        // The engine must be signed by NexusSystem's workflow: another repository's, even for the right source, is refused.
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Verify(SystemBundle, "NexusSystem", "v1.11.1", Engine, [new("NexusWindows", "refs/tags/v1.11.1")]));
        Assert.Contains("SAN", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnotherTagOrBranchIsRefused()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Verify(SystemBundle, "NexusSystem", "v1.11.2", Engine, [new("NexusSystem", "refs/tags/v1.11.2")]));
        await Assert.ThrowsAsync<InvalidDataException>(() => Verify(SystemBundle, "NexusSystem", "v1.11.1", Engine, [new("NexusSystem", "refs/heads/main")]));
    }

    [Fact]
    public async Task AFileTheAttestationDoesNotCoverIsRefused()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Verify(SystemBundle, "NexusSystem", "v1.11.1", Installer, EngineSigner));
        Assert.Contains("does not cover this file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAlteredStatementIsRefused()
    {
        // The attacker's file listed in place of the engine: the signature and the transparency log no longer match.
        var bundle = JsonNode.Parse(SystemBundle)!;
        var statement = Encoding.UTF8.GetString(Convert.FromBase64String(bundle["dsseEnvelope"]!["payload"]!.GetValue<string>()));
        Assert.Contains(Engine, statement, StringComparison.Ordinal);
        bundle["dsseEnvelope"]!["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(statement.Replace(Engine, Installer, StringComparison.Ordinal)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Verify(bundle.ToJsonString(), "NexusSystem", "v1.11.1", Installer, EngineSigner));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{not a bundle")]
    public async Task AnEmptyOrUnreadableBundleIsRefused(string bundles) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Verify(bundles, "NexusSystem", "v1.11.1", Engine, EngineSigner));

    [Fact]
    public async Task WithoutTheTrustRootNothingIsVerifiedAndItIsSaid()
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Verify(SystemBundle, "NexusSystem", "v1.11.1", Engine, EngineSigner, new Offline()));
        Assert.Contains("could not be checked", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleaseWithoutBundleIsRefused()
    {
        Uri? asked = null;
        using var http = new HttpClient(new Answer(request => { asked = request.RequestUri; return new HttpResponseMessage(HttpStatusCode.NotFound); }));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Provenance.VerifyAsync(http, "NexusSystem", "v1.11.1", Engine, EngineSigner, TestContext.Current.CancellationToken));
        Assert.Contains("ships no NexusSystem-1.11.1.intoto.jsonl", error.Message, StringComparison.Ordinal);
        Assert.Equal("https://github.com/pironjulien/NexusSystem/releases/download/v1.11.1/NexusSystem-1.11.1.intoto.jsonl", asked?.ToString());
    }

    [Fact]
    public async Task AnUnreachableBundleIsNotAVerifiedOne()
    {
        using var http = new HttpClient(new Answer(_ => throw new HttpRequestException("fixture: no network")));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Provenance.VerifyAsync(http, "NexusSystem", "v1.11.1", Engine, EngineSigner, TestContext.Current.CancellationToken));
        Assert.Contains("could not be checked", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevelopmentBuildShipsItsVersionsBundle() => Assert.Equal("NexusRelay-1.16.0.intoto.jsonl", Provenance.BundleName("NexusRelay", "v1.16.0-dev.7"));
}
