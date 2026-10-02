// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

/// <summary>
/// Fase 3.1b - tests de render de UI para <see cref="UIMeshBuilder"/>. El output se lee a traves
/// del <c>Bake</c> interno (InternalsVisibleTo concede al assembly de tests acceso) hacia un
/// <see cref="Mesh"/> normal: Vertices/UV/Colors32/Indices son arrays CPU y Upload es no-op con
/// <c>Graphics.IsHeadless</c> (precedente: <c>HeadlessGraphicsTests.Mesh_UploadHeadless_DoesNotThrow</c>),
/// asi que ningun test toca GPU real.
/// </summary>
/// <remarks>
/// GENERAL OBSERVATION (sin ID nuevo): los buffers estaticos de <c>ClipToRoundedRect</c>
/// (s_clipSrcVerts/UVs/Colors/Indices) y el pool estatico de Rent/Return no estan sincronizados.
/// Aqui es inocuo porque <c>TestAssemblyConfig</c> desactiva el paralelismo de la suite, pero el
/// builder no es seguro de usar desde varios threads.
/// </remarks>
public class UIMeshBuilderTests
{
    private static Mesh Bake(UIMeshBuilder builder)
    {
        var mesh = new Mesh();
        builder.Bake(mesh);
        return mesh;
    }

    /// <summary>Vertex/UV layout that every quad-producing primitive must follow.</summary>
    private static void AssertQuadGeometry(Mesh m, Rect r, Float2 uv0, Float2 uv1)
    {
        Assert.Equal(4, m.VertexCount);
        Assert.Equal(6, m.IndexCount);
        Assert.Equal(new Float3(r.Min.X, r.Min.Y, 0f), m.Vertices[0]);
        Assert.Equal(new Float3(r.Max.X, r.Min.Y, 0f), m.Vertices[1]);
        Assert.Equal(new Float3(r.Max.X, r.Max.Y, 0f), m.Vertices[2]);
        Assert.Equal(new Float3(r.Min.X, r.Max.Y, 0f), m.Vertices[3]);
        Assert.Equal(new Float2(uv0.X, uv0.Y), m.UV[0]);
        Assert.Equal(new Float2(uv1.X, uv0.Y), m.UV[1]);
        Assert.Equal(new Float2(uv1.X, uv1.Y), m.UV[2]);
        Assert.Equal(new Float2(uv0.X, uv1.Y), m.UV[3]);
    }

    // ============================================================
    // Quad + raw bridge (tabla de Fase A #1-#4)
    // ============================================================

    [Fact]
    public void AddQuad_EmitsPositionsIndicesUVsColors()
    {
        // KNOWN ISSUE: AddQuad documenta los vertices como "TL, TR, BR, BL" en orden
        // counter-clockwise, pero el espacio de canvas es +Y-up (Min.Y es el borde inferior, ver
        // AddFilledLinear/FillMethod origin 0=Bottom): el primer vertice es la esquina inferior
        // izquierda y ambos triangulos salen clockwise - la misma orientacion que
        // Mesh.GetFullscreenQuad. La geometria es correcta, el comentario es el que esta al reves.
        // See docs/PLAN_10_DE_10.md Fase 6. (H-UI-11)
        var builder = new UIMeshBuilder();
        builder.AddQuad(new Rect(10f, 20f, 110f, 70f), Color.Red, Float2.Zero, Float2.One);

        Assert.Equal(4, builder.VertexCount);
        Assert.Equal(6, builder.IndexCount);
        Assert.False(builder.IsEmpty);

        Mesh m = Bake(builder);
        AssertQuadGeometry(m, new Rect(10f, 20f, 110f, 70f), Float2.Zero, Float2.One);
        Assert.Equal(new uint[] { 0, 2, 1, 0, 3, 2 }, m.Indices);

        Color32 red = (Color32)Color.Red;
        Assert.Equal(red, m.Colors32[0]);
        Assert.Equal(red, m.Colors32[1]);
        Assert.Equal(red, m.Colors32[2]);
        Assert.Equal(red, m.Colors32[3]);
    }

