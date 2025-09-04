Unity setup guide (core card systems)

1) Data
- Place card CSV at `Assets/Resources/CA_Data.csv` with header: CA_ID,CA_Name_DIS,CA_Type,CA_HPMaximum,CA_ATK_INI,CA_EffectInfo_DIS,CA_DPCost,CA_MainImage
- Put main images (Sprite) at `Assets/Resources/CA_MainImages/` using names from CA_MainImage column.

2) Effect dispatcher
- `CA_EffectInfo.cs` trims leading zeros from CA_ID, subtracts 1 to get index, and binds effect scripts.
- Example bound index=3 implements damage -1 via `Effect_ReduceIncomingDamageByOne` which provides `IBeforeDamageHook`.
- Add new effects by extending the binder map in `CA_EffectInfo`.

3) Type manager (2D 或 3D 通用)
- Add `CA_TypeManager` to a `CardManager` GameObject.
- Map each `CA_Type` to a prefab (Unit/Event). Prefab hierarchy:
  CA EmptyParent
  - MainImage (Image)
  - HP icon (Image)
    - HP text (TMP)
  - ATK icon (Image)
    - ATK text (TMP)
  - EffectInfo (TMP)
  - Name (TMP)

4) 3D 版卡牌（物理+拖拽）
- 使用 `CardView3D`（`Assets/Scripts/CA/CardView3D.cs`）。在 3D 卡牌 Prefab 根物体上添加：`Rigidbody(useGravity=true, isKinematic=false)` 与 `BoxCollider`。
- 将 `MainRenderer/HPText/ATKText/EffectInfoText/NameText/BoxCollider/Rigidbody` 引用拖入。
- `Bind(CardData)` 会把 Texture2D 贴到材质，并自动按贴图像素缩放可视与碰撞盒（默认 1px=0.001u，250x400→0.25x0.4）。
- 鼠标按下开始拖拽：沿水平面（Y=DragPlaneY）移动；松开后恢复重力，让卡掉落到桌面。
- Add `CardView` to card prefab root. Wire: MainImage, HPText, ATKText, EffectInfoText, NameText.
- `Bind(CardData)` sets texts, loads sprite from `Resources/CA_MainImages/`, and binds effect scripts via `CA_EffectInfo`.
- Dragging: oscillating rotation until released. On release calls `ca_PlacementCheck` then `ca_DP` or `ca_BackToHand` (stubs to replace with game rules).

5) Card info panel (shared)
- Create a panel `CardInfoPanel` with the specified child fields and add `CardInfoPanelController`.
- Any `CardView` left-click calls `CardInfoPanelController.Instance.ShowFromCard(this)`.

6) Deck editing
- Use `DeckBuilderController` in DeckEdit scene. Provide two prefabs: one for collection items (with TMP_Text and Button) and one for deck items (TMP_Text).
- Populate `PlayerCollection` from your save; click to add to `CurrentDeck`. Right grid auto-sorted by cost then ID.
- Target deck size: 40 cards (enforce in UI as needed).

7) Shuffling and starting hand
- `DeckShuffler` bucketizes deck by dynamic cost thresholds (range thirds), shuffles to favor low early, mid midgame, high lategame.
- `DrawStartingHand` enforces low-cost guarantees per spec when possible.

8) 桌面与测试生成（3D）
 - 在场景中新建 `Table` 物体：添加 `MeshRenderer + MeshCollider`（或 Plane），再挂 `TablePlane`（`Assets/Scripts/Environment/TablePlane.cs`）以固定桌面 Y 高度（如 0）。
 - 将 `CardManager` 放入场景，配置 `CA_TypeManager` 的类型→Prefab。
 - 添加 `CardSpawner3D` 到任意物体，拖入 `TypeManager`、`SpawnParent`，设置生成的 `CA_ID` 数组，即可在运行时生成并掉到桌面。
```csharp
var typeMgr = FindObjectOfType<CA_TypeManager>();
CardDatabase.EnsureLoaded();
if (CardDatabase.TryGet(4, out var data))
{
    var go = typeMgr.CreateCardByType(data.CA_Type, parentTransform);
    var view = go.GetComponent<CardView>();
    view.Bind(data);
}
```

Integration notes
- Replace `ca_PlacementCheck`/`ca_DP` with your battlefield and cost systems.
- Hook effect interfaces (e.g., `IBeforeDamageHook`) into your combat pipeline by querying attached components on the unit GameObject.


