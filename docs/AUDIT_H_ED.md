# Auditoría H-ED — lógica de los paneles del editor

Inventario de los **43** candidatos a bug detectados en la Fase 3.4 (diagnóstico de testabilidad de los
paneles del editor) el 2026-10.

**Estado: documento vivo.** Fase 6 los triagea. Nada de lo que hay aquí está arreglado: la restricción de
la fase era no tocar producción, así que ningún `KNOWN ISSUE` en los tests describe un fallo corregido,
solo el comportamiento que existe hoy.

## Cómo se numeró

Los tres paneles se analizaron en paralelo y cada informe numeró desde H-ED-1, así que los rangos se
asignaron al final por archivo para que un ID sea estable y no ambiguo:

| Rango | Archivo | Bugs |
|---|---|---|
| H-ED-1 … H-ED-13 | `Zenith.Editor/GUI/Panels/ProjectPanel.cs` | 13 |
| H-ED-14 … H-ED-26 | `Zenith.Editor/GUI/Panels/HierarchyPanel.cs` | 13 |
| H-ED-27 … H-ED-34 | `Zenith.Editor/GUI/Panels/InspectorPanel.cs` | 8 |
| H-ED-35 … H-ED-42 | `Zenith.Editor/GUI/Panels/PreferencesPanel.cs` | 8 |
| H-ED-43 | `ProjectPanel.cs` (hallazgo posterior a los informes) | 1 |

## Los 9 que están pinados en un test

Estos son los que un test fija hoy, con un `// KNOWN ISSUE:` y el ID en el propio test. Al arreglar la
producción hay que actualizar la aserción, no sólo el comentario.

| ID | Test | Qué fija |
|---|---|---|
| H-ED-6 | `ProjectPanelTests.CanAcceptAssetDropInto_ASubAssetDrag_IsAcceptedByAFolderButWouldMoveNothing` | La validación de destino acepta un arrastre de sub-asset y el move no movería nada |
| H-ED-8 | `ProjectPanelTests.ContentEntries_HiddenEntries_DoNotCountTowardsAFoldersEmptiness` | El toggle de ocultos no llega al árbol de carpetas |
| H-ED-11 | `ProjectPanelTests.DisplayName_ADotfileWithExtensionsHidden_IsEmpty` | Nombre vacío para un dotfile con extensiones ocultas |
| H-ED-43 | `ProjectPanelTests.FormatSize_OneDecimalDigits_FollowTheCurrentCulture` | Separador decimal dependiente de la cultura |
| H-ED-16 | `HierarchyPanelTests.BuildNodeList_SearchKeepsAncestorsOfMatches_ButDoesNotOpenThem` | La búsqueda conserva el ancestro en el modelo pero no fuerza su expansión |
| H-ED-18 | `HierarchyPanelTests.ProcessGODropCore_DroppingAsFirstChild_ReversesTheOrderOfThePayload` | Soltar "como primer hijo" invierte el orden del payload |
| H-ED-10 | (ancla en 3.4a) `ProjectPanelTests.ContentItem_Identity_IsGuidPlusRelativePath` | Identidad = GUID + ruta |
| H-ED-29 | (pendiente 3.4c) `InspectorPanelTests` | Filtro `.meta` sensible a cultura y mayúsculas |
| H-ED-1 | (pendiente 3.4c) `ProjectPanelTests` | Renombrar una carpeta no rebasa la carpeta actual cuando es un descendiente |

### Comportamientos del panel que los tests dejaron fijados (no son bugs)

Al escribir las aserciones aparecieron cuatro semánticas que no estaban documentadas en el código y que
conviene tener escritas, porque son contrarias a la lectura intuitiva:

| Comportamiento | Dónde | Nota |
|---|---|---|
| Un nodo oculto no aporta ni siquiera su propia fila: el walk retorna antes de agregarlo, así que se va su subárbol entero | `HierarchyPanel.cs:514` | No es "la fila se oculta": la fila no existe |
| Reparentar al mismo padre es un no-op (`if (NewParent == _parent) return true`) | `GameObject.cs:275` | Por eso un segundo drop al mismo destino sólo reordena, no re-appendea |
| `Stack.ToArray()` devuelve LIFO (cima primero) | `System.Collections.Generic.Stack` | Afecta a las aserciones del historial de carpetas |
| `NavigateTo` empuja la carpeta **de la que se viene**, no la de destino | `ProjectPanel.cs:126` | La primera navegación ya deja `""` en la pila, que es lo que hace que Back vuelva a la raíz |

## ProjectPanel

