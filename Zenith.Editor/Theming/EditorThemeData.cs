using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using Prowl.OrigamiUI;

namespace Prowl.Editor.Theming;

/// <summary>Static editor-background style used when the animated background is off.</summary>
public enum EditorBackgroundStyle { Color }

/// <summary>
/// A color ramp with a single primary color. Other stops are computed from RGB offsets.
/// Optionally, all stops can be individually overridden.
/// </summary>
public class ColorRamp
{
    /// <summary>Primary color (the ★ stop) as hex.</summary>
    public string Primary { get; set; } = "#FF00FF";

    /// <summary>When true, use Overrides[] instead of computed offsets.</summary>
    public bool OverrideAll { get; set; }

    /// <summary>Per-stop override colors as hex. Only used when OverrideAll=true.</summary>
    public string[]? Overrides { get; set; }

    /// <summary>Number of stops in this ramp.</summary>
    [JsonIgnore] public int StopCount { get; set; }

    /// <summary>Index of the primary stop (0-based).</summary>
    [JsonIgnore] public int PrimaryIndex { get; set; }

    /// <summary>RGB offsets from primary for each stop. Set by Init().</summary>
    [JsonIgnore] public (int R, int G, int B)[] Offsets { get; set; } = [];

    /// <summary>Get the resolved color for a stop index.</summary>
    public Color GetStop(int index)
    {
        if (OverrideAll && Overrides != null && index < Overrides.Length)
            return ParseHex(Overrides[index]);

        if (index == PrimaryIndex)
            return ParseHex(Primary);

        var p = ParseHex(Primary);
        if (index >= Offsets.Length) return p;
        var o = Offsets[index];
        return Color.FromArgb(255,
            Math.Clamp(p.R + o.R, 0, 255),
            Math.Clamp(p.G + o.G, 0, 255),
            Math.Clamp(p.B + o.B, 0, 255));
    }

    /// <summary>Initialize offsets from default colors. Call once at startup.</summary>
    public void Init(int stopCount, int primaryIndex, Color[] defaults)
    {
        StopCount = stopCount;
        PrimaryIndex = primaryIndex;
        var pc = defaults[primaryIndex];
        Offsets = new (int, int, int)[stopCount];
        for (int i = 0; i < stopCount; i++)
            Offsets[i] = (defaults[i].R - pc.R, defaults[i].G - pc.G, defaults[i].B - pc.B);

        // Initialize overrides array from defaults if not set
        if (Overrides == null || Overrides.Length != stopCount)
        {
            Overrides = new string[stopCount];
            for (int i = 0; i < stopCount; i++)
                Overrides[i] = ColorToHex(defaults[i]);
        }
    }

    /// <summary> Converts a Color to a hex string in RRGGBB format. </summary>
    public static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary> Parses a hex color string (e.g. #FF00FF) to a Color. Returns Color.Magenta on parse failure. </summary>
    public static Color ParseHex(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return Color.Magenta; }
    }
}

/// <summary>
/// Serializable theme data. Ramps store a single primary color + optional per-stop overrides.
/// </summary>
public class EditorThemeData
{
    /// <summary> Display name of this theme. </summary>
    public string Name { get; set; } = "Graphite";

    // Color ramps (customization overlaid onto Origami's defaults). Primary = the bright ★ C500 stop.
    /// <summary> Neutral color ramp for backgrounds and surfaces. </summary>
    public ColorRamp Neutral { get; set; } = new() { Primary = "#121212" };
    /// <summary> Accent color ramp (warm white). </summary>
    public ColorRamp Purple { get; set; } = new() { Primary = "#EDDEDD" };
    /// <summary> Info neutral color ramp (grey). </summary>
    public ColorRamp Blue { get; set; } = new() { Primary = "#8A8A8A" };
    /// <summary> Danger / error color ramp. </summary>
    public ColorRamp Red { get; set; } = new() { Primary = "#FB7185" };
    /// <summary> Success color ramp. </summary>
    public ColorRamp Green { get; set; } = new() { Primary = "#4ADE80" };
    /// <summary> Warning / amber color ramp. </summary>
    public ColorRamp Amber { get; set; } = new() { Primary = "#FBBF24" };
    /// <summary> Ink color ramp for text and high-contrast elements. </summary>
    public ColorRamp Ink { get; set; } = new() { Primary = "#EDDEDD" };

    // Font
    /// <summary> Name of the default UI font. </summary>
    public string DefaultFontName { get; set; } = "Geist";

    /// <summary> Name of the default bold UI font. </summary>
    public string DefaultBoldFontName { get; set; } = "Geist";

    /// <summary> User interface scale factor. </summary>
    public float UserScale { get; set; } = 1f;

    // Sizing
    /// <summary> Height of the menu bar in pixels. </summary>
    public float MenuBarHeight { get; set; } = 40f;
    /// <summary> Height of a single row in pixels. </summary>
    public float RowHeight { get; set; } = 24f;
    /// <summary> Default font size in points. </summary>
    public float FontSize { get; set; } = 17f;
    /// <summary> Width of property labels in pixels. </summary>
    public float LabelWidth { get; set; } = 150f;
    /// <summary> Spacing between UI elements in pixels. </summary>
    public float Spacing { get; set; } = 4f;
    /// <summary> Padding inside UI elements in pixels. </summary>
    public float Padding { get; set; } = 6f;
    // Single knob driving both the dock gutter padding and the splitter thickness.
    /// <summary> Dock gutter padding and splitter thickness in pixels. </summary>
    public float DockSpacing { get; set; } = 6f;
    /// <summary> Height of the tab bar in pixels. </summary>
    public float TabBarHeight { get; set; } = 32f;
    /// <summary> Padding inside tabs in pixels. </summary>
    public float TabPadding { get; set; } = 12f;
    /// <summary> Corner roundness radius in pixels. </summary>
    public float Roundness { get; set; } = 6f;

