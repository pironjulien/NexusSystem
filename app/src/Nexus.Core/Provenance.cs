using System.Net;
using System.Text.RegularExpressions;
using Sigstore;
using Tuf;

namespace Nexus.Core;

/// <summary>A workflow allowed to sign a release file: the repository that holds it and the ref it was built from.</summary>
public sealed record ProvenanceSigner(string Repository, string SourceRef);

/// <summary>
/// The build provenance of a downloaded release file, checked here without any outside tool: the Sigstore bundle its
/// release ships (&lt;Repository&gt;-X.Y.Z.intoto.jsonl, GitHub's build attestation) is verified by the Sigstore library
/// against Sigstore's public trust root (certificate chain, transparency log, signature), then the signing workflow,
/// the source repository and ref, and the file's SHA-256 among the attested subjects. Three outcomes, never mixed:
/// verified (returns); failed (InvalidDataException: wrong signer, altered or absent bundle, file not attested);
/// could not be checked (HttpRequestException: the bundle or the trust root is unreachable). Only a verified file is
/// installed.
/// </summary>
public static class Provenance
{
    const string Owner = "pironjulien";
    const string Issuer = "https://token.actions.githubusercontent.com";
    const string SlsaProvenance = "https://slsa.dev/provenance/v1";

    /// <summary>Sigstore's public trust root through TUF, kept in memory: nothing is written to the user's profile.</summary>
    static readonly Lazy<ITrustRootProvider> PublicTrustRoot = new(() =>
        new TufTrustRootProvider(TufTrustRootProvider.ProductionUrl, new TufTrustRootProviderOptions { Cache = new InMemoryTufCache() }));

    /// <summary>
    /// A product's release file: signed by NexusSystem's shared release workflow for that product's main branch, or by
    /// the product's own workflow from its tag or from main.
    /// </summary>
    public static IReadOnlyList<ProvenanceSigner> ProductSigners(string repository, string tag) =>
        [new("NexusSystem", "refs/heads/main"), new(repository, $"refs/tags/{tag}"), new(repository, "refs/heads/main")];

    /// <summary>The Sigstore bundle a release ships for its files; a development build's carries its version alone.</summary>
    public static string BundleName(string repository, string tag) => $"{repository}-{tag[1..].Split('-')[0]}.intoto.jsonl";

    /// <summary>Checks a downloaded file against the bundle of its own release.</summary>
    public static async Task VerifyAsync(HttpClient http, string repository, string tag, string sha256, IReadOnlyList<ProvenanceSigner> signers, CancellationToken cancel)
    {
        string bundles;
        var name = BundleName(repository, tag);
        try
        {
            bundles = await http.GetStringAsync(new Uri($"https://github.com/{Owner}/{repository}/releases/download/{tag}/{name}"), cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidDataException($"Build provenance failed for {repository} {tag}: the release ships no {name}.", error);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            throw Unchecked(repository, tag, error);
        }
        await VerifyAsync(bundles, repository, tag, sha256, signers, PublicTrustRoot.Value, cancel).ConfigureAwait(false);
    }

    /// <summary>The same check on a bundle already read, against a given trust root (the tests' own, without network).</summary>
    public static async Task VerifyAsync(string bundles, string repository, string tag, string sha256, IReadOnlyList<ProvenanceSigner> signers,
        ITrustRootProvider trustRoot, CancellationToken cancel)
    {
        // The trust root first and alone: the only step of the verification that needs the network, so an unreachable
        // one is told apart from a refused signature.
        TrustedRoot root;
        try
        {
            root = await trustRoot.GetTrustRootAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or TufException or IOException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            throw Unchecked(repository, tag, error);
        }
        var verifier = new SigstoreVerifier(new InMemoryTrustRootProvider(root));
        var digest = Convert.FromHexString(sha256);
        var reason = "the bundle is empty";
        foreach (var line in bundles.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            SigstoreBundle bundle;
            try
            {
                bundle = SigstoreBundle.Deserialize(line);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                reason = $"unreadable bundle ({error.Message})";  // anything an altered bundle makes the parser throw
                continue;
            }
            foreach (var signer in signers)
            {
                var policy = new VerificationPolicy
                {
                    CertificateIdentity = new CertificateIdentity
                    {
                        SubjectAlternativeNamePattern = "^" + Regex.Escape($"https://github.com/{Owner}/{signer.Repository}/.github/workflows/"),
                        Issuer = Issuer,
                        Extensions = new CertificateExtensionPolicy
                        {
                            SourceRepositoryUri = $"https://github.com/{Owner}/{repository}",
                            SourceRepositoryRef = signer.SourceRef,
                        },
                    },
                };
                var (verified, result) = await verifier.TryVerifyDigestAsync(digest, HashAlgorithmType.Sha256, bundle, policy, cancel).ConfigureAwait(false);
                if (!verified)
                {
                    reason = result?.FailureReason ?? "the signature was refused";
                    continue;
                }
                // The library authenticates the attestation; that it speaks of THIS file is read from its signed statement.
                if (result?.Statement is not { PredicateType: SlsaProvenance } statement)
                    reason = "the bundle is not a build provenance attestation";
                else if (!statement.Subject.Any(subject => subject.Digest.TryGetValue("sha256", out var attested) && attested.Equals(sha256, StringComparison.OrdinalIgnoreCase)))
                    reason = "the attestation does not cover this file";
                else
                    return;
                break;  // an authentic attestation that is not this file's: another signer changes nothing
            }
        }
        throw new InvalidDataException($"Build provenance failed for {repository} {tag}: {reason}");
    }

    static HttpRequestException Unchecked(string repository, string tag, Exception error) =>
        new($"Build provenance of {repository} {tag} could not be checked ({error.Message}); nothing was installed.", error);
}
