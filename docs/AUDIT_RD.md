# Auditoría RD — pipeline de render

Inventario de los **51** candidatos a bug detectados en la Fase 3.3 (diagnóstico de testabilidad de
`CommandBuffer`, `CommandExecutor` y `RenderPipeline`) el 2026-10.

**Estado: documento vivo.** Fase 6 los triagea. Nada de lo que hay aquí está arreglado: la restricción de
la fase era no tocar producción, así que ningún `KNOWN ISSUE` en los tests describe un fallo corregido,
solo el comportamiento que existe hoy.

## Cómo se numeró

Rangos contiguos por fichero, en el orden en que se analizaron (convención heredada de
`AUDIT_H_ED.md`):

| Rango | Fichero | Bugs |
|---|---|---|
| H-RD-1 … H-RD-19 | `Zenith.Runtime/Graphics/Commands/CommandBuffer.cs` | 19 |
| H-RD-20 … H-RD-39 | `Zenith.Runtime/Graphics/Commands/CommandExecutor.cs` | 20 |
| H-RD-40 … H-RD-51 | `Zenith.Runtime/Rendering/RenderPipeline.cs` (+ 1 en `Camera.cs`) | 12 |

## ⚠️ H-RD-51 — bug arquitectural, no solo de tests

`Camera.UpdateRenderData` (`Camera.cs:283-284`) dereferencia `Window.InternalWindow.FramebufferSize`
siempre que la cámara no tenga un `Target` válido, y `Window.InternalWindow` (`Window.cs:16`) es `null`
hasta que se crea una ventana. Como es la **primera sentencia** del frame
(`DefaultRenderPipeline.cs:182`), un proceso headless o un **servidor dedicado** revienta con
`NullReferenceException` en cuanto se intenta renderizar. El run loop headless actual esquiva el problema
saltándose el render entero (`Game.cs:310`), y los tests no llegan a esa línea porque
`CommandBuffer.Blit(source, null)` tropieza antes (`CommandBuffer.cs:583`).

**No es un problema de testabilidad: es un bug arquitectónico. Impide el uso headless y los servidores
dedicados. Prioridad alta para Fase 6.**

## Prefacio: `Graphics.IsHeadless` no se puede asignar

`Graphics.cs:38` lo declara como propiedad **computada y read-only**:

```csharp
public static bool IsHeadless => GL == null;
```

No hay setter. Un test no la "activa": la obtiene porque `Graphics.GL` (`Graphics.cs:30`) sigue `null`
mientras nadie llame a `Graphics.Initialize()`. Ya está pineado en `HeadlessGraphicsTests.cs:18-21`.
Es una **precondición**, no un mecanismo.

Y el ejecutor **no se salta el trabajo**: `CommandExecutor.cs` tiene **0 guards** de `IsHeadless` y **34
dereferencias** de `Graphics.GL.*`; el único `try/catch` está en `:956` y `:962` (muestras de depuración).
Llamar a `Execute()` en headless tira `NullReferenceException` en el primer opcode de GPU. No es un
fallo en vivo porque `Graphics.Submit` **sí** corta en headless (`Graphics.cs:89-94`) y `Execute` solo se
invoca desde el hilo de render (`Graphics.cs:270`), que no arranca sin ventana. Es un **bloqueante de
testabilidad** (ver H-RD-20).

## Los 5 que están pinados en un test