    [Fact]
    public void AddQuad_SetUVRect_RemapsGeneratedShapesOnly()
    {
        var builder = new UIMeshBuilder();
        builder.SetUVRect(new Float2(0.25f, 0.5f), new Float2(0.5f, 0.25f));
        builder.AddQuad(new Rect(0f, 0f, 10f, 10f), Color.Red, Float2.Zero, Float2.One);

        // uv' = offset + uv * scale, aplicado a los 4 corners del quad
        Mesh m = Bake(builder);
        Assert.Equal(new Float2(0.25f, 0.5f), m.UV[0]);
        Assert.Equal(new Float2(0.75f, 0.5f), m.UV[1]);
        Assert.Equal(new Float2(0.75f, 0.75f), m.UV[2]);
        Assert.Equal(new Float2(0.25f, 0.75f), m.UV[3]);
    }

    [Fact]
    public void AddVertexAddIndex_BridgeKeepsRawUVAndPerVertexColor()
    {
        var builder = new UIMeshBuilder();
        builder.SetUVRect(new Float2(0.5f, 0.5f), new Float2(0.25f, 0.25f)); // no debe tocar AddVertex

        Assert.Equal(0u, builder.NextVertex);
        builder.AddVertex(new Float3(1f, 2f, 0f), new Float2(0.3f, 0.7f), new Color32(10, 20, 30, 40));
        builder.AddVertex(new Float3(3f, 4f, 0f), new Float2(0.1f, 0.9f), new Color32(50, 60, 70, 80));
        builder.AddVertex(new Float3(5f, 6f, 0f), new Float2(0.2f, 0.8f), new Color32(90, 100, 110, 120));
        Assert.Equal(3u, builder.NextVertex);

        builder.AddIndex(0);
        builder.AddIndex(1);
        builder.AddIndex(2);

        Mesh m = Bake(builder);
        Assert.Equal(3, m.VertexCount);
        Assert.Equal(3, m.IndexCount);
        Assert.Equal(new Float2(0.3f, 0.7f), m.UV[0]);
        Assert.Equal(new Float2(0.1f, 0.9f), m.UV[1]);
        Assert.Equal(new Color32(10, 20, 30, 40), m.Colors32[0]);
        Assert.Equal(new Color32(50, 60, 70, 80), m.Colors32[1]);
        Assert.Equal(new Color32(90, 100, 110, 120), m.Colors32[2]);
    }

    [Fact]
    public void IsEmpty_And_QuadsAccumulateWithBaseOffsets()
    {
        var raw = new UIMeshBuilder();
        raw.AddVertex(new Float3(0f, 0f, 0f), Float2.Zero, new Color32(1, 1, 1, 1));
        Assert.Equal(1, raw.VertexCount);
        Assert.Equal(0, raw.IndexCount);
        Assert.True(raw.IsEmpty); // vertices sin indices no producen geometria dibujable

        var builder = new UIMeshBuilder();
        builder.AddQuad(new Rect(0f, 0f, 10f, 10f), Color.Red, Float2.Zero, Float2.One);
        builder.AddQuad(new Rect(10f, 0f, 20f, 10f), Color.Blue, Float2.Zero, Float2.One);
        Assert.False(builder.IsEmpty);
        Assert.Equal(8, builder.VertexCount);
        Assert.Equal(12, builder.IndexCount);

        Mesh m = Bake(builder);
        Assert.Equal(new uint[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 }, m.Indices);
    }

    // ============================================================
    // Pool + Bake (tabla #5-#6)
    // ============================================================

