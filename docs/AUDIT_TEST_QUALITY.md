# AuditorÃ­a de calidad de tests de Zenith

Clasifica cada test por tipo (TRIVIAL, NOASSERT, BOOLONLY, ZERO). Generada por anÃ¡lisis externo el
2026-10-03, y **verificada a mano contra el cÃ³digo fuente** el mismo dÃ­a. Los conteos y clasificaciones
son la foto de ese momento: actualizar si el dataset cambia.

---

## Procedencia y lÃ­mites (leer primero)

El anÃ¡lisis externo produjo 12 ficheros de artefactos (`_*_list.txt`, `_zero.txt`) enumerando 1 624
nombres de test. Esos ficheros **se eliminaron** tras consolidar este documento y su patrÃ³n se aÃ±adiÃ³ a
`.gitignore`. Para no perder informaciÃ³n se conservan:

- **Los agregados**, que son lo accionable (secciones 1 y 5).
- **La lista de NOASSERT verificada a mano** (secciÃ³n 2), los 7 casos reales.
- **La lista de TRIVIAL de una aserciÃ³n** (secciÃ³n 3), completa, 112 entradas.

**Lo que no se conserva:** el detalle de los 257 BOOLONLY test a test, ni el de los TRIVIAL con 2 o más
aserciones. Si hacen falta, hay que reejecutar la herramienta externa.

**Advertencia metodolÃ³gica, y es lo mÃ¡s importante de este documento:** la herramienta externa
clasificaba como "sin aserciones" cualquier test sin llamadas `Assert.*` **literales en el cuerpo del
mÃ©todo**. Este repo aserta mediante **helpers** (`AssertVec`, `UITestHelpers.AssertRect`,
`AssertBakeIntegrity`...), y esos helpers no los ve. De sus 48 "NOASSERT", **43 sÃ­ asertan**. Por eso
las cifras de la secciÃ³n 1 se corrigen aquÃ­.

---

## 1. Resumen numÃ©rico

### Foto del anÃ¡lisis externo (2026-10-03 18:32)

| CategorÃ­a | Runtime | Editor | Total |
|---|---|---|---|
| Tests en el universo analizado | 956 | 668 | 1 624 |
| `TRIVIAL` (pocas aserciones) | 240 | 121 | 361 |
| de los cuales, exactamente 1 aserciÃ³n | 71 | 41 | **112** |
| `NOASSERT` (segÃºn la herramienta) | 42 | 6 | 48 |
| `BOOLONLY` (sÃ³lo booleanas) | 171 | 86 | **257** |

### Correcciones tras verificar

| Cifra | AnÃ¡lisis externo | Verificado |
|---|---|---|
| `NOASSERT` | 48 | **7** (43 eran falsos positivos) |
| `BOOLONLY` | 257 | **â‰¤257**: 257/257 sin aserciÃ³n no booleana *directa*, pero con el mismo punto ciego de helpers, asÃ­ que el nÃºmero real es menor o igual |
| `TRIVIAL` = 1 aserciÃ³n | 112 | **112 confirmados** (108 con exactamente 1, 4 con 2) |

No hay ninguna entrada obsoleta: los 956 tests del Runtime y los 668 del Editor **siguen existiendo**.
La cobertura del anÃ¡lisis fue 956/1031 mÃ©todos de Runtime (92,7%) y 668/682 de Editor (97,9%); los 89
restantes no se clasificaron (11 son los tests de H-RD-52/53/54, posteriores al anÃ¡lisis).

---

## 2. `NOASSERT` â€” los tests que no verifican nada

Verificados leyendo el cuerpo de cada uno. **7 casos, y los 7 son del mismo estilo**: ejecutan cÃ³digo y
confÃ­an en que no salte excepciÃ³n (*"must not throw"*). Es un estilo legÃ­timo, pero el nombre promete
mucho mÃ¡s de lo que comprueba.

