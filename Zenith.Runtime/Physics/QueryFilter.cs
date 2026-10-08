// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>
/// Which colliders a physics query is allowed to hit. Every query overload that used to take a bare
/// <see cref="Runtime.LayerMask"/> takes one of these instead; a LayerMask converts implicitly, so
/// layer-only call sites read the same as before.
/// <para/>
/// The exclusions are what a bare layer mask cannot express: casting from a character or a vehicle
/// almost always has to skip the caster's own colliders, and layers are a blunt instrument for that.
/// </summary>
public struct QueryFilter
{
    /// <summary>Layers the query may hit.</summary>
    public LayerMask LayerMask;

    /// <summary>Skip everything attached to this rigidbody, so a cast cannot hit its own caster.</summary>
    public Rigidbody3D IgnoreRigidbody;

    /// <summary>Skip this one collider.</summary>
    public Collider IgnoreCollider;

    /// <summary>
    /// Skip every collider in this set. A character with a visual mesh collider under it has more
    /// than one shape of its own to skip, and a filter that can name only one of them leaves the
    /// rest to collide with their own controller.
    /// <para>
    /// Kept alongside <see cref="IgnoreCollider"/> rather than replacing it, so existing single
    /// exclusion call sites keep reading the way they do and both kinds of query share one path.
    /// </para>
    /// </summary>
    public Collider[] IgnoreColliders;

    /// <summary>Hits anything on any layer.</summary>
    public static readonly QueryFilter Default = new(LayerMask.Everything);

    public QueryFilter(LayerMask layerMask)
    {
        LayerMask = layerMask;
    }

    /// <summary>This filter, additionally skipping everything attached to <paramref name="rigidbody"/>.</summary>
    public readonly QueryFilter Ignoring(Rigidbody3D rigidbody)
    {
        QueryFilter filter = this;
        filter.IgnoreRigidbody = rigidbody;
        return filter;
    }

    /// <summary>This filter, additionally skipping <paramref name="collider"/>.</summary>
    public readonly QueryFilter Ignoring(Collider collider)
    {
        QueryFilter filter = this;
        filter.IgnoreCollider = collider;
        return filter;
    }

    /// <summary>This filter, additionally skipping every collider in <paramref name="colliders"/>.</summary>
    public readonly QueryFilter Ignoring(IReadOnlyList<Collider> colliders)
    {
        QueryFilter filter = this;

        if (colliders is null || colliders.Count == 0) return filter;

        Collider[] copy = new Collider[colliders.Count];
        for (int i = 0; i < colliders.Count; i++) copy[i] = colliders[i];

        // Merge rather than replace: a caller that already excluded one collider must not lose it.
        if (filter.IgnoreCollider.IsValid())
        {
            Collider[] merged = new Collider[copy.Length + 1];
            merged[0] = filter.IgnoreCollider;
            Array.Copy(copy, 0, merged, 1, copy.Length);
            copy = merged;
        }

        filter.IgnoreColliders = copy;
        return filter;
    }

    /// <summary>Whether anything is excluded beyond the layer mask. Lets queries skip the owner lookup.</summary>
    internal readonly bool HasExclusions =>
        IgnoreRigidbody.IsValid() || IgnoreCollider.IsValid() || HasIgnoredColliderSet;

    private readonly bool HasIgnoredColliderSet => IgnoreColliders is { Length: > 0 };

    /// <summary>Whether this specific collider is excluded, by either exclusion field.</summary>
    internal readonly bool Excludes(Collider candidate)
    {
        if (candidate.IsNotValid()) return false;
        if (IgnoreCollider.IsValid() && candidate == IgnoreCollider) return true;

        Collider[] set = IgnoreColliders;
        if (set is null) return false;

        for (int i = 0; i < set.Length; i++)
            if (set[i] == candidate) return true;

        return false;
    }

    public static implicit operator QueryFilter(LayerMask layerMask) => new(layerMask);
}
