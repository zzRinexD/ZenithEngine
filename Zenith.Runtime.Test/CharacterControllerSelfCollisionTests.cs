// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// A <see cref="CharacterController"/> whose visual mesh carries its own collider collided with
/// that mesh. The collider sat on a child GameObject, and the query filter only excluded the
/// rigidbody on the controller's own GameObject, so the mesh was a perfectly valid obstacle.
///
/// Nothing about it looked like a wall, so <c>Depenetrate</c> pushed the capsule out of it every
/// frame while <c>CollideAndSlide</c> pushed it back. The two cancelled the requested movement and
/// left a constant drift pointing the opposite way: measured at 0.88 units per second against a
/// requested 5, with dot(delta, requested) = -1.00. In game it read as "S moves me forward".
///
/// The filter now also excludes the colliders on the controller's own GameObject and its
/// descendants. A collider on a <em>sibling</em> GameObject is a real obstacle and still collides,
/// which is what keeps this from turning into a blanket "ignore everything nearby".
/// </summary>
public class CharacterControllerSelfCollisionTests : RuntimeTestBase
{
    public enum ColliderKind { Box, Capsule }

    /// <summary>One large quad at y=0, standing in for an imported plane mesh.</summary>
    private GameObject CreateFloor()
    {
        float h = 30f;
        Float3[] verts =
        [
            new(-h, 0f, -h), new(h, 0f, -h), new(h, 0f, h), new(-h, 0f, h),
        ];
        var mesh = new Mesh { Vertices = verts };
        mesh.Indices = [0u, 1u, 2u, 0u, 2u, 3u];

        GameObject go = CreateGameObject("Floor");
        var mc = go.AddComponent<MeshCollider>();
        mc.Mesh = mesh;
        mc.Convex = false;
        return go;
    }

    private (Scene scene, GameObject player, CharacterController ctrl) CreateCharacter()
    {
        Scene scene = CreateScene(enable: true);
        scene.Add(CreateFloor());

        GameObject player = CreateGameObject("Player");
        scene.Add(player);
        var ctrl = player.AddComponent<CharacterController>();
        ctrl.Shape = CharacterController.ColliderShape.Capsule;
        ctrl.Size = new Float3(0.8f, 1.8f, 0.8f);
        player.Transform.Position = new Float3(0f, 0.9f, 0f);

        Tick(scene, 8);
        for (int i = 0; i < 60 && !ctrl.IsGrounded; i++) Tick(scene, 1);

        return (scene, player, ctrl);
    }

    /// <summary>
    /// Requests +X at 5 u/s for one second and returns the horizontal displacement.
    /// </summary>
    private static Float3 MoveForOneSecond(GameObject player, CharacterController ctrl)
    {
        Float3 start = player.Transform.Position;
        const float step = 5f / 60f;

        for (int i = 0; i < 60; i++)
            ctrl.Move(new Float3(step, -0.01f, 0f));

        Float3 delta = player.Transform.Position - start;
        delta.Y = 0f;
        return delta;
    }

    /// <summary>
    /// Walk +X for one second on a rig with no collider of its own, and report the displacement.
    /// This is the reference every "with my own collider" test is compared against.
    /// </summary>
    private Float3 MoveReference()
    {
        var (scene, player, ctrl) = CreateCharacter();
        return MoveForOneSecond(player, ctrl);
    }

    /// <summary>
    /// The bug, and the invariant that fixes it.
    /// <para>
    /// The character is rigged the usual way: a visual mesh parented under the controller so it
    /// follows the body, carrying its own collider. Before the fix the controller collided with
    /// that mesh, and since the mesh is not a wall, <c>Depenetrate</c> shoved the capsule out of it
    /// every frame while <c>CollideAndSlide</c> pushed it back. Measured: 0.88 units of drift per
    /// second <em>sideways</em>, against a requested 5 units straight ahead.
    /// </para>
    /// <para>
    /// The assertion is that a rig with its own colliders behaves exactly like one without, rather
    /// than that the character reaches some distance. Asserting a distance here would be asserting
    /// the resting-contact fix, which is a separate defect (documented as PENDIENTE 1 in
    /// docs/PLAN_CHARACTER_CAMERA_FIX.md) and still open: on a floor the controller currently does
    /// not travel at all. Comparing against the no-collider reference isolates this bug from that
    /// one, and still fails loudly if the drift comes back.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(ColliderKind.Box)]
    [InlineData(ColliderKind.Capsule)]
    public void OwnColliderOnChild_BehavesExactlyLikeHavingNone(ColliderKind kind)
    {
        Float3 reference = MoveReference();

        var (scene, player, ctrl) = CreateCharacter();
        AddVisualCollider(scene, player, kind);

        // Re-resolve explicitly: a collider added after OnEnable is exactly the case a cache
        // resolved at enable time cannot see, and this API exists for callers in that position.
        ctrl.ResolveSelfBody();
        Tick(scene, 2);

        Float3 actual = MoveForOneSecond(player, ctrl);

        Assert.True(MathF.Abs(actual.X - reference.X) < 0.02f
                 && MathF.Abs(actual.Z - reference.Z) < 0.02f,
            $"A mesh collider on a child must not change how the controller moves. " +
            $"With it: X={actual.X:F3} Z={actual.Z:F3}. Without: X={reference.X:F3} Z={reference.Z:F3}. " +
            "A difference means the controller is colliding with its own mesh.");
    }