    // Effects
    /// <summary> Whether glass surfaces use a blur effect. </summary>
    public bool GlassBlur { get; set; } = true;
    /// <summary> Strength of the glass blur effect. </summary>
    public float BlurAmount { get; set; } = 22f;
    /// <summary> Whether drop shadows are rendered. </summary>
    public bool DropShadows { get; set; } = true;
    /// <summary> Whether accent elements have a glow effect. </summary>
    public bool AccentGlow { get; set; } = true;
    /// <summary> Whether anti-aliasing is enabled. </summary>
    public bool AntiAliasing { get; set; } = true;

    // Background: static style (gradient / solid colour / frozen nebula).
    /// <summary> Static background style. </summary>
    public EditorBackgroundStyle BackgroundStyle { get; set; } = EditorBackgroundStyle.Color;
    /// <summary> Background color as hex. </summary>
    public string BackgroundColorA { get; set; } = "#121212";

    // Default ramp stops (RGB) = Origami's ramps. Customization is applied on top of Origami's
    // live theme, preserving each stop's alpha, so translucent glass surfaces stay glass.
    private static readonly Color[] DefaultNeutral = [H("#080808"), H("#0A0A0A"), H("#1A1A1A"), H("#121212"), H("#121212"), H("#181818"), H("#242424")];
    private static readonly Color[] DefaultPurple  = [H("#D5C5C5"), H("#DCCCCC"), H("#E2D0D0"), H("#EBDBDB"), H("#EDDEDD"), H("#F5EDED"), H("#FFFFFF")];
    private static readonly Color[] DefaultBlue    = [H("#3A3A3A"), H("#4A4A4A"), H("#5A5A5A"), H("#6A6A6A"), H("#8A8A8A"), H("#A0A0A0"), H("#C0C0C0")];
    private static readonly Color[] DefaultRed     = [H("#2A1818"), H("#3A2222"), H("#5A3030"), H("#7A4040"), H("#FB7185"), H("#FC9999"), H("#FDBDBD")];
    private static readonly Color[] DefaultGreen   = [H("#0F1A0F"), H("#162C1F"), H("#1F3A28"), H("#2D4A38"), H("#4ADE80"), H("#78E6A0"), H("#AAF0C3")];
    private static readonly Color[] DefaultAmber   = [H("#1F1808"), H("#2A2210"), H("#3A2E18"), H("#4A3A20"), H("#FBBF24"), H("#FCD060"), H("#FAE0A0")];
    private static readonly Color[] DefaultInk     = [H("#5A5A5A"), H("#6A6A6A"), H("#8A8A8A"), H("#A0A0A0"), H("#EDDEDD"), H("#FFFFFF"), H("#FFFFFF")];

    private static Color H(string hex) => ColorTranslator.FromHtml(hex);

    /// <summary>Initialize ramp offsets from the Origami defaults. Must be called after deserialization.</summary>
    public void InitRamps()
    {
        Neutral.Init(7, 4, DefaultNeutral);   // ★ = C500 (index 4)
        Purple.Init(7, 4, DefaultPurple);
        Blue.Init(7, 4, DefaultBlue);
        Red.Init(7, 4, DefaultRed);
        Green.Init(7, 4, DefaultGreen);
        Amber.Init(7, 4, DefaultAmber);
        Ink.Init(7, 4, DefaultInk);
    }

    /// <summary>Overlay this customization onto Origami's live theme: overwrite each ramp's RGB from the
    /// edited stops while keeping Origami's per-stop alpha (so translucent surfaces stay glass).</summary>
    public void ApplyTo(OrigamiTheme t)
    {
        ApplyRamp(t.Neutral, Neutral);
        ApplyRamp(t.Primary, Purple);
        ApplyRamp(t.Blue, Blue);
        ApplyRamp(t.Red, Red);
        ApplyRamp(t.Green, Green);
        ApplyRamp(t.Amber, Amber);
        ApplyRamp(t.Ink, Ink);
    }

    private static void ApplyRamp(OrigamiRamp dst, ColorRamp src)
    {
        dst.C100 = KeepAlpha(dst.C100, src.GetStop(0));
        dst.C200 = KeepAlpha(dst.C200, src.GetStop(1));
        dst.C300 = KeepAlpha(dst.C300, src.GetStop(2));
        dst.C400 = KeepAlpha(dst.C400, src.GetStop(3));
        dst.C500 = KeepAlpha(dst.C500, src.GetStop(4));
        dst.C600 = KeepAlpha(dst.C600, src.GetStop(5));
        dst.C700 = KeepAlpha(dst.C700, src.GetStop(6));
    }

    private static Color KeepAlpha(Color dst, Color rgb) => Color.FromArgb(dst.A, rgb.R, rgb.G, rgb.B);

    /// <summary> Creates a deep clone of this theme data. </summary>
    public EditorThemeData Clone()
    {
        var json = JsonSerializer.Serialize(this);
        var clone = JsonSerializer.Deserialize<EditorThemeData>(json)!;
        clone.InitRamps();
        return clone;
    }

    /// <summary> Serializes this theme to a JSON file at the given path. </summary>
    public void ExportToFile(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary> Deserializes a theme from a JSON file. Returns null if the file cannot be read or parsed. </summary>
    public static EditorThemeData? ImportFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<EditorThemeData>(json);
            data?.InitRamps();
            return data;
        }
        catch { return null; }
    }

    /// <summary> Creates a default EditorThemeData with initialized ramps. </summary>
    public static EditorThemeData CreateDefault()
    {
        var d = new EditorThemeData();
        d.InitRamps();
        return d;
    }
}
