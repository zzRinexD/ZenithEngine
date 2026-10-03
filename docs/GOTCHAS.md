# Gotchas

Trampas conocidas del motor y del entorno de desarrollo. Cada una costó al menos un test fallido o una
sesión de debugging. Leer antes de tocar código en el área correspondiente.

Extraído de `TRASPASO.md` (que está en `.gitignore`) para que sea **trazable**: cualquiera que clone el
repo hereda estas trampas. `TRASPASO.md` §3 y su sección de gotchas **sólo referencian este doc**.

---

## Del motor

Ocho trampas. Todas aparecieron midiendo o leyendo el código fuente, no por intuición; la columna "Test que la
descubrió" es la prueba de que cada una sigue viva en la suite (si alguien la arregla en Fase 6, el test
que la referencia es el que hay que actualizar).

| # | Gotcha | Dónde | Por qué | Test que la descubrió | Confianza |
|---|---|---|---|---|---|
| 1 | **`LayerMask.FromMask` toma bits de inclusión y los invierte** — el struct *guarda* exclusiones, el constructor da inclusiones | `LayerMask.cs:22-34` | Pedir "excluir la capa 3" con `FromMask(1u << 3)` la **incluye**. Usar `FromMask(0b111)` para admitir 0-2 | `RenderPipelineTests.CullRenderables_ExcludedLayer_CullsOnlyThatLayer` (`:137`), `CullRenderables_WithoutAFrustum_CullsByLayerMaskOnly` (`:122`) | **Alta** — el propio test lo explica en un comentario (`:119`) |
| 2 | **`ArrayPool.Rent(n)` no devuelve `n` bytes** — devuelve como mínimo el siguiente tamaño de power-of-two, y reutiliza arrays grandes del pool común | `CommandBuffer.cs:69` | Un test que "fuerza el crecimiento" con un tamaño fijo **pasa o falla según el orden de ejecución**. Hay que iterar hasta que el array cambie de identidad | `CommandBufferTests.EnsureCapacity_GrowsTheStream_AndKeepsEarlierCommands` (`:84`), `OnReturn_AfterGrowingTheStream_KeepsTheGrownArray` (`:112`) | **Alta** |
| 3 | **`PropertyState.ClearGlobals()` es no-op en headless** — renta un CB y lo manda a `Submit`, que lo dropea | `PropertyState.cs:367` | Para teardown hay que llamar al `internal ClearGlobalsInternal()` (`:336`) | `CommandExecutorTests.ClearGlobals_ThePublicApiIsANoOpHeadless_ButTheOpcodeClearsEverything` (`:81`); el ctor y `Dispose` de la clase lo usan | **Alta** |
| 4 | **`Graphics.IsHeadless` no se puede asignar** — es `=> GL == null`, read-only | `Graphics.cs:38` | Es una *precondición*, no un mecanismo: se obtiene por no llamar nunca a `Graphics.Initialize()` | `HeadlessGraphicsTests.Graphics_ReportsHeadless_WhenNoDevice` (`:18`); documentado en la cabecera de `CommandBufferTests.cs:21-23` | **Alta** |
| 5 | **`CommandExecutor` no tiene guard headless** — 0 checks de `IsHeadless`, 34 dereferencias de `Graphics.GL` | `CommandExecutor.cs:78-707` | `Execute()` **no se salta el trabajo: revienta** con NRE en el primer opcode de GPU, y aborta el resto del buffer | `CommandExecutorTests.Execute_ACommandThatTouchesTheDevice_ThrowsNullReferenceWithoutAGuard` (`:167`, KNOWN ISSUE en `:160`), `Execute_ADeviceCommandBeforeAGlobal_AbandonsTheRestOfTheBuffer` (`:182`) | **Alta** — hay KNOWN ISSUE abierto |
| 6 | **Los blobs van al `TransientStore`, no al stream** | `CommandBuffer.cs:387` | Un payload grande (matrices, subidas) **no hace crecer** `_stream`; sólo los comandos de payload inline lo hacen | `CommandBufferTests.EnsureCapacity_GrowsTheStream_AndKeepsEarlierCommands` (`:84`), comentario en `:77` | **Alta** — es el mismo test que payó la #2 |
| 7 | **`Stack<T>.ToArray()` es LIFO** (cima primero) | BCL | Las aserciones del historial de carpetas de `ProjectPanel` salen invertidas si se asumen en orden de inserción | `ProjectPanelTests.NavigateTo_PushesTheFolderItCameFromAndClearsTheForwardTrail` (`:340`), comentario en `:338` | **Alta** |
| 8 | **`CommandBuffer` se recicla de forma síncrona al enviarlo en headless** | `Graphics.cs:89-94` | Hay que inspeccionar `_stream` **antes** de `Submit`, o se leen ceros | `CommandBufferTests.Submit_Headless_RecyclesTheBufferSynchronously` (`:279`) | **Alta** |

