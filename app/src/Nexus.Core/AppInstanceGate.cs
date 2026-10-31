using System.Security.Principal;

namespace Nexus.Core;

/// <summary>One normal hub per user/session, including immutable installs at different executable paths.</summary>
public sealed class AppInstanceGate : IDisposable
{
    readonly Mutex mutex;
    readonly EventWaitHandle activation;
    RegisteredWaitHandle? listener;
    bool disposed;

    public bool IsFirst { get; }

    public AppInstanceGate()
        : this("julienpiron.NexusSystem") { }

    internal AppInstanceGate(string applicationKey)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var key = @"Local\" + applicationKey + "." + identity.User!.Value;
        // Existence, rather than ownership by a particular thread, represents the living application.
        mutex = new Mutex(false, key, out var first);
        IsFirst = first;
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, key + ".Activate");
    }

    public void RequestActivation() => activation.Set();

    public void Listen(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        if (!IsFirst || listener is not null) throw new InvalidOperationException("Only the first hub listens for activation.");
        listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => activate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        listener?.Unregister(null);
        activation.Dispose();
        mutex.Dispose();
    }
}
