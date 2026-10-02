// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

/// <summary>
/// Per-test harness for the UI tests (Fase 3.1a Bloque 1 y 2). Pins the canvas screen size to
/// a known 1920x1080 so scale-dependent maths stay deterministic, pushes a deterministic
/// <see cref="TimeData"/> and a <see cref="FakeInputHandler"/> so Selectable/EventSystem never see
/// the wall clock or a real device, drops LayoutUtility's static memo around every test (it only
/// expires inside GameCanvas.RebuildIfDirty, which these tests never rely on - see H-UI-5) and owns
/// every GameObject and Scene it hands out so nothing leaks into the next test.
/// </summary>
public sealed class UITestHelpers : IDisposable
{
    private readonly Float2? _prevScreenSizeOverride;
    private readonly bool _prevIsPlaying;
    private readonly TimeData _time;
    private readonly int _prevInputDepth;
    private readonly List<GameObject> _owned = new();
    private readonly List<Scene> _ownedScenes = new();

    /// <summary>Input handler pushed for this test; set keys/buttons/wheel directly on it.</summary>
    internal FakeInputHandler Input { get; }

    /// <summary>The deterministic time pushed for this test; mutate DeltaTime/Time (or AdvanceTime) directly.</summary>
    public TimeData Time => _time;

    public UITestHelpers()
    {
        _prevScreenSizeOverride = GameCanvas.ScreenSizeOverride;
        GameCanvas.ScreenSizeOverride = new Float2(1920f, 1080f);
        _prevIsPlaying = Application.IsPlaying;
        Application.IsPlaying = true;

        _time = new TimeData { DeltaTime = 1f / 60f, Time = 100f };
        Prowl.Runtime.Time.TimeStack.Push(_time);

        Input = new FakeInputHandler();
        _prevInputDepth = Prowl.Runtime.Input.Handlers.Count;
        Prowl.Runtime.Input.PushHandler(Input);

        LayoutUtility.InvalidateCache();
    }

    /// <summary>Advances the pushed game time - drives Time.TimeSinceStartup (EventSystem click streaks).</summary>
    public void AdvanceTime(float seconds) => _time.Time += seconds;

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
    /// Creates a tracked canvas root in <paramref name="scene"/>. The canvas converts its subtree
    /// when it enters the scene, so descendants added afterwards need EnsureRectTransform -
    /// AddChild does it - to be seen as UI.
    /// </summary>
    public GameObject CreateCanvas(Scene scene, string name = "Canvas")
    {
        GameObject go = NewGameObject(name);
        go.EnsureRectTransform();
        scene.Add(go);
        go.AddComponent<GameCanvas>();
        return go;
    }

    /// <summary>
    /// Creates a tracked scene. makeCurrent: true runs Scene.Load + ProcessPendingLoad so the scene
    /// becomes Scene.Current (EventSystem.Tick reads Scene.Current, and the outgoing current scene -
    /// if any - is disposed by ProcessPendingLoad itself); otherwise the scene is enabled in place
    /// but stays off the Current slot (UIRaycaster.TryPick takes the scene explicitly).
    /// </summary>
    public Scene NewScene(bool makeCurrent = false)
    {
        Scene scene = new();
        _ownedScenes.Add(scene);
        if (makeCurrent)
        {
            Scene.Load(scene);
            Scene.ProcessPendingLoad();
        }
        else
        {
            scene.Enable();
        }
        return scene;
    }

    /// <summary>
    /// Adds an EventSystem to <paramref name="scene"/> with a fixed 1920x1080 host viewport.
    /// The viewport is always written: EventSystem.Tick with a null viewport dereferences
    /// Window.InternalWindow, which does not exist headless.
    /// KNOWN ISSUE: EventSystem.Tick sin Viewport hace NRE headless (Window.InternalWindow null). See docs/PLAN_10_DE_10.md Fase 6. (H-UI-7)
    /// </summary>
    public EventSystem NewEventSystem(Scene scene, Float2? pointer = null, bool receivesInput = true)
    {
        GameObject go = NewRect("EventSystem");
        scene.Add(go);
        EventSystem es = go.AddComponent<EventSystem>();
        es.Viewport = new EventSystem.HostViewport
        {
            ReferenceSize = new Float2(1920f, 1080f),
            PointerPosition = pointer ?? Float2.Zero,
            ReceivesInput = receivesInput,
        };
        return es;
    }

    /// <summary>Writes the host-viewport pointer position (window pixels, top-left origin, +Y down).</summary>
    public void SetPointer(EventSystem es, float screenX, float screenY)
    {
        EventSystem.HostViewport vp = es.Viewport
            ?? new EventSystem.HostViewport { ReferenceSize = new Float2(1920f, 1080f) };
        vp.PointerPosition = new Float2(screenX, screenY);
        es.Viewport = vp;
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
        // 1) Scenes first: their Disable fires OnDisable (EventSystem clears Current + state),
        //    so nothing dangling points into the teardown that follows.
        foreach (Scene scene in _ownedScenes)
        {
            if (scene.IsDisposed) continue;
            if (scene.IsActive) scene.Disable();
            scene.Dispose();
        }
        _ownedScenes.Clear();

        // 2) GOs handed out outside any scene.
        foreach (GameObject go in _owned)
            if (!go.IsDisposed)
                go.Dispose();
        _owned.Clear();

        // 3) Pop only the handlers this harness pushed, leaving any pre-existing stack intact.
        while (Prowl.Runtime.Input.Handlers.Count > _prevInputDepth)
            Prowl.Runtime.Input.PopHandler();

        // 4) Pop the pushed TimeData, but only if it is still on top (a test may have pushed more).
        if (Prowl.Runtime.Time.TimeStack.Count > 0 && ReferenceEquals(Prowl.Runtime.Time.TimeStack.Peek(), _time))
            Prowl.Runtime.Time.TimeStack.Pop();

        Application.IsPlaying = _prevIsPlaying;
        GameCanvas.ScreenSizeOverride = _prevScreenSizeOverride;
        LayoutUtility.InvalidateCache();

        // 5) Hygiene: a live EventSystem after teardown is a real leak (its OnDisable never ran).
        Assert.True(EventSystem.Current is null || EventSystem.Current.IsDisposed,
            $"EventSystem.Current quedó vivo tras Dispose: {EventSystem.Current?.Name}");
    }
}
