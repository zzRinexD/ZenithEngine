// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading.Tasks;

using Prowl.Runtime;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// H-RD-54: a load that needs another asset while the load lock is held must not deadlock.
///
/// <para><see cref="AssetBackendBase.Get"/> holds <c>_loadLock</c> across the whole deserialize, and
/// <see cref="AssetLoader.LoadBlocking"/> used to queue the request and wait on the single loader
/// thread. When the caller was itself the lock holder - a deserializer resolving a dependency - that
/// loader thread would block on the very lock being held, while this thread waited for it. A textbook
/// AB-BA deadlock: both threads parked forever, no crash, no log.</para>
///
/// <para>Nothing in the shipped serializers triggers it today (they resolve lazily), which makes it
/// latent rather than active - but an importer or plugin calling <c>EnsureLoaded</c> during a
/// deserialize would, and nothing downstream could avoid it. The backend now publishes whether this
/// thread holds the lock, and the loader resolves inline when it does.</para>
/// </summary>
[Collection("AssetLoaderConcurrency")]
public class AssetLoaderConcurrencyTests : IDisposable
{
    /// <summary>Generous: the assertion is about "did not deadlock", not about being fast.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    private readonly AssetBackendBase? _previousBackend = AssetDatabase.Current;

    public void Dispose()
    {
        // Stop first: it releases anyone blocked in LoadBlocking and clears the queues, so the
        // loader thread cannot outlive the test or leak work into the next one.
        AssetLoader.Stop();
        AssetDatabase.Current = _previousBackend;
    }

    /// <summary>A stand-in asset: EngineObject has no abstract members, so it needs no overrides.</summary>
    private sealed class TestAsset : EngineObject
    {
    }

    /// <summary>
    /// Loads <c>dependency</c> by asking for it from inside <see cref="LoadFresh"/>, which is exactly
    /// where the load lock is held. Stands in for a deserializer that needs a dependency resolved
    /// before it can finish.
    /// </summary>
    private sealed class DependencyLoadingBackend : AssetBackendBase
    {
        private readonly Guid _dependency;
        public readonly Guid Root;
        public bool SawLockHeldInsideLoadFresh = false;

        public DependencyLoadingBackend(Guid root, Guid dependency)
        {
            Root = root;
            _dependency = dependency;
        }

        protected override EngineObject? LoadFresh(Guid assetId)
        {
            SawLockHeldInsideLoadFresh |= assetId == Root && AssetBackendBase.IsLoadingOnThisThread;

            if (assetId == _dependency)
            {
                var leaf = new TestAsset { AssetID = assetId };
                SetLoaded(assetId, leaf);
                return leaf;
            }

            // The nested blocking load, issued while the lock is held.
            var resolved = AssetLoader.LoadBlocking(_dependency);
            if (resolved != null)
                SetLoaded(assetId, resolved);
            return resolved;
        }
    }

    /// <summary>The regression: a load that depends on another asset completes instead of parking
    /// two threads against each other.</summary>
    [Fact]
    public void Get_WhoseDeserializeNeedsAnotherAsset_DoesNotDeadlock()
    {
        Guid root = Guid.NewGuid();
        Guid dependency = Guid.NewGuid();
        var backend = new DependencyLoadingBackend(root, dependency);
        AssetDatabase.Current = backend;

        EngineObject? result = null;
        var load = Task.Run(() => result = backend.Get(root));

        Assert.True(Task.WaitAny([load], (int)Deadline.TotalMilliseconds) >= 0,
            "Get() never returned. The loader thread is blocked on the load lock the calling thread " +
            "is holding: an AB-BA deadlock (H-RD-54).");

        load.GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.Same(result, backend.GetCached(dependency));
    }

    /// <summary>The inline decision must be driven by real lock ownership, not by the thread being
    /// the loader thread - so <c>IsLoadingOnThisThread</c> has to be true inside the lock.</summary>
    [Fact]
    public void Get_ReportsThatTheLoadLockIsHeldOnTheCallingThread()
    {
        Guid root = Guid.NewGuid();
        var backend = new DependencyLoadingBackend(root, Guid.NewGuid());
        AssetDatabase.Current = backend;

        Assert.False(AssetBackendBase.IsLoadingOnThisThread,
            "No load is running on this thread yet.");

        backend.Get(root);

        Assert.True(backend.SawLockHeldInsideLoadFresh,
            "LoadFresh runs under the load lock, so IsLoadingOnThisThread must be true there.");

        Assert.False(AssetBackendBase.IsLoadingOnThisThread,
            "The depth must be unwound when Get returns, or later loads would resolve inline wrongly.");
    }

    /// <summary>The queue path still works: a caller that does NOT hold the lock goes through the
    /// loader thread, so the inline fix did not turn every blocking load into a main-thread load.</summary>
    [Fact]
    public void LoadBlocking_FromAThreadThatDoesNotHoldTheLock_StillResolvesThroughTheLoader()
    {
        Guid root = Guid.NewGuid();
        Guid dependency = Guid.NewGuid();
        var backend = new DependencyLoadingBackend(root, dependency);
        AssetDatabase.Current = backend;

        Assert.False(AssetBackendBase.IsLoadingOnThisThread);

        EngineObject? loaded = AssetLoader.LoadBlocking(dependency);

        Assert.NotNull(loaded);
        Assert.Same(loaded, backend.GetCached(dependency));
    }

    /// <summary>Nothing to wait for means nothing to time out: an already-cached id returns straight
    /// away, off the fast path before any queueing.</summary>
    [Fact]
    public void LoadBlocking_ForAnAlreadyLoadedAsset_ReturnsFromTheCache()
    {
        Guid root = Guid.NewGuid();
        Guid dependency = Guid.NewGuid();
        var backend = new DependencyLoadingBackend(root, dependency);
        AssetDatabase.Current = backend;

        var first = backend.Get(dependency);
        Assert.NotNull(first);

        Assert.Same(first, AssetLoader.LoadBlocking(dependency));
    }
}