    [Fact]
    public void RentReturn_PoolsAndResetsGeometryAndUVRect()
    {
        UIMeshBuilder builder = UIMeshBuilder.Rent();
        builder.SetUVRect(new Float2(0.25f, 0.25f), new Float2(0.5f, 0.5f));
        builder.AddQuad(new Rect(0f, 0f, 10f, 10f), Color.Red, Float2.Zero, Float2.One);
        Assert.Equal(4, builder.VertexCount);

        UIMeshBuilder.Return(builder);
        Assert.Equal(0, builder.VertexCount);
        Assert.Equal(0, builder.IndexCount);
        Assert.True(builder.IsEmpty);

        UIMeshBuilder recycled = UIMeshBuilder.Rent();
        Assert.Same(builder, recycled); // la pila es LIFO: devuelve la misma instancia

        recycled.AddQuad(new Rect(0f, 0f, 10f, 10f), Color.Red, Float2.Zero, Float2.One);
        Mesh m = Bake(recycled);
        Assert.Equal(Float2.Zero, m.UV[0]); // Return() dejo el UV rect en identidad
        Assert.Equal(Float2.One, m.UV[2]);

        UIMeshBuilder.Return(recycled);
    }

    [Fact]
    public void Bake_EmptyNoOp_ThenWritesArraysAndBoundsWithoutResettingBuilder()
    {
        var empty = new UIMeshBuilder();
        Assert.True(empty.IsEmpty);
        Mesh untouched = new Mesh();
        empty.Bake(untouched);
        Assert.Equal(0, untouched.VertexCount);
        Assert.Equal(0, untouched.IndexCount);

        var builder = new UIMeshBuilder();
        builder.AddQuad(new Rect(10f, 20f, 110f, 70f), Color.Red, Float2.Zero, Float2.One);

        Mesh m = new Mesh();
        builder.Bake(m);
        Assert.Equal(4, m.VertexCount);
        Assert.Equal(6, m.IndexCount);
        Assert.Equal(4, m.UV.Length);
        Assert.Equal(4, m.Colors32.Length);
        Assert.True(m.HasUV);
        Assert.True(m.HasColors32);
        Assert.Equal(new Float3(10f, 20f, 0f), m.bounds.Min);
        Assert.Equal(new Float3(110f, 70f, 0f), m.bounds.Max);

        // Bake no resetea el builder (eso lo hace Return/Reset)
        Assert.Equal(4, builder.VertexCount);
        Assert.Equal(6, builder.IndexCount);

        // hornear dos veces la misma geometria en el mismo mesh es seguro
        builder.Bake(m);
        Assert.Equal(4, m.VertexCount);
        Assert.Equal(6, m.IndexCount);
    }

    // ============================================================
    // AddRoundedRect (tabla #7-#8)
    // ============================================================

    [Fact]
    public void AddRoundedRect_RadiusGateFallsBackToQuad()
    {
        foreach (float radius in new[] { -5f, 0f, 0.5f })
        {
            var builder = new UIMeshBuilder();
            builder.AddRoundedRect(new Rect(0f, 0f, 100f, 50f), radius, Color.Green);
            Assert.Equal(4, builder.VertexCount);
            Assert.Equal(6, builder.IndexCount);
        }

        var sharp = new UIMeshBuilder();
        sharp.AddRoundedRect(new Rect(0f, 0f, 100f, 50f), 0f, Color.Green);
        Mesh m = Bake(sharp);
        AssertQuadGeometry(m, new Rect(0f, 0f, 100f, 50f), Float2.Zero, Float2.One);
    }

