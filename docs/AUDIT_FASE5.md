# Auditoría Fase 5 — Rendimiento del editor y del pipeline

Registro de la Fase 5 (medir antes de optimizar), completada el 2026-10. Complementa a
`AUDIT_H_ED.md` (43 bugs de paneles) y `AUDIT_RD.md` (51 bugs de render): aquellos inventarían
problemas, éste dice **cuáles de ellos son de verdad lentos** y cuáles no.

**Regla de oro de la fase: nada de optimizar sin medir.** Las 8 hipótesis iniciales se medieron primero;
**2 resultaron ser P0 reales, 1 era P2 y 5 se descartaron**. Una de las P0 ni siquiera estaba en la lista.

---

## 1. Método

Arnés temporal en el proyecto de tests (`GC.GetAllocatedBytesForCurrentThread()` + `Stopwatch`,
min-de-5), ejecutado en tres entornos:

1. **Headless** (proyecto de tests): sin GPU, sin fuente.
2. **Editor real**: `Prowl.Editor` lanzado con GPU (Intel UHD, OpenGL 4.1) y fuentes cargadas,
   instrumentando el frame loop durante 181 frames de calentamiento.
3. **Aislamiento de artefactos**: cuando una cifra salía contaminada, se midió el contaminante por separado
   y se descontó en vez de reportar el número bruto.

### ⚠️ Dos artefactos que contaminan cualquier medición (no olvidar)

| Artefacto | Tamaño | Cómo se detecta |
|---|---|---|
| **`EditorTheme.OrigamiTheme` se reconstruía en cada acceso** mientras no hubiera fuente | 12 304 B y 26 µs por lectura de color | Un `BuildNodeList` headless de 1000 nodos asigna 36,5 MB, de los que **23 MB eran reconstrucciones de tema**. En el editor real el mismo rebuild mide **438 KB**: un factor **×83** |
| **Overhead de reflexión del propio arnés** | 12–70 µs y 32 B por `MethodInfo.Invoke` | Los 32 B del "cache hit" son exactamente un `object[1]` (8 de payload + 24 de cabecera). Restado, el coste real es <1 µs |

Corolario: **una medición headless del editor no vale nada sin descontar el tema**, y una medición por
reflexión no vale nada sin descontar el `Invoke`. Las dos correcciones están ya en producción
(guard de `EditorTheme`, cache del árbol).

---

## 2. Lo que se arregló

### P0-H — `HierarchyPanel` reconstruía el árbol entero cada frame

`BuildNodeList` se llamaba en `OnGUI` sin flag de dirty: 2 listas + un `TreeNode` + **una cadena GUID
por nodo y por frame**. Era el mayor asignador del panel.

**Cifras reales (editor, 181 frames de calentamiento):**

| N | Antes (headless) | Antes (editor real) | Después, frame en reposo |
|---|---|---|---|
| 100 | 9 166 KB | 44,9 KB | **~0** |
| 500 | — | 219,7 KB | **~0** |
| 1000 | **36 553 KB** | 438,5 KB | **~0** |

La clave de diseño: **no hay dirty flag**. `Scene.Version` cubre add/remove/attach/detach/reorden de
raíces, pero **no** reparent, rename ni `EnabledInHierarchy` — y hay ~15 `SetParent` en el panel, más
scripts, inspector y undo. Un flag a mano en 20 sitios es una bomba de relojería. En vez de eso, la cache
**se autovalida**: si parece fresca, recorre las filas y compara padre (referencia), nombre (cadena) y
`EnabledInHierarchy` — **cero asignaciones**, y detecta cambios de cualquier fuente. Verificado: reparent,
rename, disable y add, los cuatro.

### P0-A — `CollectRenderables` repartía dos listas nuevas por frame y cámara

| Renderables | Antes | Después |
|---|---|---|
| 100 | 2 280 B/call | **0 B** |
| 1 000 | **16 688 B/call** | **0 B** |
| 4 000 | 65 888 B/call | **0 B** |

1 MB/s por cámara a 60 fps. El coste era el **rearreglo del array** de `List<IRenderable>` al crecer
desde vacío (Σ de cada duplicado de 4 a 1024), no las listas en sí: 64 B con la escena vacía.
También mejoró el tiempo: −15% a 1000 renderables.

### Paso 5 — guard de `EditorTheme.OrigamiTheme`

| | Antes | Después |
|---|---|---|
| 1000 lecturas de color (headless) | 26 ms, 12 304 B/lectura | **0,02 ms, 0 B** |
| 1000 lecturas (**con fuente real**) | — | **0,008 ms, 0 B** |

