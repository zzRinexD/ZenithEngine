// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// A character standing exactly on a surface could not move at all. The shape cast starts in
/// contact, reports a hit at zero distance, <see cref="CharacterController"/> clamped the
/// advance to 0, and the projected slide then cancelled the motion.
///
/// On a triangulated floor it is much worse than "sluggish": the capsule's rounded bottom
/// grazes the surface, and the internal edges of a <see cref="MeshCollider"/> make that grazing
/// contact pick up a lateral normal that faces the motion head-on. The character reads that as
/// a wall and stops dead.
/// </summary>
public class CharacterControllerRestingContactTests : RuntimeTestBase
{
    private FakeInputHandler _input = null!;

    /// <summary>A triangulated floor. Concave mesh colliders build one shape per triangle, which
    /// is exactly where a grazing contact picks up a normal from a neighbouring face.</summary>
    private GameObject CreateMeshFloor(Float3[] vertices, uint[] indices, string name)
    {
        var mesh = new Mesh { Vertices = vertices };
        mesh.Indices = indices;

        var go = CreateGameObject(name);
        var mc = go.AddComponent<MeshCollider>();
        mc.Mesh = mesh;
        mc.Convex = false;
        return go;
    }

    /// <summary>A grid of quads sharing vertices, so every internal triangle edge is a candidate.</summary>
    private static (Float3[] verts, uint[] indices) Grid(int cells = 8, float size = 60f)
    {
        var verts = new List<Float3>();
        var indices = new List<uint>();
        float step = size / cells;
        float h = size * 0.5f;

        for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
                verts.Add(new Float3(-h + x * step, 0f, -h + z * step));

        int Idx(int x, int z) => z * (cells + 1) + x;
        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                int a = Idx(x, z), b = Idx(x + 1, z), c = Idx(x + 1, z + 1), d = Idx(x, z + 1);
                indices.Add((uint)a); indices.Add((uint)b); indices.Add((uint)c);
                indices.Add((uint)a); indices.Add((uint)c); indices.Add((uint)d);
            }
        }

        return (verts.ToArray(), indices.ToArray());
    }

    /// <summary>Floor plus a capsule character wired to a camera, resting exactly on it.</summary>
    private (Scene scene, CharacterController ctrl) CreateRig(GameObject floor)
    {
        _input = new FakeInputHandler();
        Input.PushHandler(_input);

        Scene scene = CreateScene(enable: true);
        scene.Add(floor);

        var player = CreateGameObject("Player");
        scene.Add(player);
        var ctrl = player.AddComponent<CharacterController>();
        ctrl.Shape = CharacterController.ColliderShape.Capsule;
        ctrl.Size = new Float3(0.8f, 1.8f, 0.8f);
        player.Transform.Position = new Float3(0f, 0.9f, 0f);

        var camGo = CreateGameObject("Camera");
        scene.Add(camGo);
        var cam = camGo.AddComponent<OrbitFollowCamera>();
        cam.Target = player.Transform;
        cam.Mode = OrbitFollowCamera.OrbitMode.HoldRightClick;
        cam.CollisionEnabled = false;

        var move = player.AddComponent<ThirdPersonCharacterMovement>();
        move.Camera = cam;
        move.Controller = ctrl;

        // Let it settle onto the floor and let the camera publish a movement basis.
        Tick(scene, 8);
        return (scene, ctrl);
    }

    private static float DistanceFrom(CharacterController ctrl, Float3 origin) =>
        Float3.Distance(ctrl.GameObject.Transform.Position, origin);

    /// <summary>Two triangles: the smallest triangulated floor there is. The character must walk.</summary>
    [Fact]
    public void Character_WalksOnMeshColliderFloor_QuadOfTwoTriangles()
    {
        const float h = 20f;
        Float3[] verts =
        [
            new(-h, 0f, -h), new(h, 0f, -h), new(h, 0f, h), new(-h, 0f, h),
        ];
        var (scene, ctrl) = CreateRig(CreateMeshFloor(verts, [0u, 1u, 2u, 0u, 2u, 3u], "QuadFloor"));

        Assert.True(ctrl.IsGrounded, "Precondition: the character must be resting on the mesh.");

        Float3 before = ctrl.GameObject.Transform.Position;
        _input.PressKey(KeyCode.W);
        Tick(scene, 40);

        Assert.True(DistanceFrom(ctrl, before) > 2f,
            $"A character standing exactly on a mesh floor must be able to walk. Moved {DistanceFrom(ctrl, before):F4}.");
    }

    /// <summary>
    /// A grid, where every internal triangle edge can produce the lateral normal. This is the
    /// case that left the character stuck.
    /// </summary>
    [Fact]
    public void Character_WalksOnMeshColliderFloor_Grid()
    {
        var (verts, indices) = Grid(cells: 8);
        var (scene, ctrl) = CreateRig(CreateMeshFloor(verts, indices, "GridFloor"));

        Assert.True(ctrl.IsGrounded, "Precondition: the character must be resting on the mesh.");

        Float3 before = ctrl.GameObject.Transform.Position;
        _input.PressKey(KeyCode.W);
        Tick(scene, 40);

        Assert.True(DistanceFrom(ctrl, before) > 2f,
            $"A character on a triangulated grid floor must be able to walk. Moved {DistanceFrom(ctrl, before):F4}.");
    }
}