    [Fact]
    public void AddRoundedRect_FanCountsCenterUV_AndZeroSegmentsEmitsNaN()
    {
        // KNOWN ISSUE (winding): AddRoundedRect emite un fan cuyo perimetro recorre el rect en
        // sentido CCW (+Y-up), asi que sus triangulos quedan CCW mientras que AddQuad sale CW.
        // AddFilledRadial hace lo mismo y su comentario ("flip ... so triangles keep the same
        // orientation as AddQuad") tiene el sentido invertido. Hoy es invisible porque
        // DefaultUI/DefaultText declaran "Cull Off"; cualquier material UI con culling descartaria
        // una clase de primitivas. See docs/PLAN_10_DE_10.md Fase 6. (H-UI-12)
        // KNOWN ISSUE: AddRoundedRect(cornerSegments: 0) divide s/0f -> 0/0 = NaN y hornea
        // vertices/UVs NaN en el Mesh (sin guard en el parametro publico); con segments negativos
        // emite 1 vertice de centro sin indices y queda IsEmpty. See docs/PLAN_10_DE_10.md Fase 6. (H-UI-13)
        var builder = new UIMeshBuilder();
        builder.AddRoundedRect(new Rect(0f, 0f, 100f, 100f), 10f, Color.Green, cornerSegments: 6);

        // 1 centro + 4 esquinas * (6 segmentos + 1) puntos de perimetro
        Assert.Equal(29, builder.VertexCount);
        Assert.Equal(28 * 3, builder.IndexCount);

        Mesh m = Bake(builder);
        Assert.Equal(new Float3(50f, 50f, 0f), m.Vertices[0]);
        Assert.Equal(new Float2(0.5f, 0.5f), m.UV[0]);
        Assert.Equal(new uint[] { 0, 1, 2 }, m.Indices.Take(3).ToArray());

        // El radio redondea las esquinas: ningun vertice cae exactamente en una esquina valida.
        Assert.DoesNotContain(new Float3(0f, 0f, 0f), m.Vertices);
        Assert.DoesNotContain(new Float3(100f, 0f, 0f), m.Vertices);
        Assert.DoesNotContain(new Float3(100f, 100f, 0f), m.Vertices);
        Assert.DoesNotContain(new Float3(0f, 100f, 0f), m.Vertices);

        // Documentacion del comportamiento actual ante cornerSegments = 0 (H-UI-13)
        var zeroSegments = new UIMeshBuilder();
        zeroSegments.AddRoundedRect(new Rect(0f, 0f, 100f, 100f), 10f, Color.Green, cornerSegments: 0);
        Mesh nan = Bake(zeroSegments);
        Assert.Equal(5, nan.VertexCount); // 1 centro + 4 perimetro
        Assert.True(float.IsNaN(nan.Vertices[1].X));
        Assert.True(float.IsNaN(nan.UV[1].Y));
    }

    // ============================================================
    // AddTiled / AddNineSlice (tabla #9-#10)
    // ============================================================

    [Fact]
    public void AddTiled_CountsClipsPartialUV_AndInvalidFallsBackToQuad()
    {
        var builder = new UIMeshBuilder();
        builder.AddTiled(new Rect(0f, 0f, 250f, 100f), new Float2(100f, 100f), Color.Red);

        // ceil(250/100)=3 x ceil(100/100)=1 -> 3 quads
        Assert.Equal(12, builder.VertexCount);
        Assert.Equal(18, builder.IndexCount);

        Mesh m = Bake(builder);
        Assert.Equal(new Float3(200f, 0f, 0f), m.Vertices[8]);
        Assert.Equal(new Float3(250f, 100f, 0f), m.Vertices[10]);
        // el tile parcial recorta la UV para no estirar la textura: u1 = 50/100
        Assert.Equal(new Float2(0.5f, 0f), m.UV[9]);
        Assert.Equal(new Float2(0.5f, 1f), m.UV[10]);

        var fallback = new UIMeshBuilder();
        fallback.AddTiled(new Rect(0f, 0f, 250f, 100f), Float2.Zero, Color.Red);
        Assert.Equal(4, fallback.VertexCount);
        Assert.Equal(6, fallback.IndexCount);
        Mesh fm = Bake(fallback);
        AssertQuadGeometry(fm, new Rect(0f, 0f, 250f, 100f), Float2.Zero, Float2.One);
    }

