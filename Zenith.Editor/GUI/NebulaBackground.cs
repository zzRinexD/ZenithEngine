using System;

using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.GUI;

/// <summary>
/// The animated "Nebula" backdrop drawn behind the editor's translucent glass panels (and the
/// project launcher): a deep void, slowly wandering nebula clouds, a baked tileable starfield and
/// the occasional comet.
/// </summary>
public sealed class NebulaBackground
{
    private readonly Paper _paper;
    private readonly Random _rng = new(20240630);
    private float _time;

    private static Color Col(int r, int g, int b, float a = 1f) => Color32.FromArgb((int)Math.Round(a * 255), r, g, b);

    // Per-layer visibility + the raw void colour behind everything (all settable from the theme).
    /// <summary> Whether to draw the coloured nebula gradients. </summary>
    public bool ShowClouds = true;    // the coloured nebula gradients

    // Theme tint (primary/secondary); the whole nebula is coloured from these.
    private System.Drawing.Color _primary = System.Drawing.Color.FromArgb(168, 85, 247);
    private System.Drawing.Color _secondary = System.Drawing.Color.FromArgb(96, 165, 250);
    private Color ColC(System.Drawing.Color c, float a) => Col(c.R, c.G, c.B, a);
    private Color Lighten(System.Drawing.Color c, float t, float a) =>
        Col((int)(c.R + (255 - c.R) * t), (int)(c.G + (255 - c.G) * t), (int)(c.B + (255 - c.B) * t), a);

    // Each cloud glides at constant speed while its heading eases toward a slowly-changing target.
    private struct Cloud { public float cx, cy, ang, targetAng, timer, rf, phase; public Color color; }
    private readonly Cloud[] _clouds = new Cloud[4];

    private const float CloudSpeed = 0.045f;
    private const float CloudTurn = 2.5f;

    /// <summary> Initialises the four nebula clouds with randomised positions, sizes and colours. </summary>
    public NebulaBackground(Paper paper)
    {
        _paper = paper;

        var cc = new[] { Col(168, 85, 247, 0.40f), Col(217, 107, 216, 0.26f), Col(80, 90, 220, 0.24f), Col(52, 211, 238, 0.14f) };
        var cr = new[] { 0.42f, 0.40f, 0.40f, 0.30f };
        var sx = new[] { 0.43f, 0.67f, 0.52f, 0.14f };
        var sy = new[] { 0.37f, 0.55f, 0.92f, 0.60f };
        for (int i = 0; i < 4; i++)
            _clouds[i] = new Cloud { cx = sx[i], cy = sy[i], ang = (float)(_rng.NextDouble() * MathF.Tau), rf = cr[i], color = cc[i], phase = (float)(_rng.NextDouble() * 6.28f), timer = 0f };
    }

    /// <summary> Advances the nebula animation (clouds and comets) by the given time delta. </summary>
    public void Update(float dt)
    {
        _time += dt;
        UpdateClouds(dt);
    }

    /// <summary>Tint the two dominant nebula clouds with the theme's primary/secondary colours
    /// (alpha preserved), so the animated background tracks the active theme.</summary>
    public void Retint(System.Drawing.Color primary, System.Drawing.Color secondary)
    {
        _primary = primary;
        _secondary = secondary;
        _clouds[0].color = ColC(primary, 0.40f);
        _clouds[1].color = ColC(secondary, 0.26f);
        _clouds[2].color = ColC(secondary, 0.22f);
        _clouds[3].color = Lighten(primary, 0.25f, 0.16f);
    }

    /// <summary>Pull the current theme's tint + per-layer visibility + void colour onto this instance.</summary>
    public void ApplyThemeSettings()
    {
        Retint(EditorTheme.Accent, EditorTheme.Blue400);
        ShowClouds = true;
    }

    /// <summary>The single editor-backdrop draw path, shared by the editor shell and the launcher: applies
    /// the theme settings, advances the animation (respecting speed / frozen), then paints the animated
    /// nebula, a static nebula, a gradient, or a solid colour per the Effects settings.</summary>
    public static void DrawEditorBackground(Paper paper, NebulaBackground nebula, string id, float w, float h, float dt)
    {
        nebula.ApplyThemeSettings();
        nebula.Update(0f);

        var box = paper.Box(id).PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, h).IsNotInteractable();
        box.BackgroundColor(EditorTheme.BackgroundColorA);
    }

    private void UpdateClouds(float dt)
    {
        dt = Math.Min(dt, 0.1f);
        float turn = 1f - MathF.Exp(-dt / CloudTurn);
        for (int i = 0; i < _clouds.Length; i++)
        {
            ref Cloud c = ref _clouds[i];

            if (c.cx < -0.2f || c.cx > 1.2f || c.cy < -0.2f || c.cy > 1.2f)
                c.targetAng = MathF.Atan2(0.5f - c.cy, 0.5f - c.cx);
            else if ((c.timer -= dt) <= 0f)
            {
                c.targetAng = (float)(_rng.NextDouble() * MathF.Tau);
                c.timer = (float)(3.0 + _rng.NextDouble() * 5.0);
            }

            float da = (float)MathF.IEEERemainder(c.targetAng - c.ang, MathF.Tau);
            c.ang += da * turn;
            c.cx += MathF.Cos(c.ang) * CloudSpeed * dt;
            c.cy += MathF.Sin(c.ang) * CloudSpeed * dt;
        }
    }

    /// <summary> Draws the full nebula background (void fill, clouds) into the specified rectangle. </summary>
    public void Draw(Canvas vg, Rect rect)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y, w = (float)rect.Size.X, h = (float)rect.Size.Y;
        float big = Math.Max(w, h);

        vg.BeginPath(); vg.Rect(x, y, w, h); vg.SetFillColor(ColC(System.Drawing.Color.FromArgb(6, 4, 9), 1f)); vg.Fill();

        float t = _time;

        if (ShowClouds)
        {
            RadialFill(vg, x, y, w, h, x + w * 0.7f, y + h * 0.2f, 0, big * 0.95f,
                Col((int)(_primary.R * 0.16f), (int)(_primary.G * 0.17f), (int)(_primary.B * 0.19f), 1f), Col(5, 3, 12, 0f));

            foreach (var c in _clouds)
            {
                float rad = w * c.rf * (1f + 0.10f * MathF.Sin(t * 0.09f + c.phase));
                RadialFill(vg, x, y, w, h, x + c.cx * w, y + c.cy * h, 0, rad, c.color, Color32.FromArgb(0, c.color));
            }

            RadialFill(vg, x, y, w, h,
                x + w * 0.5f + MathF.Sin(t * 0.06f) * 22f,
                y + h * 0.40f + MathF.Sin(t * 0.05f + 2.0f) * 16f,
                0, w * 0.46f, Lighten(_primary, 0.12f, 0.38f), ColC(_primary, 0f));
        }

        RadialFill(vg, x, y, w, h, x + w * 0.5f, y + h * 0.4f, big * 0.55f, big * 0.9f, Col(0, 0, 0, 0f), Col(0, 0, 0, 0.55f));
    }

    private static void RadialFill(Canvas vg, float x, float y, float w, float h, float cx, float cy, float ir, float or, Color inner, Color outer)
    {
        vg.SaveState();
        vg.SetRadialBrush(cx, cy, ir, or, inner, outer);
        vg.BeginPath(); vg.Rect(x, y, w, h); vg.Fill();
        vg.RestoreState();
    }
}