| ID | file:line | Descripción | Conf. |
|---|---|---|---|
| H-ED-1 | 1169 vs 1122 | Renombrar una carpeta sólo rebasea `_currentFolder` por igualdad exacta; si la carpeta actual es un **descendiente** queda apuntando a una ruta muerta. El borrado sí lo cubre con `StartsWith(sel + "/")` | alta |
| H-ED-2 | 429-485 | `PerformAssetMove` nunca rebasea `_currentFolder` ni el historial de navegación | alta |
| H-ED-3 | 123-143 | El historial Back/Forward nunca se poda ni se valida: tras un rename o un borrado navega a carpetas inexistentes | alta |
| H-ED-4 | 752 → 1148 vs 637 | F2 sobre una carpeta del árbol abre el overlay con id `proj_asset_*`, pero el árbol sólo dibuja el overlay de `proj_folder_*`: el overlay queda activo e invisible y Enter no hace nada | alta |
| H-ED-5 | 987, 1022, 1161 | El menú ofrece Rename/Delete para sub-assets y ambos son no-ops (la ruta lleva `#`), con un toast de "nombre ya existe" que además es falso | alta |
| H-ED-6 | 506-521, 865, 1290, 1365 | Arrastrar un sub-asset ilumina la carpeta destino (el guard de ciclos lee el directorio antes del `#`) y luego no mueve nada, sin aviso | alta |
| H-ED-7 | 1615-1618 | Con búsqueda activa el placeholder de "create" se filtra, así que `CreateAssetTask` nunca se completa | media |
| H-ED-8 | 700, 551 | "Show Hidden" no llega al árbol: `BuildFolderNodes` filtra los dot-carpeta sin condición y la cache del árbol sólo keya en `ContentVersion` (la de contenido sí keya `_showHidden`, 1534) | alta |
| H-ED-9 | 41-49 | El botón de refresh crea un backend nuevo sin hacer `Dispose` del anterior (watchers duplicados), y la cache keyea por un `int` que puede no avanzar | media |
| H-ED-10 | 1667 | `Selection` no se reconcilia tras renombrar/mover/borrar: filas fantasma, contador del footer inflado, borrado silencioso sobre rutas muertas | alta |
| H-ED-11 | 1624-1625 | `GetFileNameWithoutExtension(".gitignore")` es `""`: con ocultos visibles y extensiones ocultas la fila sale en blanco | alta (impacto bajo) |
| H-ED-12 | 674-688 | `IsFolderEmpty` ignora `_showHidden`, así que una carpeta con sólo dot-ficheros se pinta como vacía | alta (impacto bajo) |
| H-ED-13 | 187-195, 243-247 | El hover de destino de un drop va un frame retardado y se mezcla con una lectura del mismo frame | media |

## HierarchyPanel

| ID | file:line | Descripción | Conf. |
|---|---|---|---|
| H-ED-14 | 780 y 911 | **Delete duplicado** en el menú de contexto: mismo comando dos veces, sólo cambia `danger` | alta |
| H-ED-15 | 471-484 | El drop al fondo ("unparent to root") **no** pasa por `ExcludeNestedSelections`, que sí usan las rutas 224, 573, 780, 826, 911, 1002: seleccionar padre+hijo y soltar en vacío los aplana a root | alta |
| H-ED-16 | 517-519 vs 551 | La búsqueda conserva los ancestros de una coincidencia pero nunca fuerza la expansión (`OverrideExpanded` sólo se usa para el ping), así que lo que vive bajo un nodo colapsado no se dibuja | alta |
| H-ED-17 | 304-313 | El scroll-to-ping calcula la Y con el índice absoluto de `treeNodes` en vez de con las filas visibles | media-alta |
| H-ED-18 | 464-465 → 605-611 | Soltar "como primer hijo" con varios objetos: `SetParent` (append) y luego `SetSiblingIndex(0)` por objeto invierte el orden, al contrario que `Into` | alta |
| H-ED-19 | 323-328 + `Selection.cs:15,94` | El rango con shift usa el índice absoluto y selecciona filas colapsadas invisibles; el ancla `_lastClickedIndex` es global, compartida por todas las listas del editor | media |
| H-ED-20 | 72-84, 449 | En el frame del drop no se promueve el hover diferido: un destino recién hoverado cae en "unparent to root" | media |
| H-ED-21 | 551-552 | El ping fuerza la expansión escribiendo estado persistente del árbol: queda expandido para siempre tras el ping | media |
| H-ED-22 | 54-58, 263-287 | El estado del ping está partido: los GUID son `static` pero `_forceExpandedIds` y el scroll son de instancia, así que un ping tras reabrir el panel no expande ni desplaza | media |
| H-ED-23 | 61, 568 | `_expandState` nunca se limpia: guarda entradas `true` de objetos borrados o filtrados que `IsTargetExpanded` sigue creyendo | media-alta |
| H-ED-24 | 336-343 | Click derecho sin modificador **destruye** la multi-selección; con Ctrl/Shift sobre una fila no seleccionada añade en vez de reemplazar; sólo mira las teclas *izquierda* | alta |
| H-ED-25 | 758 | El título del menú usa `Selection.Count` mientras los rótulos `(N)` usan `selectedGOs.Count` | media |
| H-ED-26 | 471-485 | El unparent a root no fija índice de raíz (queda en orden de registro) y el redo sólo graba `SetParent(default)` | media |

## InspectorPanel