    /// <summary>
    /// The drift's specific signature, stated on its own so a regression is unambiguous: movement
    /// must never go backwards or sideways relative to what was asked for.
    /// </summary>
    [Fact]
    public void OwnColliderNeverPushesTheCharacterBackwardsOrSideways()
    {
        var (scene, player, ctrl) = CreateCharacter();
        AddVisualCollider(scene, player, ColliderKind.Box);
        ctrl.ResolveSelfBody();
        Tick(scene, 2);

        Float3 displacement = MoveForOneSecond(player, ctrl);

        // Requested direction was +X, so X must not go negative and Z must stay put.
        Assert.True(displacement.X > -0.02f,
            $"The character must never be pushed against the requested +X: got X={displacement.X:F3}.");
        Assert.True(MathF.Abs(displacement.Z) < 0.05f,
            $"There must be no sideways drift from self-collision: Z={displacement.Z:F3}.");
    }

    private void AddVisualCollider(Scene scene, GameObject player, ColliderKind kind)
    {
        GameObject visual = CreateGameObject("Visual");
        scene.Add(visual);
        visual.SetParent(player, worldPositionStays: false);
        visual.Transform.LocalPosition = Float3.Zero;

        if (kind == ColliderKind.Box)
        {
            visual.AddComponent<BoxCollider>().Size = new Float3(1f, 1.8f, 1f);
        }
        else
        {
            var capsule = visual.AddComponent<CapsuleCollider>();
            capsule.Radius = 0.5f;
            capsule.Height = 1.8f;
        }
    }

    /// <summary>
    /// Two colliders under the controller, not one. A mesh plus an accessory must not reintroduce
    /// the bug for the second shape, which a single-exclusion fix would have done.
    /// </summary>
    public void SeveralOwnColliders_AreAllExcluded()
    {
        Float3 reference = MoveReference();

        var (scene, player, ctrl) = CreateCharacter();

        foreach (string name in new[] { "Visual", "Accessory", "Gear" })
        {
            GameObject child = CreateGameObject(name);
            scene.Add(child);
            child.SetParent(player, worldPositionStays: false);
            child.Transform.LocalPosition = Float3.Zero;
            child.AddComponent<BoxCollider>().Size = new Float3(1f, 1.8f, 1f);
        }

        ctrl.ResolveSelfBody();
        Tick(scene, 2);

        Float3 actual = MoveForOneSecond(player, ctrl);

        Assert.True(MathF.Abs(actual.X - reference.X) < 0.02f
                 && MathF.Abs(actual.Z - reference.Z) < 0.02f,
            $"Three own colliders must behave like none: X={actual.X:F3} Z={actual.Z:F3} " +
            $"vs reference X={reference.X:F3} Z={reference.Z:F3}.");
    }

    /// <summary>
    /// The pre-existing exclusion must survive. A controller sitting on a rigidbody still has to
    /// ignore that body's colliders, or a controller dropped onto a dynamic body jams immediately.
    /// </summary>
    [Fact]
    public void RigidbodyOnOwnGameObject_StillExcluded()
    {
        Scene scene = CreateScene(enable: true);
        scene.Add(CreateFloor());

        GameObject player = CreateGameObject("Player");
        scene.Add(player);
        var body = player.AddComponent<Rigidbody3D>();
        var ctrl = player.AddComponent<CharacterController>();
        ctrl.Shape = CharacterController.ColliderShape.Capsule;
        ctrl.Size = new Float3(0.8f, 1.8f, 0.8f);
        player.Transform.Position = new Float3(0f, 0.9f, 0f);

        Assert.NotNull(body);
        Assert.True(body.IsValid(), "Precondition: the rig must have a rigidbody.");

        Tick(scene, 8);
        for (int i = 0; i < 60 && !ctrl.IsGrounded; i++) Tick(scene, 1);

        // The rigidbody generates its own collider, which the controller must skip.
        ctrl.ResolveSelfBody();

        Float3 actual = MoveForOneSecond(player, ctrl);

        Assert.True(MathF.Abs(actual.Z) < 0.05f,
            $"A controller on a rigidbody must not collide with its own body: drifted Z={actual.Z:F3}.");
        Assert.True(actual.X > -0.02f,
            $"A controller on a rigidbody must not be pushed backwards: X={actual.X:F3}.");
    }

