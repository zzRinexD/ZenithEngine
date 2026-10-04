# Auditoría de calidad de tests

Consolida un análisis externo recibido el 2026-10 (ejecutado a las 18:32, antes de H-RD-52/53/54) y
**verifica sus conclusiones contra el código fuente**. El resultado modifica la lectura del análisis:
una de sus tres categorías|resulta no ser fiable.

Los 12 ficheros de artefactos (`_*_list.txt`, `_zero.txt`) se han eliminado; este doc los sustituye.

---

## 1. Qué contiene el análisis original

Doce ficheros, dos proyectos, cuatro ficheros por proyecto con el mismo formato `columna<TAB>columna`:

| Fichero | Contenido | Runtime | Editor |
|---|---|---|---|
| `_zero_list.txt` | **Universo**: todos los tests del proyecto (`fichero.cs`, `nombre`) | 956 | 668 |
| `_trivial_list.txt` | Clasificación: `TRIVIAL`/`NOASSERT` + nº de aserciones | 282 | 127 |
| `_boolonly.txt` | Tests cuyas únicas aserciones son booleanas | 171 | 86 |
| `_noassert_list.txt` | Tests sin aserciones (= subconjunto `NOASSERT`) | 42 | 6 |
| `_zero.txt` | Subconjunto mínimo (⊂ `_zero_list`) | 4 | 3 |
| `_smoke_list.txt` | Muestra de smoke tests | 11 | 6 |

Relaciones confirmadas: `_boolonly` ⊂ `_trivial`; `_noassert_list` == filas `NOASSERT` de
`_trivial_list` (idénticas tras normalizar columnas); `_zero.txt` ⊂ `_zero_list.txt`.

---

## 2. Veredicto por categoría — sólo una fiable

| Categoría | Tests | ¿Fiable? | Evidencia |
|---|---|---|---|
| `_boolonly` | 257 tests | ✅ **Sí** | 257/257 revisados: ninguno contiene una aserción no booleana |
| `TRIVIAL` (nº de aserciones) | 361 tests | ✅ **Sí** | De los 112 marcados "1 aserción", **108 tienen exactamente 1** (4 tienen 2) |
| `NOASSERT` | 48 tests | ❌ **No** | **43 de 48 sí asertan** (90% de falsos positivos) |

### Por qué falla `NOASSERT`

La herramienta contaba sólo llamadas `Assert.*` **en el cuerpo del método**. Los tests de este repo
asignan mediante **helpers**, y esos helpers no los ve:

```csharp
// TransformTests.cs - clasificado como "sin aserciones"
public void WorldMatrix_Reparent_InvalidatesCache()
{
    ...
    AssertVec(new Float3(11, 0, 0), WorldOrigin(child));   // ← el helper no se contó
    child.SetParent(b, false);
    AssertVec(new Float3(101, 0, 0), WorldOrigin(child));
}
```

Afecta a casi todos los `TransformTests`, `LayoutGroupTests`, `RectTransformTests`, `UIMeshBuilderTests`
y a varios de `BuildSystemTests`. **Consecuencia: el "hotspot" que signalaba el análisis
(`TransformTests.cs` con 20 de 48 tests sin aserciones) es falso** — 19 de esos 20 sí verifican.

**Los tests de este repo no se debilitan con helpers; el análisis no los entendió.**

---

## 3. Cifras reales (verificadas contra el código)

| Categoría | Runtime | Editor | Total |
|---|---|---|---|
| Tests en el universo del análisis | 956 | 668 | 1 624 |
| Métodos hoy sin clasificar | 75 | 14 | **89** |
| Entradas del análisis ya obsoletas | **0** | **0** | **0** |
| `TRIVIAL` (aserciones débiles) | 240 | 121 | 361 |
| `boolonly` | 171 | 86 | **257** |
| `NOASSERT` **según el análisis** | 42 | 6 | 48 |
| `NOASSERT` **real** | 5 | 0 | **5** |

El análisis cubre 956/1031 métodos de Runtime (92,7%) y 668/682 de Editor (97,9%). Los 89 sin
clasificar incluyen los 11 tests nuevos de H-RD-52/53/54, que postdatan al análisis.

### Distribución de aserciones (`TRIVIAL`, fiable)

| Aserciones | Runtime | Editor | Total |
|---|---|---|---|
| 1 | 71 | 41 | **112** |
| 2 | 74 | 46 | 120 |
| 3 | 43 | 21 | 64 |
| 4 | 28 | 5 | 33 |
| 5-11 | 24 | 8 | 32 |

### Dónde se concentran