| Archivo | Test | LÃ­nea | QuÃ© promete vs. quÃ© hace |
|---|---|---|---|
| `SceneDispatcherTests.cs` | `AThrowingCallback_IsContainedRatherThanUnwinding` | 85 | Afirma que la excepciÃ³n se contiene y **no** desenrolla. SÃ³lo la lanza: si el dispatcher la dejara propagar, **el test pasa** |
| `SceneDispatcherTests.cs` | `PhysicsEvent_WithNoHandlers_DoesNothing` | 188 | Que no ocurre nada. Llama y no observa ningÃºn efecto |
| `SceneDispatcherTests.cs` | `PhysicsEvent_OnNullGameObject_IsIgnored` | 280 | Que se ignoran. SÃ³lo detecta si lanza |
| `ProwlActionTests.cs` | `Invoke_OnANullTarget_IsANoOp` | 95 | Que invocar sobre null es no-op. SÃ³lo detecta si lanza |
| `ComponentClipboardTests.cs` | `PasteValues_Undo_AfterComponentRemoved_FailsGracefully` | 622 | Que falla "con elegancia". No comprueba el estado ni la elegancia |
| `GraphicsShutdownTests.cs` | `Submit_AfterShutdownBegins_IsANoOpAndDoesNotThrow` | 32 | *"DoesNotThrow"* es honesto: la aserciÃ³n **es** que no lance |
| `GraphicsShutdownTests.cs` | `SubmitAndWait_AfterShutdownBegins_IsANoOpAndDoesNotThrow` | 50 | Ãdem |

`AThrowingCallback_IsContainedRatherThanUnwinding` es el mÃ¡s peligroso de los siete: su nombre afirma
precisamente el invariante que no mide, y el resto del fichero sÃ­ usa `Assert` con contenido.

### Falsos positivos que deben ignorarse

La herramienta marcÃ³ 48; 43 assertan mediante helpers. Ejemplos verificados:

| Test | Por quÃ© es falso positivo |
|---|---|
| `TransformTests.WorldMatrix_Reparent_InvalidatesCache` | Aserta con `AssertVec(...)` en las lÃ­neas 338 y 341 |
| `RectTransformTests.ComputeRect_FixedAnchors_CentersRectOnAnchorPoint` | Aserta con `UITestHelpers.AssertRect(...)` en las lÃ­neas 20 y 21 |
| `NavMeshCollectorTests.Terrain_SteepBake_SurfaceIsContinuousGroundedAndSliverFree` | Aserta con `AssertBakeIntegrity(...)` en la lÃ­nea 387 |
| `NavMeshCollectorTests.Terrain_SculptedPlateauBake_...` | Ãdem, lÃ­nea 401 |

**El "hotspot" que seÃ±alaba el anÃ¡lisis era falso:** `TransformTests.cs` figuraba con 20 de los 48,
y 19 de esos 20 sÃ­ verifican.

---

## 3. `TRIVIAL` â€” tests con una sola aserciÃ³n

112 tests (71 Runtime + 41 Editor). A diferencia de `NOASSERT`, **esta cifra es fiable**: se
verificÃ³ que 108 tienen exactamente 1 aserciÃ³n y 4 tienen 2. Es la categorÃ­a real mÃ¡s numerosa y la
que merece la revisiÃ³n.

ConcentraciÃ³n (tests con 1 aserciÃ³n por fichero):

| Archivo | NÂº | Archivo | NÂº |
|---|---|---|---|
| `PhysicsTests.cs` | 16 | `BuildSystemTests.cs` | 9 |
| `TerrainTests.cs` | 8 | `PrefabTests.cs` | 9 |
| `HeadlessGraphicsTests.cs` | 4 | `SceneManagementTests.cs` | 4 |
| `AudioTests.cs` | 4 | `AssetDatabaseTests.cs` | 4 |

Lista completa por fichero (ninguno supera las 30 entradas):

**AssemblyDefinitionTests.cs** (1)

| Test |
|---|
| `SelfReferencingAsmdef_CompilesWithoutCycleError` |

**AssetDatabaseTests.cs** (4)

| Test |
|---|
| `CreateAsset_PathTraversal_IsRejected` |
| `LockPermanent_Twice_IsIdempotent_SingleUnlockReleasesIt` |
| `MoveAsset_ToOccupiedPath_Fails` |
| `TickIdleSweep_RespectsInterval_NoOpRightAfterAFullSweep` |

**AudioTests.cs** (4)

| Test |
|---|
| `Distances_AreStraightenedOutOnValidate` |
| `Mixer_DropsRoutingToAGroupThatIsNotThere` |
| `Mixer_StraightensOutARoutingLoopOnLoad` |
| `PlayClipAtPoint_WithoutADevice_SpawnsNothing` |

**BakeMeshTests.cs** (1)

| Test |
|---|
| `MeshVersion_IncrementsOnGeometryChange` |

**Boolean32MatrixTests.cs** (4)

