// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

/// <summary>
/// Per-test harness for the UI layout tests (Fase 3.1a Bloque 1). Pins the canvas screen size to
/// a known 1920x1080 so scale-dependent maths stay deterministic, drops LayoutUtility's static memo
/// around every test (it only expires inside GameCanvas.RebuildIfDirty, which these tests never
/// run - see H-UI-5) and owns every GameObject it hands out so nothing leaks into the next test.
/// </summary>
public sealed class UITestHelpers : IDisposable
{
    private readonly Float2? _prevScreenSizeOverride;
    private readonly List<GameObject> _owned = new();

    public UITestHelpers()
    {
        _prevScreenSizeOverride = GameCanvas.ScreenSizeOverride;
        GameCanvas.ScreenSizeOverride = new Float2(1920f, 1080f);
        LayoutUtility.InvalidateCache();
    }

    /// <summary>Creates a bare GameObject with a RectTransform, tracked for disposal.</summary>
    public GameObject NewRect(string name = "Rect")
    {
        GameObject go = NewGameObject(name);
        go.EnsureRectTransform();
        return go;
    }

    /// <summary>Creates a tracked GameObject without a RectTransform.</summary>
    public GameObject NewGameObject(string name = "TestObject")
    {
        var go = new GameObject(name);
        _owned.Add(go);
        return go;
    }

    /// <summary>Creates a tracked child under <paramref name="parent"/> and gives it a RectTransform.</summary>
    public GameObject AddChild(GameObject parent, string name = "Child")
    {
        GameObject go = NewGameObject(name);
        go.SetParent(parent);
        go.EnsureRectTransform();
        return go;
    }

    /// <summary>
    /// Creates a tracked canvas root in <paramref name="scene"/> (used by the Bloque 2 tests). The
    /// canvas converts its subtree when it enters the scene, so descendants added afterwards need
    /// EnsureRectTransform - AddChild does it - to be seen as UI.
    /// </summary>
    public GameObject CreateCanvas(Scene scene, string name = "Canvas")
    {
        GameObject go = NewGameObject(name);
        go.EnsureRectTransform();
        scene.Add(go);
        go.AddComponent<GameCanvas>();
        return go;
    }

    /// <summary>Exact component-wise rect comparison - every expected value here is exact.</summary>
    public static void AssertRect(Rect actual, float minX, float minY, float maxX, float maxY)
    {
        Assert.Equal(minX, actual.Min.X);
        Assert.Equal(minY, actual.Min.Y);
        Assert.Equal(maxX, actual.Max.X);
        Assert.Equal(maxY, actual.Max.Y);
    }

    public void Dispose()
    {
        GameCanvas.ScreenSizeOverride = _prevScreenSizeOverride;
        foreach (GameObject go in _owned)
            if (!go.IsDisposed)
                go.Dispose();
        _owned.Clear();
        LayoutUtility.InvalidateCache();
    }
}