La hipótesis original de Fase 5A —"es un cliff si un camino futuro no tiene fuente"— **quedó
**falsada** por la medición real: con fuente, el guard acierta siempre y no hay reconstrucción. El
problema era exclusivamente headless. El guard no compra rendimiento en producción: **compra medición
limpia**, que es lo que permitió cerrar el paso 3.

---

## 3. Follow-ups abiertos

### 🔶 El arrastre del árbol sigue reconstruyendo (P0-H no lo cubre)

`EnsureTreeCache` fuerza reconstrucción mientras hay un drag en curso, porque el `DropIndicator` de cada
fila es estado del frame. El resultado es que **reposo es gratis y arrastre no**:

| N | Frame en reposo | **Frame arrastrando** |
|---|---|---|
| 100 | ~0 | 0,146 ms |
| 500 | ~0 | **0,495 ms** + 219,7 KB |
| 1000 | ~0 | **0,968 ms** + 438,5 KB |

Arrastrando 1000 objetos son ~1 ms/frame extra y ~26 MB/s de basura. **No es una regresión** (el código de
reconstrucción es el mismo de antes), pero es el caso que más se nota con 500+ objetos.

**Trabajo propuesto, para Fase 6 al refactorizar `HierarchyPanel`:** durante un drag, la lista de nodos
sigue siendo válida salvo por el `DropIndicator`. Se podría cachear la estructura y refrescar **sólo** los
indicadores de las filas, con lo que el arrastre pagaría O(nodo bajo el puntero) en vez de O(nodo).

### 🔶 `CollectRenderables` fuera del editor

`CollectRenderablesInto` es el camino por frame; el `CollectRenderables` estático sigue reservando dos
listas y sólo lo usan los tests hoy. Si aparece un llamador nuevo, que use el `Into`.

### 🔶 `EnsureWorldBounds` cachea por lista, no por frame (H-RD-45)

Indexado en `(ReferenceEquals(lista), count)`. Una lista de larga vida cuyo *contenido* cambie manteniendo
el count sirve AABBs obsoletos. No se ha medido (no aparece en el perfil) pero el invariante es frágil.

---

## 4. Lo que se descartó, y por qué

Cinco hipótesis resultaron no ser problemas. Los detalles y las medidas están en
`AUDIT_H_ED.md` §"Descartados como problema de rendimiento" (H-ED-3, H-ED-10, H-ED-23) y
`AUDIT_RD.md` §idem (H-RD-11, H-RD-50).

Resumen: **H-ED-23 no era un leak** (acotado por nodos distintos, se reinicia al reabrir el panel) y
**H-ED-3/H-ED-10 son bugs de corrección con 2 KB y 1,6 KB de memoria**, irrelevantes como rendimiento.
**H-RD-11 está acotado a ≤8 MB** por el tope del pool. **H-RD-50 sólo afecta a un profiler in-game que no
existe.**

---

## 5. Hotspot nuevo detectado durante la medición

**El mayor de todos no estaba en la lista de hipótesis**: la reconstrucción por frame del árbol
(P0-H, arriba). Se encontró al medir, no al leer el código — leerlo no lo delata, porque la línea
`BuildNodeList(root, 0, treeNodes, flatObjects)` dentro de `OnGUI` parece normal.

Corolario de método: **la lista de candidatos seńc debate antes de medir, no después**. Dos de las ocho
hipótesis iniciales resultaron mal diagnósticos (no mal bug: mal *sitio*): el O(n²) del filtrado era
irrelevante en escenas reales (plano es O(N) constante: 190 ns/nodo a cualquier N), y el coste de
`CollectRenderables` no eran las dos listas sino el crecimiento del array.

---

## 6. Estado

| Ítem | Estado |
|---|---|
| P0-H (árbol por frame) | **Arreglado** — verificado visualmente por el usuario |
| P0-A (`CollectRenderables`) | **Arreglado** |
| Guard de `EditorTheme` | **Arreglado** |
| Arrastre del árbol | **Abierto** — follow-up de Fase 6 |
| Búsqueda O(n²) al filtrar | **Descartado** — 0,35 ms a N=1000, y sólo con anidamiento profundo |
| Historial, Selection, `_expandState` | **Descartados** — bugs de corrección, no de rendimiento |
| `_stream` sin recortar | **Descartado** — acotado por el pool |
| `RenderStats` sin `BeginFrame` | **Descartado** — sólo afecta a un profiler in-game |