| Test |
|---|
| `Constructor_Default_CreatesEmptyMatrix` |
| `Indexer_InvalidIndices_HandlesGracefully` |
| `IsSymmetric_ReturnsFalseForAsymmetricMatrix` |
| `IsSymmetric_ReturnsTrueForSymmetricMatrix` |

**BuildSystemProjectTests.cs** (1)

| Test |
|---|
| `NoProcessors_LeavesTheBuildUntouched` |

**BuildSystemTests.cs** (9)

| Test |
|---|
| `AnAlreadyCancelledToken_RunsNothing` |
| `Cancel_AfterCompletion_DoesNothing` |
| `Cancel_IsSafeToRepeat` |
| `Cancelling_StopsTheBuildAndSkipsWhatFollows` |
| `ChangingAnyPartOfTheKey_Misses` |
| `EditorOnlyPlugin_NeverShips` |
| `EveryDesktopTarget_HasARuntimeIdentifier` |
| `NonDiagnosticLines_AreIgnored` |
| `NoProjectSuffix_LeavesProjectNull` |

**CharacterControllerTests.cs** (3)

| Test |
|---|
| `ACastWithNoDirectionIsRefusedRatherThanThrowing` |
| `BlockedMovementReportsTheDistanceActuallyTravelled` |
| `DepenetrationPushesOutOfTheFloorRatherThanThroughIt` |

**ComponentClipboardTests.cs** (2)

| Test |
|---|
| `PasteAsNew_Redo_AfterTargetDeleted_FailsGracefully` |
| `PasteAsNew_Undo_AfterTargetDeleted_FailsGracefully` |

**ComponentTests.cs** (3)

| Test |
|---|
| `GetComponent_ReturnsNull_WhenAbsent` |
| `RemoveComponent_ByGuid_RemovesInstance` |
| `RemoveComponent_RequiredByAnother_IsBlocked` |

**CustomAssetInspectorTests.cs** (1)

| Test |
|---|
| `CustomAssetHasNoEditorOfItsOwn` |

**EditorHarnessSmokeTests.cs** (1)

| Test |
|---|
| `Prefab_FileIsWrittenUnderAssets` |

**HandleContextTests.cs** (3)

| Test |
|---|
| `DepthDoesNotOverrideAClearScreenDistanceWin` |
| `NonCandidateDistance_DoesNotStarveObjectPicking` |
| `OverlappingHandles_ResolveToTheOneInFront` |

**HeadlessGraphicsTests.cs** (4)

| Test |
|---|
| `DefaultShaderMenuPaths_AreUnique` |
| `Graphics_ReportsHeadless_WhenNoDevice` |
| `Material_CreatesHeadless_WithoutThrowing` |
| `Mesh_UploadHeadless_DoesNotThrow` |

**HeadlessRunTests.cs** (1)

| Test |
|---|
| `RunHeadless_SetsHeadlessFlagDuringRun` |

**HierarchyTests.cs** (2)

| Test |
|---|
| `EnabledInHierarchy_DisabledChildStaysDisabled_WhenParentReEnabled` |
| `GetSiblingIndex_NoParent_ReturnsNull` |

**HotReloadSceneTests.cs** (1)

| Test |
|---|
| `WatchingEditorAssembly_WalksStaticsHeadless_WithoutCrashing` |

**LightBVHTests.cs** (1)

| Test |
|---|
| `Build_Bounds_Cover_All_Tight_Lights` |

**MeshTests.cs** (1)

| Test |
|---|
| `Serialize_EmptyMesh_DoesNotThrow` |

**NavMeshBuildTests.cs** (2)

| Test |
|---|
| `Build_WithNoGeometry_ReturnsNull` |
| `FlatQuad_DownFacingWinding_ReturnsNull` |

**NavMeshComponentTests.cs** (1)

| Test |
|---|
| `Agent_RotatesTowardTravelDirection` |

**NavMeshModifierTests.cs** (1)

| Test |
|---|
| `Source_NotWalkableArea_ProducesNoPolys` |

**PhysicsTests.cs** (16)