Notas de método, por si hace falta repetirlas:

- Las #2 y #6 las pagará **el mismo test**. No son dos trampas independientes: son la misma
  (`EnsureCapacity` tiene que crecer lastream) vista desde los dos lados — qué la hace crecer y qué no.
- La #4 no es un bug, es una decisión de diseño correcta que parece un bug porque el nombre
  `IsHeadless` invita a asignarlo.

---

## De herramientas

### ⚠️ Encoding en PowerShell 5.1

`Get-Content` / `Set-Content` / here-strings **no son seguros** para docs con acentos en este repo. Sin
`-Encoding`, `Get-Content` lee UTF-8 como ANSI y `Set-Content` escribe ANSI: rompe el UTF-8.

**Usar siempre** una de estas dos vías:

- A nivel de .NET, sin pasar por strings del host:

  ```powershell
  [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
  [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))   # sin BOM
  ```

  (o concatenar con `[System.IO.File]::ReadAllBytes` + `WriteAllBytes`, que es byte-exacto y lo más seguro)

- Editar con las herramientas `read`/`edit` del agente, que ya escriben UTF-8.

**Síntoma del bug**: `É` (0xC3 0x89) reescrito como `0xE9` suelto → **mezcla de ANSI y UTF-8 en el mismo
archivo**. Los acentos ya escritos antes se conservan, así que el fichero *parece* bien en un visor lenient
y sólo falla al buscar bytes: **no hay aviso visible**.

**Verificación rápida** tras tocar un doc con acentos: buscar bytes `0xE1`–`0xF3` **sueltos** (no seguidos
de un byte `0x80`–`0xBF`), y confirmar que decodifica como UTF-8 estricto:

```powershell
$b=[System.IO.File]::ReadAllBytes($path); $lone=0
for ($i=0;$i -lt $b.Length;$i++){ if($b[$i] -ge 0xE1 -and $b[$i] -le 0xF3){ $n=$b[$i+1]; if(-not ($n -ge 0x80 -and $n -le 0xBF)){$lone++} } }; $lone
```

`0` = correcto. Si sale >0, reescribir el fichero por bytes desde la versión commiteada.

| Dónde | Confianza | Por qué |
|---|---|---|
| `docs/*.md` con acentos | **Alta** | Reproducido en `AUDIT_H_ED.md` y `AUDIT_RD.md` al añadir las secciones de descartes; ambos quedaron con bytes ANSI sueltos y hubo que repararlos por bytes |
| Ficheros de test `.cs` | **Baja** | No observados: se editan con las herramientas del agente, que ya escriben UTF-8. Vigilar si alguna vez se tocan con `Set-Content` |

### ⚠️ `.gitignore` se traga ficheros de test sin avisar

`Inspect*.cs` estaba en `.gitignore` (heredado de Fase 0) y **se tragaba
`Zenith.Editor.Test/InspectorPanelTests.cs`** en silencio: `git status` no lo mostraba y nunca se habría
commiteado. Resuelto en `b9852e70`.

**Lección:** tras crear un fichero de test nuevo, comprobar `git check-ignore -v <ruta>`. Los ficheros ya
commiteados no se ven afectados por `.gitignore`, sólo los nuevos. (`TRASPASO.md` es el caso inverso: está
ignorado a propósito, y por eso este doc existe.)