    [Fact]
    public void AddNineSlice_NineQuadsWithBorderUVs()
    {
        // KNOWN ISSUE: AddNineSlice no clampa inner/uvBorders (solo UIImage.EmitSliced lo hace):
        // si los bordes en pixeles exceden el rect, el ctor de Rect SI normaliza el min/max
        // invertido - verificado: new Rect(10, 0, 0, 10) -> Min=(0,0), Max=(10,10) - asi que las
        // posiciones salen ordenadas, pero las UVs de border se guardan tal cual y la porcion
        // central queda con uv0.x > uv1.x. See docs/PLAN_10_DE_10.md Fase 6. (H-UI-16)
        var builder = new UIMeshBuilder();
        builder.AddNineSlice(
            new Rect(0f, 0f, 300f, 300f),
            new Float4(10f, 10f, 10f, 10f),       // left, top, right, bottom (pixeles)
            new Float4(0.1f, 0.1f, 0.1f, 0.1f),   // left, top, right, bottom (fraccion UV)
            Color.Red);

        Assert.Equal(36, builder.VertexCount); // 9 quads
        Assert.Equal(54, builder.IndexCount);

        Mesh m = Bake(builder);
        // la porcion central es el quad #4 -> vertices 16..19
        Assert.Equal(new Float3(10f, 10f, 0f), m.Vertices[16]);
        Assert.Equal(new Float3(290f, 10f, 0f), m.Vertices[17]);
        Assert.Equal(new Float3(290f, 290f, 0f), m.Vertices[18]);
        Assert.Equal(new Float3(10f, 290f, 0f), m.Vertices[19]);
        Assert.Equal(new Float2(0.1f, 0.1f), m.UV[16]);
        Assert.Equal(new Float2(0.9f, 0.9f), m.UV[18]);

        // Documentacion del comportamiento actual ante bordes excedidos (H-UI-16)
        var over = new UIMeshBuilder();
        over.AddNineSlice(
            new Rect(0f, 0f, 100f, 100f),
            new Float4(80f, 80f, 80f, 80f),
            new Float4(0.7f, 0.1f, 0.7f, 0.1f),
            Color.Red);
        Mesh om = Bake(over);
        // posiciones: Rect normaliza x1=80 > x2=20 -> el centro sale (20,20)-(80,80), ordenado
        Assert.Equal(new Float3(20f, 20f, 0f), om.Vertices[16]);
        Assert.Equal(new Float3(80f, 20f, 0f), om.Vertices[17]);
        // UVs: us = [0, 0.7, 0.3, 1] -> la porcion central queda invertida
        Assert.Equal(new Float2(0.7f, 0.1f), om.UV[16]);
        Assert.Equal(new Float2(0.3f, 0.1f), om.UV[17]);
        Assert.True(om.UV[16].X > om.UV[17].X);
    }

    // ============================================================
    // AddFilled (tabla #11-#14)
    // ============================================================

    [Fact]
    public void AddFilled_AmountGatesGeometry()
    {
        foreach (float amount in new[] { -0.5f, 0f })
        {
            var builder = new UIMeshBuilder();
            builder.AddFilled(new Rect(0f, 0f, 100f, 50f), Color.Red, FillMethod.Horizontal, 0, amount, false);
            Assert.Equal(0, builder.VertexCount);
            Assert.Equal(0, builder.IndexCount);
            Assert.True(builder.IsEmpty);
        }

        foreach (float amount in new[] { 1f, 2f }) // se clampa a 1 -> quad completo
        {
            var builder = new UIMeshBuilder();
            builder.AddFilled(new Rect(0f, 0f, 100f, 50f), Color.Red, FillMethod.Horizontal, 0, amount, false);
            Assert.Equal(4, builder.VertexCount);
            Assert.Equal(6, builder.IndexCount);
            Mesh m = Bake(builder);
            AssertQuadGeometry(m, new Rect(0f, 0f, 100f, 50f), Float2.Zero, Float2.One);
        }
    }