| Fichero | `boolonly` | Fichero | `TRIVIAL` con 1 aserción |
|---|---|---|---|
| `PrefabTests.cs` | 21 | `PhysicsTests.cs` | 16 |
| `TerrainTests.cs` | 17 | `BuildSystemTests.cs` | 9 |
| `PhysicsTests.cs` | 16 | `PrefabTests.cs` | 9 |
| `BuildSystemTests.cs` | 16 | `TerrainTests.cs` | 8 |
| `NavMeshComponentTests.cs` | 15 | `LayoutGroupTests.cs` | 7 |

---

## 4. Los 5 tests que de verdad no verifican nada

Todos son **"must not throw"**: ejecutan código y confían en que no salte excepción. Es un estilo
legítimo, pero el nombre promete más de lo que comprueba.

| Test | Lo que promete | Lo que hace |
|---|---|---|
| `SceneDispatcherTests.AThrowingCallback_IsContainedRatherThanUnwinding` | Que la excepción se contiene y **no** desenrolla | La añade y no comprueba nada: si el dispatcher dejara propagar, el test **pasa** |
| `ProwlActionTests.Invoke_OnANullTarget_IsANoOp` | Que invocar sobre null es no-op | Invoca; sólo detecta si lanza |
| `SceneDispatcherTests.PhysicsEvent_WithNoHandlers_DoesNothing` | Que sin handlers no ocurre nada | Llama; no observa ningún efecto |
| `SceneDispatcherTests.PhysicsEvent_OnNullGameObject_IsIgnored` | Que se ignoran | Llama con null; sólo detecta si lanza |
| `ComponentClipboardTests.PasteValues_Undo_AfterComponentRemoved_FailsGracefully` | Que falla con elegancia | Ejecuta undo/redo; no comprueba el estado ni que sea "graceful" |

`AThrowingCallback_IsContainedRatherThanUnwinding` es el más peligroso: su nombre afirma
precisamente el invariante que no mide.

---

## 5. Patrones detectados

1. **El análisis externo no es reutilizable tal cual.** Su detector de aserciones no ve helpers, así
   que cualquier métrica basada en "cuántas aserciones" será una **cota inferior**. Los ~1 600 tests
   de este repo usan helpers de aserción con frecuencia; una reauditoría debería contar llamadas a
   helpers.
2. **El riesgo real no son los tests sin aserciones (5), sino los de una sola aserción (112) y los
   booleanos (257).** Un `Assert.True(x)` con una condicióncompound es válido; lo que no dice es
   *qué* verifica. Ése es el trabajo de revisión que queda.
3. **Concentración por dominio, no por fichero.** `PrefabTests`, `TerrainTests`, `PhysicsTests` y
   `BuildSystemTests` ocupan los cuatro primeros puestos en ambas métricas. Son los sitios donde
   un test débil cuesta más: son physiquement sistemas con invariantes numéricas.
4. **La cobertura del análisis es parcial pero no está obsoleta**: 0 entradas obsoletas, 89 métodos sin
   clasificar. Reutilizable como punto de partida si se corrige el detector.
5. Cruce con el trabajo reciente: los tests de `HierarchyPanel`, `RenderPipeline` y `AudioBuffer`
   —añadidos en fases anteriores— **no aparecen** en las categorías débiles.

---

## 6. Recomendaciones

| # | Acción | Esfuerzo |
|---|---|---|
| 1 | Arreglar los 5 tests de la sección 4: o asertan el efecto, o se renombran a `..._DoesNotThrow` | Bajo, alto valor |
| 2 | Revisar los 112 tests de una sola aserción en los 4 ficheros concentrados | Medio |
| 3 | Reejecutar el análisis con detección de aserciones vía helpers antes de volver a confiar en él | Bajo |
| 4 | No reauditar los 43 falsos positivos de `NOASSERT`: ya verificados | — |

**Ninguno de estos tests ha fallado nunca**, y el repo tiene 1 996 tests verdes. El riesgo aquí es
**falsa confianza**, no una rotura: un test que no verifica da la misma sensación de cobertura que
uno que sí.

---

## 7. Procedencia y reproducibilidad

Los 12 artefactos se generaron por una herramienta externa y se eliminaron tras consolidar este
resumen (patrón añadido a `.gitignore` para que no vuelvan). Lo único que se conserva es lo verificado
aquí; las listas brutas de 1 624 nombres no eran manejables y su único valor era el agregado que este
doc resume.

**Confianza:** alta en las cifras de `boolonly` y `TRIVIAL` (verificadas test a test contra el
código); **nula** en la categoría `NOASSERT` del análisis original, refutada con 43 contraejemplos.