    /// <summary>
    /// A collider on a SIBLING must still collide. This is the guard that stops the fix becoming a
    /// blanket "ignore everything nearby": a sibling is a different body and a genuine obstacle, so
    /// walking into one has to stop the character.
    /// </summary>
    [Fact]
    public void ColliderOnSiblingGameObject_StillCollides()
    {
        var (scene, player, ctrl) = CreateCharacter();

        // A sibling of the player: same parent (the scene), not a descendant of the player.
        GameObject wall = CreateGameObject("Wall");
        scene.Add(wall);
        wall.AddComponent<BoxCollider>().Size = new Float3(1f, 3f, 4f);
        wall.Transform.Position = new Float3(1.5f, 1.5f, 0f);

        ctrl.ResolveSelfBody();
        Tick(scene, 2);

        Float3 displacement = WalkIntoWall(player, ctrl);

        Assert.True(displacement.X < 1.5f,
            $"A sibling collider is a real wall and must stop the character; it travelled X={displacement.X:F3} " +
            "and walked through or past it.");
    }

    /// <summary>Walk into the wall for two seconds, well past it if collision were ignored.</summary>
    private static Float3 WalkIntoWall(GameObject player, CharacterController ctrl)
    {
        Float3 start = player.Transform.Position;
        const float step = 5f / 60f;

        for (int i = 0; i < 120; i++)
            ctrl.Move(new Float3(step, -0.01f, 0f));

        Float3 delta = player.Transform.Position - start;
        delta.Y = 0f;
        return delta;
    }

    /// <summary>A descendant several levels down must be excluded, not just direct children.</summary>
    [Fact]
    public void DeepDescendantCollider_IsAlsoExcluded()
    {
        Float3 reference = MoveReference();

        var (scene, player, ctrl) = CreateCharacter();

        GameObject mid = CreateGameObject("Mid");
        scene.Add(mid);
        mid.SetParent(player, worldPositionStays: false);

        GameObject deep = CreateGameObject("Deep");
        scene.Add(deep);
        deep.SetParent(mid, worldPositionStays: false);
        deep.Transform.LocalPosition = Float3.Zero;
        deep.AddComponent<BoxCollider>().Size = new Float3(1f, 1.8f, 1f);

        ctrl.ResolveSelfBody();
        Tick(scene, 2);

        Float3 actual = MoveForOneSecond(player, ctrl);

        Assert.True(MathF.Abs(actual.X - reference.X) < 0.02f
                 && MathF.Abs(actual.Z - reference.Z) < 0.02f,
            $"A deep descendant must be excluded too: X={actual.X:F3} Z={actual.Z:F3} " +
            $"vs reference X={reference.X:F3} Z={reference.Z:F3}.");
    }

    /// <summary>An inactive mesh collider must still be excluded, so enabling it later is safe.</summary>
    [Fact]
    public void InactiveOwnCollider_IsStillExcluded()
    {
        Float3 reference = MoveReference();

        var (scene, player, ctrl) = CreateCharacter();

        GameObject visual = CreateGameObject("Visual");
        scene.Add(visual);
        visual.SetParent(player, worldPositionStays: false);
        visual.Transform.LocalPosition = Float3.Zero;
        var box = visual.AddComponent<BoxCollider>();
        box.Size = new Float3(1f, 1.8f, 1f);

        visual.Enabled = false;
        ctrl.ResolveSelfBody();
        Tick(scene, 2);

        // Enable it mid-run: if the cache had skipped inactive objects, this is the moment the
        // character would start colliding with itself.
        visual.Enabled = true;
        Tick(scene, 2);

        Float3 actual = MoveForOneSecond(player, ctrl);

        Assert.True(MathF.Abs(actual.X - reference.X) < 0.02f
                 && MathF.Abs(actual.Z - reference.Z) < 0.02f,
            $"An inactive own collider must be excluded too, or enabling it mid-run reintroduces " +
            $"self-collision: X={actual.X:F3} Z={actual.Z:F3} " +
            $"vs reference X={reference.X:F3} Z={reference.Z:F3}.");
    }
}