| ID | file:line | Descripción | Conf. |
|---|---|---|---|
| H-ED-27 | 498 | `lastMod:yyyy-MM-dd` con `CurrentCulture`: con un calendario no gregoriano el sello sale en otro calendario | alta |
| H-ED-28 | 497 | `new DateTime(entry.LastModifiedTicks)` sin validar desde metadata persistida: un meta corrupto lanza dentro de `OnGUI` y tumba el frame | media |
| H-ED-29 | 638 | `.Count(f => !f.EndsWith(".meta"))` es sensible a cultura **y** a mayúsculas: un `Foo.META` cuenta como contenido, y el resultado equivocado se cachea | alta |
| H-ED-30 | 505-524 | Las listas de dependencias/dependientes se truncan a 20 sin indicador, mientras el resumen de selección sí muestra "and_more" | media |
| H-ED-31 | 948 vs 958 | El fallback de GUID corto sale con y sin "..." según el camino, para la misma situación | alta (impacto bajo) |
| H-ED-32 | 273-279, 297 | Una selección mixta (1 GameObject + N assets) con el GameObject activo muestra sólo el GO: los assets no aparecen en ninguna parte | media-alta |
| H-ED-33 | 394, 400, 472, 494, 845, 929 | Se muestra `Type.Name` crudo: ``List`1``, `Outer+Inner` | media-baja |
| H-ED-34 | 552-577 | El `switch` de import-settings no tiene `default`: los tags `Compound`, `Long` y `Double` no se pintan y no hay indicación de que el ajuste exista | media |

## PreferencesPanel

| ID | file:line | Descripción | Conf. |
|---|---|---|---|
| H-ED-35 | 292 | Botón **"Apply" en inglés hardcodeado**; los demás chips usan `Loc`, y ya existe la clave `inspector.apply` (usada en 579) | alta |
| H-ED-36 | 617-714 (sin `OnClosed`) | Cerrar la pestaña o cambiar de categoría con `_rebindingId != null` deja `ShortcutManager.IsRebinding` en `true` y **todos** los atajos dejan de responder | alta |
| H-ED-37 | 150-162 | `fpsIndex < 0 ? 0` mapea un valor fuera de lista (p. ej. 90) a "Unlimited" en pantalla sin cambiar el ajuste real | media-alta |
| H-ED-38 | 133-139 | El mismo patrón con `ThumbnailSize`: un 256 se muestra como "32" | media |
| H-ED-39 | 432-440 | El preset de densidad se detecta por igualdad exacta de floats: cualquier edición parcial da `-1`, sin segmento activo, y el callback lo fuerza a Cozy | media |
| H-ED-40 | 424, 446-454 | Los sliders formatean con `"F2"` y `CurrentCulture`: coma decimal en pantalla frente a JSON invariant | media |
| H-ED-41 | 284 | `EndsWith(".prowltheme")` sensible a cultura y mayúsculas: `My.PROWLTHEME` se guarda como `My.PROWLTHEME.prowltheme` | media (impacto bajo) |
| H-ED-42 | 356 | La tarjeta del preset sigue "seleccionada" tras editar el tema, porque sólo compara `theme.Name` | media |

## H-ED-43

| ID | file:line | Descripción | Conf. |
|---|---|---|---|
| H-ED-43 | `ProjectPanel.cs:1633` | `FormatSize` formatea la rama de un decimal con `"0.#"` y sin `IFormatProvider`: el separador es el de `CurrentCulture`, así que 1536 bytes se ven "1.5 KB" o "1,5 KB" según el editor | media |

## Descartados durante el análisis

Se llegaron a mirar y **no** son bugs (anotados para que nadie los vuelva a investigar):

- `BuildAssetDragPayload` (`ProjectPanel.cs:395`) **sí** filtra los sub-assets; sólo los constructores de payload de ruta única dejan pasar la ruta con `#` (H-ED-6).
- `ContentItem.Equals`/`GetHashCode` son coherentes entre sí (no hay violación del contrato de hash); son la causa raíz de H-ED-10, no un bug por sí mismos.
- `TabId`/`ParseTab` y el round-trip índice↔tamaño de miniaturas son correctos para los valores legales.
- Los hex de los presets de `ApplyPreset` coinciden con los de `EditorThemeData`.
- El `!cancellable` invertido en `OnDismissed` (`InspectorPanel`) es la intención documentada y no puede revertir dos veces: `Modal.Pop()` no invoca `OnDismissed`.
- `RenameOverlay.Begin` sobrescribe los callbacks, pero eso es justo lo que H-ED-7 señala como segundo camino al bloqueo, no un terceiro bug.
- `OnCreated` (`ProjectPanel.cs:1072-1084`) es código muerto: la única referencia en el repo es su propia definición.
- `FindGOByIdentifier` (`HierarchyPanel.cs:1261`) es código muerto: no tiene call site.
- La búsqueda en `BuildNodeList` llama a `go.GetChildrenDeep()` por nodo cuando hay texto de búsqueda: O(n·subárbol) por pulsación de tecla.
- La caída de `DragDrop.EndDrag()` tras una validación fallida (`ProjectPanel.cs:255`) es probablemente inocua: el framework ya terminó el arrastre en el frame del drop (no verificable sin el fuente de Origami).