| ID | Test | Qué fija |
|---|---|---|
| H-RD-1 | `CommandBufferTests.Blit_ClearColorOnly_StillEncodesTheStencilBit` (3.3a) | `Blit` borra el stencil del destino sin que se le pidiera |
| H-RD-4 | `CommandBufferTests.SetGlobalMatrices_CallerMutatesTheArrayAfterwards_AndTheStreamSeesIt` (3.3a) | La matriz se graba por referencia, sin snapshot |
| H-RD-7 | `CommandBufferTests.DrawArrays_IsNotCountedInRenderStats` (3.3a) | Los draws sin índices no aparecen en el profiler |
| H-RD-40 | `RenderPipelineTests.DrawRenderables_InstancedFallback_RecordsTrianglesWhateverTheMeshSays` (3.3b) | El fallback instanciado fuerza `Topology.Triangles` |
| H-RD-43 | `RenderPipelineTests.SetupGlobalUniforms_WithNoSmoothDeltaTime_UploadsAnInfiniteReciprocal` (3.3b) | `1/0` sube `+Infinity` al UBO de uniforms globales |

Pendiente de pinar: **H-RD-20** (bloque de 3.3c).

Los otros 45 quedan **sólo en este inventario** (sin comentario en código).

## CommandBuffer

| ID | file:line | Descripción | Conf. |
|---|---|---|---|
| H-RD-1 | 592 | `Blit` ORea `ClearFlags.Stencil` incondicionalmente y la API **no tiene parámetro `clearStencil`** ⇒ `clearColor: true` también borra el stencil del destino | **alta** |
| H-RD-2 | 842-844 | El guard de encode tardío comprueba `_inPool`, que es `false` durante toda la ventana `Submit`→hilo de render ⇒ **codificar tras `Submit` no lanza**, y el hilo de render lee `_streamPos`/`_stream` a la vez (lectura rota / desincronía del decodificador) | **alta** |
| H-RD-3 | 58, 60-62, 94 | `_submitted` es estado muerto: se escribe (`:78`, `:94`, `Graphics.cs:95,116`) y no se lee nunca. Su doc ("cleared only by OnRent, NOT by OnReturn") lo contradice; ese comportamiento lo implementa `_ownerReleased` | media |
| H-RD-4 | 299-304 | `SetGlobalMatrices` hace `PushObject(values)`: **la referencia viva del array del caller**, rompiendo la promesa de snapshot de la propia clase (`:33-35`, `:202-204`). El executor copia en tiempo de ejecución, quizá frames después | **media** |
| H-RD-5 | 527 | `DrawMesh` dereferencia `Material.Shader` nullable (`:511-512` sólo protegen `mesh` y `material`) ⇒ NRE desde un método de grabación | media |
| H-RD-6 | 540-548 | `hasModel = !model.Equals(default)` hace que una matriz **explícitamente cero** sea indistinguible del argumento opcional `default` ⇒ no se emite nada y el `prowl_ObjectToWorld` del draw anterior persiste (la API no puede expresar "colapsar a un punto") | media |
| H-RD-7 | 491-498 | `DrawArrays` es el único encoder de draw que **no** llama a `RenderStats.RecordDraw` (los otros dos sí, `:471` y `:488`) ⇒ overlays, partículas y wireframes son invisibles en el profiler | media |
| H-RD-8 | 845-846 vs 878-893 | La codificación **no es atómica**: `WriteHeader` avanza `_streamPos` y confirma el opcode *antes* de que `PushObject`/`InternName` puedan lanzar. Un throw capturado deja un opcode huérfano sin payload, y el ejecutor descompasa todo lo posterior. Alcanzable desde **19** métodos | media |
| H-RD-9 | 90-92 + `CommandExecutor.cs:53-54` | `OnReturn` devuelve los snapshots al pool (`ps.Clear()`), pero el executor singleton sigue apuntando a ellos ⇒ el siguiente CB aplica un bloque de propiedades **vacío**. Latente: hoy cada ruta de draw rebinda en el mismo CB | media |
| H-RD-10 | 515 | `DrawMesh` hace `return` silencioso si `VertexArrayObject == null || VertexCount <= 0`, sin log, mientras `Mesh.Upload()` sí avisa (`Mesh.cs:526-534`) | media-baja |
| H-RD-11 | 868-876 + 84-95 | `EnsureCapacity` dobla `_stream` pero **nunca lo recorta**; `OnReturn` sólo reinicia `_streamPos`. Una subida grande ancla ese array para siempre y el pool retiene hasta 64 buffers. `TransientStore.cs:86-98` lo hace bien: los dos subsistemas son inconsistentes | baja |
| H-RD-12 | 551-559 | `subMeshIndex = -1` significa "malla entera", pero `subMeshIndex = 99` (índice obsoleto) cae en el mismo `else` ⇒ draw de malla completa en vez de un error. `DrawMesh` y `RenderPipeline.cs:733-741` comparten la forma del `if/else` | baja |
| H-RD-13 | 391-410 | Tres sobrecargas de `SetTexture`, mismo opcode, tres semánticas de null/disposed: `Texture2D`/`Texture3D` convierten un disposed en `null`; `GraphicsTexture` guarda la textura **disposed** verbatim ⇒ `glBindTexture` sobre un nombre borrado | baja |
| H-RD-14 | 581-585 | `Blit(..., destination: null)` dereferencia `Window.InternalWindow` (`:583`), `null` en headless/servidor ⇒ NRE en un "post-process a pantalla" ordinario. Contradice el contrato headless del propio fichero (`:17`, `:634-635`) y el guard explícito de `Graphics.Screenshot` (`Graphics.cs:447-450`). Las otras tres sobrecargas **sí** son headless-safe | media |
| H-RD-15 | 627-631 y 832-835 | `BeginSample`/`EndSample` no llevan contador de profundidad ni validación de balance; un `EndSample` sin `BeginSample` saca un grupo que nunca se empujó. Además `EndSample` (`:832`) vive ~200 líneas lejos de `BeginSample` (`:627`) | baja |
| H-RD-16 | 98-106 | `OnDestroy` libera `_stream` y `_store` pero **no limpia** `_objects`, `_nameMap` ni `_rentedSnapshots` ⇒ el buffer sigue referenciando texturas/buffers/FB/programas. Alcanzable porque `Graphics.Submit` pone `_ownerReleased` y hace que el `Dispose` del usuario sea no-op | baja |
| H-RD-17 | 47, 887-893 | `InternName` memoiza nombres de 21 métodos sin tope por CB; nombres generados dinámicamente (`$"camera{i}.pos"`, UBOs por luz) crecen hasta el tope global de 65 536 y entonces lanza un error engañoso desde el bucle de render | baja |
| H-RD-18 | 595, 605, 615, 622 + `Mesh.cs:961-982` | `Mesh.GetFullscreenQuad()` cachea **una** instancia estática de por vida del proceso y las cuatro sobrecargas de `Blit` la pasan a `DrawMesh` → `Upload()` → `EnsureNotDisposed()` **sin guard `IsValid()`**, al contrario que `DefaultRenderPipeline.cs:92`. Si algo la destruye, **todo `cmd.Blit` posterior lanza `ObjectDisposedException`** | media |
| H-RD-19 | 518-529 | Ocho `material.SetKeyword(...)` escriben en el `Material` compartido del caller **sin restaurar en ninguna salida**: el `return` de `:529` (variante no encontrada) es posterior a las mutaciones. El material queda con los keywords del último mesh ⇒ un `Blit` posterior puede elegir una **variante de shader equivocada** | baja |

