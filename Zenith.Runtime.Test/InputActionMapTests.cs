// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for <see cref="InputActionMap"/>: action registration and removal, duplicate-name
/// rejection, lookups (FindAction/GetAction/indexer) and enable/disable cascading to the
/// member actions. The map is an EngineObject but needs no scene — constructed and disposed
/// right in the test.
/// </summary>
public class InputActionMapTests
{
    [Fact]
    public void Map_AddFindRemoveActions_DuplicateNamesThrow()
    {
        using var map = new InputActionMap("Test");
        Assert.Equal("Test", map.Name);

        using var defaults = new InputActionMap();
        Assert.Equal("New InputActionMap", defaults.Name);

        var move = map.AddAction("Move", InputActionType.Value);
        Assert.Same(map, move.ActionMap);
        Assert.Equal("Move", move.Name);

        // Duplicate names are rejected on both overloads.
        var duplicate = Assert.Throws<ArgumentException>(() => map.AddAction("Move"));
        Assert.Contains("already exists", duplicate.Message);
        var rogue = new InputAction("Move");
        Assert.Throws<ArgumentException>(() => map.AddAction(rogue));
        Assert.Null(rogue.ActionMap); // rejected before the map was assigned

        // An existing action can be adopted.
        var jump = new InputAction("Jump");
        map.AddAction(jump);
        Assert.Same(map, jump.ActionMap);

        Assert.Equal(2, map.Actions.Count);
        Assert.Same(jump, map.FindAction("Jump"));
        Assert.Same(move, map.GetAction("Move"));
        Assert.Same(jump, map["Jump"]);
        Assert.Null(map.FindAction("Nope"));
        Assert.Throws<KeyNotFoundException>(() => map.GetAction("Nope"));
        Assert.Throws<KeyNotFoundException>(() => map["Nope"]);

        // Removal disables the action and detaches it.
        Assert.True(map.RemoveAction("Move"));
        Assert.False(map.RemoveAction("Move"));
        Assert.False(move.Enabled);
        Assert.Null(move.ActionMap);
        Assert.Null(map.FindAction("Move"));
        Assert.Single(map.Actions);
        Assert.Contains("1 actions", map.ToString());
    }

    [Fact]
    public void Map_EnableDisable_CascadesToActions_AndUpdateSkipsDisabled()
    {
        using var map = new InputActionMap("Player");
        var jump = map.AddAction("Jump");
        jump.AddBinding(KeyCode.Space);
        var move = map.AddAction("Move", InputActionType.Value);

        Assert.False(map.Enabled); // fresh actions start disabled

        map.Enable();
        Assert.True(map.Enabled);
        Assert.All(map.Actions, a => Assert.Equal(InputActionPhase.Waiting, a.Phase));

        var handler = new FakeInputHandler();
        handler.PressKey(KeyCode.Space);
        map.UpdateActions(handler, 0.0f);

        Assert.Equal(InputActionPhase.Performed, jump.Phase);
        Assert.Equal(1.0f, jump.ReadValue<float>());
        Assert.Equal(InputActionPhase.Waiting, move.Phase); // no bindings → nothing to perform

        map.Disable();
        Assert.False(map.Enabled);
        Assert.All(map.Actions, a => Assert.Equal(InputActionPhase.Disabled, a.Phase));

        map.UpdateActions(handler, 0.1f); // disabled members are skipped entirely
        Assert.All(map.Actions, a => Assert.Equal(InputActionPhase.Disabled, a.Phase));
    }
}