| Test |
|---|
| `BoxCollider_RegistersShape` |
| `CapsuleCollider_RegistersShape` |
| `CollisionMatrix_DisabledLayers_DynamicPassesThroughStatic` |
| `CollisionMatrix_EnabledLayers_DynamicRestsOnStatic` |
| `ConeCollider_RegistersShape` |
| `ConstraintProperties_AreSafeAfterTheirBodyIsDisabled` |
| `CylinderCollider_RegistersShape` |
| `IgnoreCollisionBetween_BodiesDoNotCollide` |
| `IgnoredCollisions_AreScopedToTheirOwnWorld` |
| `MassDependentForceModes_MoveHeavyBodiesLess` |
| `MeshCollider_Concave_RegistersTriangleMesh` |
| `MeshCollider_Convex_OnDynamicBody_RestsOnFloor` |
| `MeshCollider_Convex_RegistersHull` |
| `Rigidbody_CreatesQueryableBody_OnEnable` |
| `Rigidbody_SyncsTransformFromBody_AfterStep` |
| `SphereCollider_RegistersShape` |

**PluginTests.cs** (1)

| Test |
|---|
| `DuplicateManagedPluginFilenames_DoNotCrashCompile` |

**PrefabTests.cs** (10)

| Test |
|---|
| `AComponentTheInstanceAdded_CanBeRemovedFromIt` |
| `AComponentThePrefabProvides_CannotBeRemovedFromAnInstance` |
| `Build_StripReportsNothingToDoOnAPlainScene` |
| `EditingMode_IsRefusedDuringPlayMode` |
| `Import_RejectsPrefabThatIsNotAGameObject` |
| `Instantiate_NullData_ReturnsNull` |
| `RecordComponentOverrides_NoChange_NoOverride` |
| `RedoingAnApplyAddition_DoesNotPushAnotherUndoStep` |
| `ThePrefabDroppingAComponent_StillRemovesItFromInstances` |
| `UnpackingAnInstance_LetsItsComponentsBeRemoved` |

**ProjectPanelTests.cs** (1)

| Test |
|---|
| `CanAcceptAssetDropInto_ASubAssetDrag_IsAcceptedByAFolderButWouldMoveNothing` |

**RuntimeUtilsTests.cs** (3)

| Test |
|---|
| `ResolveType_MalformedOrUnloadable_ReturnsNullWithoutThrowing` |
| `ResolveType_NullOrBlank_ReturnsNull` |
| `ResolveType_TypeNameQualifiedWithWrongAssembly_ReturnsNull` |

**SceneCameraGatherTests.cs** (1)

| Test |
|---|
| `GatherCost_DoesNotScaleWithSceneSize` |

**SceneManagementTests.cs** (4)

| Test |
|---|
| `CancelDontDestroyOnLoad_LetsItDieWithTheScene` |
| `FrameCallbacks_OnADisposedScene_AreNoOps` |
| `FrameCallbacks_OnASceneDisposedMidCallback_DoNotThrow` |
| `ManualPreserve_AddingBackToSceneCurrentAfterLoad_LosesTheObject` |

**ScriptCompilationTests.cs** (2)

| Test |
|---|
| `GameScript_ReferencingEditorType_FailsToCompile` |
| `ProjectNamedLikeEnginePrefix_StillReferencesEngineDlls` |

**SerializationTests.cs** (1)

| Test |
|---|
| `AssetRef_Null_RoundTrips` |

**TerrainTests.cs** (8)

| Test |
|---|
| `AbsurdDensityStaysAffordable` |
| `CascadeSquareCoversItsBand` |
| `MeshBuildGoesStaleOnceTheSlackIsSpent` |
| `MeshBuildGoesStaleWhenTheDistanceChanges` |
| `RayLeavingTheGrid_TerminatesInsteadOfWalkingToTheDistanceLimit` |
| `RotatedTerrain_IsMissedByAWorldVerticalRay` |
| `UnpaintedPrototypesHaveNoBounds` |
| `VerticalRay_OutsideTheGrid_Misses` |

**UIRaycasterTests.cs** (3)

| Test |
|---|
| `TryPick_FueraDelRect_False` |
| `TryPick_RaycastTargetDeshabilitado_False` |
| `TryPick_SinGraphic_False` |

**UndoSkipFieldsTest.cs** (1)

| Test |
|---|
| `AllSkipFieldsMatchRealRuntimeFields` |

**UndoTests.cs** (4)

| Test |
|---|
| `Continuous_NoMovement_PushesNoStep` |
| `NoOpChange_DoesNotCreateStep` |
| `PropertyUndo_OnDestroyedTarget_IsSafeNoOp` |
| `WhenPlaying_RecordingIsNoOp` |

**WheelTests.cs** (3)

| Test |
|---|
| `Car_LandsFromJumpWithSpunWheels_DoesNotLurch` |
| `Car_OnSideSlope_DoesNotCreepSideways` |
| `Wheel_Airborne_IsNotGrounded` |