## CommandExecutor

| ID | file:line | Descripción | Conf. |
|---|---|---|---|
| H-RD-20 | 78-85 (y las 34 llamadas a `Graphics.GL.*`) | `Execute` **no** tiene guard `IsHeadless`/`GL == null` ni `try`. En headless lanza `NullReferenceException` en el primer opcode de GPU, abortando el resto del CB **después** de haber committeado todas las mutaciones CPU previas (globales, properties pegajosos, cola de texturas). No hay forma de distinguir "omitido" de "aplicado a medias" | **alta** |
| H-RD-21 | 784-785 (orig en `:49`, `:50`) | `DoClear` restaura las máscaras de profundidad y stencil desde `_raster` **sin comprobar `_rasterInitialized`**. Antes del primer `SetRasterState`/draw, `_raster` es `default(RasterizerState)` ⇒ el **primer clear del proceso** pisa `glDepthMask(false)` y `glStencilMask(0)`, exactamente el fallo que el código circundante (`:774-776`) evita | **alta** |
| H-RD-22 | 890-894 (orig en `:49`, `Primitives.cs:74-94`) | El comentario dice "push GL defaults" pero empuja `default(RasterizerState)` —el struct todo-cero—, **no** los initializers declarados: `new RasterizerState() != default(RasterizerState)` (ctor explícito en `Primitives.cs:96-99`). El primer draw corre sin depth test, sin depth write, sin culling y sin stencil | **alta** |
| H-RD-23 | 74, 356-365, 896, 934 | `_pendingDirectTextures` se limpia sólo en `SetShader` (`:173`) y al final de `PrepareDraw` (`:934`). `PrepareDraw` retorna pronto en `:896` si no hay programa, así que un `SetUniformTexture` del CB *n* sin draw sigue en cola y se vacía contra el programa que se enlace en el **primer draw del CB n+1** ⇒ se enlaza la textura de un shader al sampler de otro | media |
| H-RD-24 | 78-709 | Sin `try/finally` ni rollback: un throw a mitad deja los 11 campos del ejecutor a medias **de por vida del proceso** (`Graphics.cs:43` lo hace `static readonly`). `Graphics.cs:270-282` captura y devuelve el buffer al pool pero **no reinicia los espejos** ⇒ un CB malo envenena el renderer hasta reiniciar | **alta** |
| H-RD-25 | 866-869 (vs `:846-849`) | `DoDrawIndexedInstanced` **descarta `baseVertex`** (`_ = baseVertex;` en `:867`) mientras el hermano no-instanciado sí lo honra. Un draw instanciado con `baseVertex != 0` lee índices desplazados 0 ⇒ geometría errónea o fuera de rango, sin error ni log | **alta** |
| H-RD-26 | 747-752; 687-688; `GraphicsProgram.cs:170-178` | `BindProgram(null)` llama a `GL.UseProgram(0)` pero nunca limpia el estático global `GraphicsProgram.currentProgram`, así que ese estático sigue nombrando un programa que **no** está enlazado ⇒ `Graphics.CurrentProgram` miente. `DisposeShader` usa ese mismo estático obsoleto para decidir, así que la reconciliación puede fallar en ambos sentidos | media |
| H-RD-27 | 78-85 (vs `Graphics.cs:85-86`) | `Execute` no tiene guard de idempotencia: no comprueba `_submitted`, `_inPool` ni `_ownerReleased`. Ejecutar dos veces duplica la cola `_pendingDirectTextures` y **vuelve a correr `CreateBuffer`/`CreateTexture`**, fugando el nombre GL anterior | media |
| H-RD-28 | 939, 943, 947 | `AllocateTextureSlot` es un `++` sin cota. Nada en el fichero ni en las constantes de capacidad de `Graphics` (`Graphics.cs:25-28`, que son de *tamaño*, no de *unidades*) limita contra `GL_MAX_TEXTURE_IMAGE_UNITS` ⇒ un material con más texturas que unidades camina `ActiveTexture` fuera de rango y el shader lee la unidad equivocada | media |
| H-RD-29 | 970, 979, 982, 985, 988, 993 | **Ninguno** de los readers de ancho fijo hace bounds-check. `Slice` sólo lanza si `pos > Length`, así que con `pos == Length-1` el `MemoryMarshal.Read<T>` (unchecked) lee **fuera del span**. Como `_stream` viene de `ArrayPool` (mayor que `_streamPos`), la sobrelectura decodifica basura silenciosamente: un stream truncado es indetectable | **alta** |
| H-RD-30 | 80 (vs `CommandBuffer.cs:100-104`) | `Execute` dereferencia `cmd._stream` sin comprobar null. `OnDestroy()` lo pone a `null!` (`:103`) y el pool lo llama al descartar buffers por encima del tope (`CommandBufferPool.cs:42`) ⇒ NRE. Simétricamente, `_streamPos > _stream.Length` hace que `AsSpan(0, _streamPos)` lance `ArgumentOutOfRangeException` | media |
| H-RD-31 | 47; `GraphicsBuffer.cs:94` | La invalidación del espejo de VAO pasa por el **singleton global**: `GraphicsBuffer.Bind()` llama a `Graphics.Executor.InvalidateBoundVAO()`, no al ejecutor que está haciendo el bind. Correcto en producción (hay un solo ejecutor) pero hace el espejo **incorregible** desde cualquier otra instancia y, por tanto, **no testeable**: el `new CommandExecutor()` de un test no se puede invalidar por la vía documentada | **alta** |
| H-RD-32 | 743-744 (comentario `:740-742`) | `ApplyRenderTarget` reemite `GL.Viewport(0,0,w,h)` en **cada** llamada con `draw != null`, incluso cuando el bind del FBO se saltó por redundante ⇒ contradice la invariante propia del ejecutor ("los binds redundantes se saltan") y su propio comentario: `SetViewport` seguido de `SetRenderTarget(mismoFb)` reinicia el viewport en silencio | media |
| H-RD-33 | 813-816 (vs `:436`) | Cuando `DoUpdateBuffer` reasigna llama a `buf.Set(size, p, dynamic: true)` con **`true` hardcodeado**, ignorando el flag con el que se creó el buffer ⇒ un buffer `STATIC_DRAW` se reasigna como `DYNAMIC_DRAW` en la primera actualización completa. El bit no se guarda en `GraphicsBuffer`, así que no se puede recuperar | media |
| H-RD-34 | 507 (vs `CommandBuffer.cs:680-681`) | `AllocateTextureCubeFace` calcula `TextureCubeMapPositiveX + face` **sin rango**; el encoder documenta 0..5 pero no valida nada. `face == 6` aliasa la cara 5 y más allá se sale del enum, sin diagnóstico | media |
| H-RD-35 | 438, 465, 636, 657 | Los cuatro `Create*` regeneran el handle GL sin comprobar si el objeto ya tenía uno ⇒ un `Create*` duplicado fuga el nombre anterior (los `Dispose*` sí guardan por `Handle != 0`, así que nunca se libera). Aparte, `CreateVertexArrayOp`/`CreateFramebufferOp` ponen los espejos a 0 asumiendo que `CreateGLObject` deja GL en 0, invariante duplicada en un segundo sitio sin aserción | media |
| H-RD-36 | 568-573 (vs `CommandBuffer.cs:730`) | El `switch` del eje en `SetTextureWrap` tiene casos 0/1/2 y **no tiene `default`**; el encoder toma un `byte axis` arbitrario sin validar ⇒ `axis >= 3` es un no-op silencioso | baja |
| H-RD-37 | 764-766 vs 772 | `DoClear` empuja `ClearColor`/`ClearDepth`/`ClearStencil` **antes** del `if (mask == 0) return;` ⇒ `ClearRenderTarget((ClearFlags)0, …)` (legal, `ClearFlags` no tiene miembro `None`) muta los valores de clear sin limpiar nada | baja |
| H-RD-38 | 788-802 | `DoBlit` no valida nada antes de `GL.BlitFramebuffer` (`:801`): `mask == 0` produce `GL_INVALID_ENUM` y no se comprueba que los framebuffers de lectura y escritura difieran (blittear un FBO sobre sí mismo es indefinido en GL, y es justo lo que `ApplyRenderTarget` (`:717`) admite por diseño) | baja |
| H-RD-39 | 1007-1017; `Primitives.cs:48` | `ToGL` no tiene caso para `Topology.Quads` (7), así que cae en `_ => Triangles`: una malla de quads se rasteriza como una lista de triángulos sin diagnóstico. **Latente**: `Quads` tiene cero referencias fuera del enum | baja |

