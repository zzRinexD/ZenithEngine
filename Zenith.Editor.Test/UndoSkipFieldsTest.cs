using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prowl.Editor.Core;
using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Guards against silent drift in Undo._undoSkipFields: if a field in the runtime is
/// renamed without updating the skip list, undo could start overwriting identity/
/// internal state. This test fails when a skip-listed name no longer matches any
/// field in Prowl.Runtime.
/// </summary>
public class UndoSkipFieldsTest
{
    // Names in _undoSkipFields that are intentionally kept as defensive no-ops
    // even though they don't currently match any field (e.g. renamed or never existed).
    // Adding to this list is an explicit decision — do NOT add entries just to silence a failure.
    //
    // Inventory (2026-09-29, reflection over Prowl.Runtime, 3978 field names): all 10
    // current entries match real fields — _identifier, _instanceID, _enabledInHierarchy,
    // _go, _hasStarted, _hasBeenEnabled, _executeAlwaysCached, AssetID, AssetPath and
    // <IsDisposed>k__BackingField. Intentionally empty until a rename makes one orphaned.
    private static readonly HashSet<string> s_knownOrphans = new()
    {
    };

    [Fact]
    public void AllSkipFieldsMatchRealRuntimeFields()
    {
        var runtimeAsm = typeof(Prowl.Runtime.GameObject).Assembly;

        // Collect every field name declared anywhere in the runtime assembly.
        var realFieldNames = new HashSet<string>();
        foreach (var type in runtimeAsm.GetTypes())
        {
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Static
                                             | BindingFlags.Public | BindingFlags.NonPublic))
            {
                realFieldNames.Add(f.Name);
            }
        }

        var skipFields = Undo.GetSkipFieldsForTesting();

        var orphans = skipFields.Where(name => !realFieldNames.Contains(name)
                                            && !s_knownOrphans.Contains(name))
                                .ToList();

        Assert.True(orphans.Count == 0,
            $"Undo._undoSkipFields contains names that no longer match any field in Prowl.Runtime: {string.Join(", ", orphans)}. " +
            $"Either fix the name (field was renamed) or add it to s_knownOrphans with a comment explaining why.");
    }
}