**WindZoneTests.cs** (3)

| Test |
|---|
| `GustsKeepTheFieldMoving` |
| `MovingTheZoneDoesNotScrambleTheField` |
| `StrongerWindDrivesGustsFaster` |

---

## 4. `BOOLONLY` â€” sÃ³lo asertan `true`/`false`

**257 tests** (171 Runtime + 86 Editor). Se verificÃ³ uno a uno que **ninguno** contiene una
aserciÃ³n de valor directa (`Assert.Equal`, `NotNull`, `Contains`, ...).

Reserva: ese chequeo tiene el **mismo punto ciego que la herramienta original** â€” no ve aserciones
hechas a travÃ©s de helpers. Un test que compara floats con `AssertVec(...)` y ademÃ¡s tiene un
`Assert.True(...)` aparecerÃ­a aquÃ­ como BOOLONLY sin serlo. **La cifra real es â‰¤257.**

ConcentraciÃ³n (los ficheros con mÃ¡s tests BOOLONLY):

| Archivo | NÂº |
|---|---|
| `PrefabTests.cs` | 21 |
| `TerrainTests.cs` | 17 |
| `PhysicsTests.cs` | 16 |
| `BuildSystemTests.cs` | 16 |
| `NavMeshComponentTests.cs` | 15 |
| `WheelTests.cs` | 12 |
| `Boolean32MatrixTests.cs` | 12 |
| `NavMeshObstacleTests.cs` | 11 |

**BOOLONLY no es un defecto.** `Assert.True(x >= 0)` sobre una coordenada es una aserciÃ³n
legÃ­tima y readable; en booleanos de geometrÃ­a es la forma natural de expressar una condiciÃ³n
compuesta. Marcar los 257 como deuda serÃ­a un error de lectura: el coste de revisar esta categorÃ­a es
alto y el beneficio, bajo. La cifra es contexto, no una lista de trabajo.

---

## 5. Recomendaciones para Fase 6

| # | AcciÃ³n | CategorÃ­a | Esfuerzo | Valor |
|---|---|---|---|---|
| 1 | Arreglar o renombrar los **7 tests de la secciÃ³n 2**. `AThrowingCallback_IsContainedRatherThanUnwinding` primero: mide lo contrario de lo que dice | `NOASSERT` | Bajo | **Alto** |
| 2 | Revisar los **112 tests de una sola aserciÃ³n**, empezando por `PhysicsTests` (16), `BuildSystemTests` (9) y `PrefabTests` (9) | `TRIVIAL` | Medio | Medio |
| 3 | **No tocar los 257 BOOLONLY**: son aserciones vÃ¡lidas en su mayorÃ­a | `BOOLONLY` | â€” | â€” |
| 4 | Si se reaudita, usar una herramienta que resuelva C# (no regex) y cuente helpers de aserciÃ³n | â€” | Bajo | **Alto** |

### QuÃ© es aceptable

- **Tests "must not throw"** con nombre honesto (`..._DoesNotThrow`): son valiosos. Cubren que un camino de cÃ³digo
  no revienta, que es una invariante real. El problema es sÃ³lo cuando el nombre promete mÃ¡s.
- **Tests de interfaz y de humo**: por naturaleza, una aserciÃ³n.
- **Tests BOOLONLY**: bien escritos, son tan legibles como un `Assert.Equal`.

### QuÃ© es deuda real

- Los **5 tests de la secciÃ³n 2 cuyo nombre afirma un invariante que no comprueba**. Un test que no
  verifica da la misma sensaciÃ³n de cobertura que uno que sí: eso es *falsa confianza*, y es peor que
  no tener test, porque desactiva la alarma.
- Los **112 de una sola aserciÃ³n** en los ficheros concentrados: no por tener una aserciÃ³n (eso es
  perfectly vÃ¡lido), sino por revisar si esa Ãºnica aserciÃ³n cubre lo que el nombre promete.

### Contexto

**Ninguno de estos tests ha fallado nunca** y el repo tiene **1 996 tests verdes** (1 274 Runtime +
722 Editor). Nada aquÃ­ indica una rotura: es una revisiÃ³n de quÃ© relied upon. Fase 6 deberÃ­a
priorizar el punto 1 (7 tests, una tarde) y tratar el punto 2 como revisiÃ³n de cÃ³digo normal, no
como campaÃ±a de calidad.