## RenderPipeline

| ID | file:line | Descripción | Conf. |
|---|---|---|---|
| H-RD-40 | 842 (vs `:740`, `:838`) | El fallback del draw instanciado **fuerza `Topology.Triangles`** en vez de `mesh.MeshTopology`: el no-instanciado usa `mesh.MeshTopology` y el sub-mesh usa `sub.Topology`. Una malla de líneas o strips con `GetSubMeshIndex() == -1` se rasteriza como triángulos | **alta** |
| H-RD-41 | 611-614 | Los batches sólo se reordenan `if (hasSortOffsets)`, y ningún shader built-in declara offset de orden (verificado: cero `"Tag" = "Value+N"` en `Assets/Defaults/*.shader`). En la práctica el pase transparente conserva el **orden de descubrimiento**: `SortRenderables` sólo ordena objetos *dentro* de un batch, así que el back-to-front entre materiales no se respeta | media |
| H-RD-42 | 613 | `batches.Sort((a,b) => a.SortKey.CompareTo(b.SortKey))` es introsort **inestable** y los empates son la norma (todo batch de pase 0), así que con `hasSortOffsets` activo el orden relativo puede cambiar entre frames ⇒ parpadeo de alpha en transparentes | media |
| H-RD-43 | 423 | `1.0f / Time.DeltaTime` y `1.0f / Time.SmoothDeltaTime` sin guarda: `DeltaTime` es 0 si `TimeData.Update()` nunca corrió (servidor, primer frame, frame pausado y **todo test con `RuntimeTestBase`**, que empuja `SmoothDeltaTime = 0`) ⇒ se sube **`+Infinity`** al UBO de uniforms globales | **media** |
| H-RD-44 | 158-185 (en especial `:175-177`, `:184`) | `CameraSnapshot` produce un snapshot degenerado si `Camera.UpdateRenderData()` no ha corrido: `PixelWidth = PixelHeight = 0`, `Aspect = 0` (→ `1 + 1/0 = Infinity` en `:417`) y un `WorldFrustum` construido de `ViewMatrix × ProjectionMatrix` **a cero**, que descarta todo. La precondición no está documentada ni se impone | media |
| H-RD-45 | 321, 332, 346-347 | `EnsureWorldBounds` cachea por frame indexado **sólo** en `(ReferenceEquals(list), count)` y nunca se invalida: cualquier lista de larga vida cuyo *contenido* cambie manteniendo el count sirve AABBs y flags obsoletos para todos los culls posteriores. El comentario `:325-328` afirma que "los renderables no se mueven entre la recolección y el dibujo", que nada impone | media |
| H-RD-46 | 638-646, 799-808, 813, 845 | `material.SetKeyword(...)` muta el `Material` compartido y `GPU_INSTANCING` se limpia sólo en las dos salidas normales (`:813`, `:845`): cualquier excepción entre `:808` y `:845` lo deja activo en un material que otros sistemas reutilizarán ⇒ variante equivocada | media |
| H-RD-47 | 282-287 | `CullRenderable` (singular) tiene **cero call sites** en todo el repo y duplica el predicado de `:266-269` **omitiendo la máscara de capas y la tolerancia a frustum nulo**: dos copias de la regla de culling que pueden divergir, y la copia sin usar es la obsoleta | **alta** (hecho) / impacto bajo |
| H-RD-48 | 516 y 710 | `IRenderable.GetRenderingData` se invoca **dos veces por renderable y pase** (una como sonda de instancing en la fase 1, otra en la fase 3), hasta 6 por objeto y cámara y frame más 2 por cascada. `UIRenderItem` lo tapa con un guard de `PropertyCacheState` (`UIRenderItem.cs:80-90`); un renderable propio que reserve memoria lo paga cada vez | **alta** (hecho) / perf |
| H-RD-49 | 187 + 214, 224 | `ActiveObjectIds` es una propiedad pública **settable** cuyo valor el pipeline muta después (`Add` en `:224`, `Clear` en `:214`): el llamante puede inyectar un `HashSet<int>` que también posee y el pipeline lo borrará en silencio. Los dos campos que sí definen el comportamiento (`s_prevModelMatrices`, `s_framesSinceLastCleanup`) no tienen accessor, y `CLEANUP_INTERVAL_FRAMES` es `private` ⇒ el cleanup no es testeable sin reflexión | media (API/testabilidad) |
| H-RD-50 | 277, 617 → `RenderStats.cs:82, 89` | **Nadie en `Zenith.Runtime` llama a `RenderStats.BeginFrame()`/`EndFrame()`**: los únicos call sites están en `GameViewPanel.cs:145,159`. En un player standalone los contadores se acumulan sin límite y `RenderStats.Last` queda `default` para siempre, contradiciendo los docs de `RenderStats.cs:7-12,81,88` | **alta** |
| H-RD-51 | `Camera.cs:283-284` | `UpdateRenderData` dereferencia `Window.InternalWindow` sin cámara con `Target` válido. **Bug arquitectural — ver la nota destacada al principio de este documento** | **alta** |