    [Fact]
    public void AddFilled_LinearOrigins_RectAndUV()
    {
        Rect rect = new(0f, 0f, 100f, 50f);

        // Horizontal, origin 0 = Left
        var left = new UIMeshBuilder();
        left.AddFilled(rect, Color.Red, FillMethod.Horizontal, 0, 0.25f, false);
        Mesh ml = Bake(left);
        AssertQuadGeometry(ml, new Rect(0f, 0f, 25f, 50f), Float2.Zero, new Float2(0.25f, 1f));

        // Horizontal, origin 1 = Right
        var right = new UIMeshBuilder();
        right.AddFilled(rect, Color.Red, FillMethod.Horizontal, 1, 0.25f, false);
        Mesh mr = Bake(right);
        AssertQuadGeometry(mr, new Rect(75f, 0f, 100f, 50f), new Float2(0.75f, 0f), Float2.One);

        // Vertical, origin 0 = Bottom (+Y up: Min.Y es el borde inferior)
        var bottom = new UIMeshBuilder();
        bottom.AddFilled(rect, Color.Red, FillMethod.Vertical, 0, 0.25f, false);
        Mesh mb = Bake(bottom);
        AssertQuadGeometry(mb, new Rect(0f, 0f, 100f, 12.5f), Float2.Zero, new Float2(1f, 0.25f));

        // Vertical, origin 1 = Top
        var top = new UIMeshBuilder();
        top.AddFilled(rect, Color.Red, FillMethod.Vertical, 1, 0.25f, false);
        Mesh mt = Bake(top);
        AssertQuadGeometry(mt, new Rect(0f, 37.5f, 100f, 50f), new Float2(0f, 0.75f), Float2.One);
    }

    [Fact]
    public void AddFilled_Radial360_WedgeCounts()
    {
        var builder = new UIMeshBuilder();
        builder.AddFilled(new Rect(0f, 0f, 100f, 100f), Color.Red, FillMethod.Radial360, 0, 0.5f, false);

        // pivot + stops [0, pi/4, 3pi/4, pi] (los corners BR y TR caen dentro de la mitad de barrido)
        Assert.Equal(5, builder.VertexCount);
        Assert.Equal(9, builder.IndexCount);

        Mesh m = Bake(builder);
        Assert.Equal(new Float3(50f, 50f, 0f), m.Vertices[0]);
        Assert.Equal(new Float2(0.5f, 0.5f), m.UV[0]);
        AssertNear(new Float3(50f, 0f, 0f), m.Vertices[1]);
        AssertNear(new Float3(100f, 0f, 0f), m.Vertices[2]);
        AssertNear(new Float3(100f, 100f, 0f), m.Vertices[3]);
        AssertNear(new Float3(50f, 100f, 0f), m.Vertices[4]);
        Assert.Equal(new uint[] { 0, 1, 2 }, m.Indices.Take(3).ToArray());
    }

    [Fact]
    public void AddFilled_InvalidOrigin_NoOp()
    {
        var builder = new UIMeshBuilder();
        builder.AddFilled(new Rect(0f, 0f, 100f, 100f), Color.Red, FillMethod.Radial90, 99, 0.5f, false);

        Assert.Equal(0, builder.VertexCount);
        Assert.Equal(0, builder.IndexCount);
        Assert.True(builder.IsEmpty);
    }

    // ============================================================
    // ClipToRoundedRect (tabla #15-#16)
    // ============================================================