## Sospechas débiles: NO se presentan como bugs

Se llegaron a mirar y se descartan, para que nadie las vuelva a investigar:

| Dónde | Por qué no llega |
|---|---|
| `RenderPipeline.cs:490` | `needsWorldToObject = tagValue != "ShadowCaster"` optimisea por el *string pedido* en vez de por el pase resuelto. Inofensivo hoy (las tres luces pasan `("LightMode","ShadowCaster")`), pero un caller con `("RenderOrder","ShadowCaster")` pierde la inversa silenciosamente |
| `RenderPipeline.cs:485` | `hasRenderOrder = !string.IsNullOrWhiteSpace(shaderTag)`: pasar `""` desactiva el filtro y dibuja **todos** los pases del material sin avisar. Ningún caller del árbol lo hace ⇒ mina, no defecto vivo |
| `RenderPipeline.cs:712`, `:717` | `properties.GetInt("_ObjectID")` y `cmd.SetInstanceProperties(properties)` no tienen guard de null sobre el `PropertyState` que devuelve un `IRenderable`. Todas las implementaciones del árbol devuelven no-null |
| `RenderPipeline.cs:269` + `LayerMask.cs:34` | `HasLayer` hace `1u << index`, un shift módulo 32: un `GetLayer()` de `-1` o `>= 32` se aliasa a otra capa en vez de rechazarse |
| `RenderPipeline.cs:621` + `:308` | `foreach (RenderBatch batch in batches)` itera la lista compartida mientras `DrawInstancedRenderablePass` ejecuta código de usuario. Seguro hoy (no hay `DrawRenderables` reentrante), pero el comentario `:311` afirma "nunca reentrante" sin guard |
| `RenderPipeline.cs:664-692` | El RT temporal del grab pass se libera tras *codificar* los draws pero antes de que el caller envíe el CB; la seguridad depende de la invariante de orden FIFO documentada en `RenderTexture.cs:227-255`. Correcto, pero la restricción es invisible en el punto de llamada |
| `CommandExecutor.cs:956`, `:962` | `DoBeginSample`/`DoEndSample` tragan excepciones. Deliberado (mismo patrón en `Graphics.cs:214`) e inofensivo, aunque es la **única** razón por la que esos dos opcodes sobreviven en headless |

## Bugs deliberadamente NO reclamados (comprobados y encontrados limpios)

- `CommandBuffer.SetTexture(string, Texture2D)` (`:395`) y `EncodeScreenshot` (`Graphics.cs:475`) pasan un `uint` donde el ejecutor castea `(GraphicsTexture?)`: **no es bug**, `Texture2D.Handle` es `public GraphicsTexture Handle` (`Texture.cs:19`).
- Orden de escritura de `_raster` en `SetRasterState` (`:161-163`): aplicar-después-de-grabar es correcto para la restauración de `DoClear`.
- Contabilidad de espejos de FBO en `ApplyRenderTarget` (`:717-738`): la rama `ReferenceEquals` usa `Framebuffer` para ambas y actualiza ambos espejos.
- Invalidación de espejos en `DisposeVertexArray` (`:648`) y `DisposeFramebuffer` (`:669-670`): correcto, GL desenlaza implícitamente el nombre borrado.
- El salto de `BindVAO` (`:757`): sólido, dado que lo mantienen `CreateVertexArrayOp` (`:639`), `DisposeVertexArray` (`:648`) y `GraphicsBuffer.Bind`.
- `ReadBlob` (`:999-1003`): **sí** hace bounds-check, vía `TransientStore.Read` (`TransientStore.cs:77`).
- El límite de `PushObject` (`:881-882`) está bien colocado: permite el índice 65535, rechaza el 65536 y lanza **antes** del `Add`, así que no hay entrada parcial.