    [Fact]
    public void ClipToRoundedRect_InsideOutsideAndNoOps()
    {
        // KNOWN ISSUE: el "fast path" para triangulos completamente dentro que documenta el XML de
        // ClipToRoundedRect no esta implementado (siempre corre Sutherland-Hodgman completo, aqui
        // es functionalmente equivalente), y el `Cap = 48` del workspace no cubre
        // cornerSegments=11 con su propia formula (3 + 4*(11+1) = 51 > 48) -> posible
        // IndexOutOfRangeException en los stackalloc. Sin llamadores en produccion hoy.
        // See docs/PLAN_10_DE_10.md Fase 6. (H-UI-14)

        // (a) radio por debajo del umbral -> no-op
        var belowThreshold = new UIMeshBuilder();
        belowThreshold.AddQuad(new Rect(0f, 0f, 100f, 100f), Color.Red, Float2.Zero, Float2.One);
        belowThreshold.ClipToRoundedRect(new Rect(0f, 0f, 100f, 100f), 0.4f);
        Assert.Equal(4, belowThreshold.VertexCount);
        Assert.Equal(6, belowThreshold.IndexCount);

        // (b) builder vacio -> no-op sin throw
        var empty = new UIMeshBuilder();
        empty.ClipToRoundedRect(new Rect(0f, 0f, 100f, 100f), 20f);
        Assert.True(empty.IsEmpty);

        // (c) quad completamente dentro -> se re-emite triangulo a triangulo (el clip procesa
        // triángulos sueltos, no el quad), asi que el resultado son 2 triángulos = 6 vertices.
        var inside = new UIMeshBuilder();
        inside.AddQuad(new Rect(20f, 20f, 80f, 80f), Color.Red, Float2.Zero, Float2.One);
        inside.ClipToRoundedRect(new Rect(0f, 0f, 100f, 100f), 20f);
        Assert.Equal(6, inside.VertexCount);
        Assert.Equal(6, inside.IndexCount);

        Mesh im = Bake(inside);
        Assert.Equal(new uint[] { 0, 1, 2, 3, 4, 5 }, im.Indices);
        // triángulo 1 = (v0, v2, v1) del quad original
        Assert.Equal(new Float3(20f, 20f, 0f), im.Vertices[0]);
        Assert.Equal(new Float3(80f, 80f, 0f), im.Vertices[1]);
        Assert.Equal(new Float3(80f, 20f, 0f), im.Vertices[2]);
        Assert.Equal(new Float2(0f, 0f), im.UV[0]);
        Assert.Equal(new Float2(1f, 1f), im.UV[1]);
        Assert.Equal(new Float2(1f, 0f), im.UV[2]);
        // triángulo 2 = (v0, v3, v2) del quad original
        Assert.Equal(new Float3(20f, 20f, 0f), im.Vertices[3]);
        Assert.Equal(new Float3(20f, 80f, 0f), im.Vertices[4]);
        Assert.Equal(new Float3(80f, 80f, 0f), im.Vertices[5]);
        Assert.Equal(new Float2(0f, 1f), im.UV[4]);
        Assert.Equal(new Float2(1f, 1f), im.UV[5]);

        // (d) quad completamente fuera -> descartado por completo
        var outside = new UIMeshBuilder();
        outside.AddQuad(new Rect(200f, 200f, 300f, 300f), Color.Red, Float2.Zero, Float2.One);
        outside.ClipToRoundedRect(new Rect(0f, 0f, 100f, 100f), 20f);
        Assert.Equal(0, outside.VertexCount);
        Assert.Equal(0, outside.IndexCount);
        Assert.True(outside.IsEmpty);
    }

    [Fact]
    public void ClipToRoundedRect_PartialClip_ClipsAndKeepsUVIn01()
    {
        var builder = new UIMeshBuilder();
        builder.AddQuad(new Rect(-50f, -50f, 50f, 50f), Color.Red, Float2.Zero, Float2.One);
        builder.ClipToRoundedRect(new Rect(0f, 0f, 100f, 100f), 20f);

        Assert.True(builder.VertexCount > 4); // los corners fuera fueron reemplazados por intersecciones
        Assert.Equal(0, builder.IndexCount % 3);

        Mesh m = Bake(builder);
        Assert.Equal(0, m.IndexCount % 3);
        foreach (uint index in m.Indices)
            Assert.True(index < (uint)m.VertexCount);

        foreach (Float3 v in m.Vertices)
        {
            Assert.InRange(v.X, -0.001f, 100.001f);
            Assert.InRange(v.Y, -0.001f, 100.001f);
            Assert.Equal(0f, v.Z);
        }

        // los UVs se interpolan en las intersecciones -> siguen dentro de 0..1
        foreach (Float2 uv in m.UV)
        {
            Assert.InRange(uv.X, -0.001f, 1.001f);
            Assert.InRange(uv.Y, -0.001f, 1.001f);
        }

        // el unico vertice original que estaba dentro sobrevive intacto
        Assert.Contains(m.Vertices, v => MathF.Abs(v.X - 50f) < 0.001f && MathF.Abs(v.Y - 50f) < 0.001f);
    }

    private static void AssertNear(Float3 expected, Float3 actual, int precision = 2)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }
}