## Comportamientos confirmados que no son bugs (documentados al escribir los tests)

| Comportamiento | Dónde |
|---|---|
| Los setters globales **no** tocan `PropertyState.s_global*` al codificar: sólo se mutan en tiempo de **ejecución** (`CommandExecutor.cs:210-297`) | Codificar un setter global es trabajo CPU inerte |
| `Submit` en headless **recicla el buffer de forma síncrona** (`_streamPos = 0`, objetos limpiados) ⇒ hay que inspeccionar `_stream` **antes** de enviarlo | `Graphics.cs:89-94` + `CommandBuffer.cs:84-95` |
| El `DrawArrays` no cuenta en stats **y** tampoco es deduplicado; ningún encoder deduplica nada: el dedup vive en el ejecutor | `CommandBuffer.cs:133`, `CommandExecutor.cs:41-43` |
| `GraphicsVertexArray`/`GraphicsProgram`/`GraphicsBuffer`/`GraphicsFrameBuffer` sí asignan su wrapper gestionado headless (sólo el `Handle` queda 0), así que el guard `mesh.VertexArrayObject == null` **no** cortocircuita en tests | `GraphicsVertexArray.cs:25-42`, `GraphicsProgram.cs:57-70` |
| `PropertyState.ClearGlobals()` es un **no-op en headless**: renta un CB y lo manda a `Submit`, que lo dropea. Para teardown hay que llamar al `internal` `ClearGlobalsInternal()` | `PropertyState.cs:367` vs `:336` |
| `LayerMask` guarda bits de **exclusión**, pero `FromMask` recibe bits de **inclusión** y los invierte | `LayerMask.cs:22-34` |
| `EnsureWorldBounds` cachea por frame indexado sólo en `(ReferenceEquals(lista), count)`: una segunda pasada sobre la misma lista no pregunta nada | `RenderPipeline.cs:332` |
| `SortRenderables` devuelve su `_sortResult` **reutilizado**, no una lista nueva: dos sorts consecutivos devuelven el mismo objeto | `RenderPipeline.cs:309,395` |
| El primer pase del shader por defecto (`Standard.shader`) lleva `Tags { "RenderOrder" = "Opaque" }`, así que `DrawRenderables(..., "RenderOrder", "Opaque", ...)` —la forma que usa el pipeline— casa exactamente un pase | `Standard.shader:42` |
| `Time.DeltaTime` sale de `TimeData.DeltaTime` sin llamar a `Update()`: con el `TimeData` de `RuntimeTestBase` es 1/60, pero **`SmoothDeltaTime` es 0**, y ése es el que se invierte sin guarda | `Time.cs:55`, `RuntimeTestBase.cs:44` |