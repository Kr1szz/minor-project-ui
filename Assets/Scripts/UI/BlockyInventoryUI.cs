using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BlockyInventory
{
    [Serializable]
    public sealed class InventoryItemData
    {
        public string id;
        public string displayName;
        [Min(0)] public int quantity;
        public Color iconTint = Color.white;
        public Sprite icon;

        public InventoryItemData() { }

        public InventoryItemData(string itemId, string itemName, int itemQuantity, Color tint, Sprite itemIcon = null)
        {
            id = itemId;
            displayName = itemName;
            quantity = itemQuantity;
            iconTint = tint;
            icon = itemIcon;
        }
    }

    /// <summary>Creates a five-slot, pointer-interactive inventory panel at runtime.</summary>
    public sealed class BlockyInventoryUI : MonoBehaviour
    {
        public const int SlotCount = 5;

        [Header("Starting inventory")]
        public List<InventoryItemData> startingItems = new List<InventoryItemData>();

        [Header("HUD layout")]
        [Min(420f)] public float panelWidth = 580f;
        [Min(90f)] public float panelHeight = 112f;
        public Vector2 screenPadding = new Vector2(0f, 16f);
        public int canvasSortingOrder = 40;

        [Header("Input")]
        public bool createEventSystemWhenMissing = true;

        public event Action<int, InventoryItemData> ItemSelected;
        public event Action<InventoryItemData> ItemUsed;
        public event Action InventoryChanged;

        private readonly List<InventoryItemData> items = new List<InventoryItemData>(SlotCount);
        private readonly SlotView[] slotViews = new SlotView[SlotCount];
        private bool dataInitialized;
        private int selectedIndex;
        private int hoveredIndex = -1;
        private int draggingIndex = -1;
        private Canvas inventoryCanvas;
        private RectTransform canvasRect;
        private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
        private Image dragGhost;
        private Text dragGhostLabel;
        private Font uiFont;
        private Texture2D solidTexture;
        private Sprite solidSprite;
        private readonly List<Sprite> runtimeItemSprites = new List<Sprite>();
        private readonly List<Texture2D> runtimeItemTextures = new List<Texture2D>();

        private static readonly Color SlotIdle = HexColor("#171410");
        private static readonly Color SlotInterior = HexColor("#39332C");
        private static readonly Color SlotHover = HexColor("#927943");
        private static readonly Color SlotSelected = HexColor("#D5AA55");
        private static readonly Color TextPrimary = HexColor("#F2E8D4");
        private static readonly Color TextMuted = HexColor("#B4A58A");

        private sealed class SlotView
        {
            public Image border;
            public Image interior;
            public Image iconImage;
            public Image iconBase;
            public Image iconHighlight;
            public Image iconShadow;
            public Text itemName;
            public Text quantity;
        }

        private void Awake()
        {
            EnsureEventSystem();
            BuildCanvas();
            InitializeData();
            BuildPanel();
            RefreshAll();
        }

        private void OnDestroy()
        {
            if (dragGhost != null)
                Destroy(dragGhost.gameObject);
            if (solidSprite != null)
                Destroy(solidSprite);
            if (solidTexture != null)
                Destroy(solidTexture);
            for (int i = 0; i < runtimeItemSprites.Count; i++)
            {
                if (runtimeItemSprites[i] != null)
                    Destroy(runtimeItemSprites[i]);
            }
            for (int i = 0; i < runtimeItemTextures.Count; i++)
            {
                if (runtimeItemTextures[i] != null)
                    Destroy(runtimeItemTextures[i]);
            }
        }

        /// <summary>Returns a copy of the item in the requested slot, or null when the slot is empty.</summary>
        public InventoryItemData GetItemAt(int slotIndex)
        {
            EnsureDataInitialized();
            if (!IsValidSlot(slotIndex) || IsEmpty(items[slotIndex]))
                return null;
            return CopyItem(items[slotIndex]);
        }

        /// <summary>Adds an item to its matching stack or the first empty slot. Returns false when full.</summary>
        public bool AddItem(string id, string displayName, int quantity, Color iconTint, Sprite icon = null)
        {
            EnsureDataInitialized();
            if (string.IsNullOrWhiteSpace(id) || quantity <= 0)
                return false;

            for (int i = 0; i < items.Count; i++)
            {
                if (!IsEmpty(items[i]) && items[i].id == id)
                {
                    items[i].quantity += quantity;
                    RefreshAll();
                    InventoryChanged?.Invoke();
                    return true;
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (IsEmpty(items[i]))
                {
                    items[i] = new InventoryItemData(id, string.IsNullOrWhiteSpace(displayName) ? id : displayName, quantity, iconTint, icon);
                    RefreshAll();
                    InventoryChanged?.Invoke();
                    return true;
                }
            }

            return false;
        }

        /// <summary>Removes quantity from the first matching stack and returns whether an item was found.</summary>
        public bool RemoveItem(string id, int quantity = 1)
        {
            EnsureDataInitialized();
            if (string.IsNullOrWhiteSpace(id) || quantity <= 0)
                return false;

            for (int i = 0; i < items.Count; i++)
            {
                if (!IsEmpty(items[i]) && items[i].id == id)
                {
                    items[i].quantity -= quantity;
                    if (items[i].quantity <= 0)
                        items[i] = new InventoryItemData();
                    RefreshAll();
                    InventoryChanged?.Invoke();
                    return true;
                }
            }

            return false;
        }

        /// <summary>Replaces a slot's contents. Pass null or a non-positive quantity to clear it.</summary>
        public void SetSlotItem(int slotIndex, InventoryItemData item)
        {
            EnsureDataInitialized();
            if (!IsValidSlot(slotIndex))
                return;

            items[slotIndex] = IsEmpty(item) ? new InventoryItemData() : CopyItem(item);
            RefreshAll();
            InventoryChanged?.Invoke();
        }

        private void InitializeData()
        {
            if (dataInitialized)
                return;

            dataInitialized = true;
            items.Clear();
            bool useDemoItems = startingItems == null || startingItems.Count == 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (useDemoItems)
                    items.Add(CreateDemoItem(i));
                else if (i < startingItems.Count && !IsEmpty(startingItems[i]))
                    items.Add(CopyItem(startingItems[i]));
                else
                    items.Add(new InventoryItemData());
            }
        }

        private void EnsureDataInitialized()
        {
            if (!dataInitialized)
                InitializeData();
        }

        private InventoryItemData CreateDemoItem(int index)
        {
            switch (index)
            {
                case 0: return new InventoryItemData("pvc_pipe", "PVC Pipe", 1, HexColor("#A8C6C4"), CreateItemIcon("pvc_pipe"));
                case 1: return new InventoryItemData("elbow_90", "90° Elbow", 1, HexColor("#7EC9B3"), CreateItemIcon("elbow_90"));
                case 2: return new InventoryItemData("ball_valve", "Ball Valve", 1, HexColor("#D9B46A"), CreateItemIcon("ball_valve"));
                case 3: return new InventoryItemData("outlet_tap", "Outlet Tap", 1, HexColor("#66BFA8"), CreateItemIcon("outlet_tap"));
                default: return new InventoryItemData("pressure_gauge", "Pressure Gauge", 1, HexColor("#88B7C7"), CreateItemIcon("pressure_gauge"));
            }
        }

        private Sprite CreateItemIcon(string itemId)
        {
            string[] rows;
            switch (itemId)
            {
                case "pvc_pipe":
                    rows = new[] { "000000000000", "000000000000", "000000000000", "000000000000", "111111111100", "122222222100", "133333332100", "111111111100", "000000000000", "000000000000", "000000000000", "000000000000" };
                    break;
                case "elbow_90":
                    rows = new[] { "000000000000", "000000000000", "000000000000", "111111110000", "122222210000", "133333310000", "000000110000", "000000110000", "000000110000", "000000110000", "000000000000", "000000000000" };
                    break;
                case "ball_valve":
                    rows = new[] { "000000000000", "000001100000", "000001100000", "000011110000", "111111111100", "122344221100", "133344331100", "111111111100", "000011110000", "000001100000", "000001100000", "000000000000" };
                    break;
                case "outlet_tap":
                    rows = new[] { "000000000000", "001111111000", "000011000000", "000011000000", "111111111100", "122222221100", "133333331100", "000011000000", "000011111000", "000011001100", "000011001100", "000000000000" };
                    break;
                default:
                    rows = new[] { "000011110000", "001122221100", "011222222110", "112222222211", "112233222211", "112223322211", "112222332211", "112222233211", "011222222110", "001111111100", "000001100000", "000001100000" };
                    break;
            }

            return CreateComponentSprite(rows, GetItemColor(itemId), itemId);
        }

        private static Color GetItemColor(string itemId)
        {
            switch (itemId)
            {
                case "pvc_pipe": return HexColor("#A8C6C4");
                case "elbow_90": return HexColor("#7EC9B3");
                case "ball_valve": return HexColor("#D9B46A");
                case "outlet_tap": return HexColor("#66BFA8");
                default: return HexColor("#88B7C7");
            }
        }

        private Sprite CreateComponentSprite(string[] rows, Color baseColor, string itemId)
        {
            int width = rows[0].Length;
            int height = rows.Length;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "Inventory Component Icon";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    char pixel = rows[y][x];
                    if (pixel == '0')
                    {
                        texture.SetPixel(x, height - 1 - y, Color.clear);
                        continue;
                    }

                    Color pixelColor = pixel == '2'
                        ? Color.Lerp(baseColor, Color.white, 0.58f)
                        : pixel == '3'
                            ? Color.Lerp(baseColor, Color.black, 0.48f)
                            : pixel == '4'
                                ? HexColor("#D8C28C")
                                : baseColor;
                    texture.SetPixel(x, height - 1 - y, pixelColor);
                }
            }

            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), width);
            sprite.name = "Inventory " + itemId + " Icon";
            runtimeItemTextures.Add(texture);
            runtimeItemSprites.Add(sprite);
            return sprite;
        }

        private void EnsureEventSystem()
        {
            if (!createEventSystemWhenMissing || FindAnyObjectByType<EventSystem>() != null)
                return;

            GameObject eventSystemObject = new GameObject("Inventory Event System", typeof(EventSystem));
            InputSystemUIInputModule inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new GameObject("Blocky Inventory Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            inventoryCanvas = canvasObject.GetComponent<Canvas>();
            inventoryCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            inventoryCanvas.pixelPerfect = true;
            inventoryCanvas.sortingOrder = canvasSortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
            canvasRect = canvasObject.GetComponent<RectTransform>();

            solidTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "Inventory Solid Pixel",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            solidTexture.SetPixel(0, 0, Color.white);
            solidTexture.Apply(false, true);
            solidSprite = Sprite.Create(solidTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
            solidSprite.name = "Inventory Solid Sprite";
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void BuildPanel()
        {
            RectTransform hudRoot = CreateRect("Component Hotbar", canvasRect, new Vector2(panelWidth, panelHeight), new Vector2(screenPadding.x, panelHeight * 0.5f + screenPadding.y));
            hudRoot.anchorMin = new Vector2(0.5f, 0f);
            hudRoot.anchorMax = new Vector2(0.5f, 0f);
            hudRoot.pivot = new Vector2(0.5f, 0.5f);

            CreateImage("Hotbar Shadow", hudRoot, new Vector2(panelWidth - 12f, 88f), new Vector2(4f, -4f), HexColor("#080909"));
            CreateImage("Hotbar Frame", hudRoot, new Vector2(panelWidth - 12f, 86f), Vector2.zero, HexColor("#756347"));
            CreateImage("Hotbar Backing", hudRoot, new Vector2(panelWidth - 18f, 80f), Vector2.zero, new Color(0.055f, 0.065f, 0.055f, 0.96f));

            for (int i = 0; i < SlotCount; i++)
                BuildSlot(hudRoot, i);
        }


        private void BuildSlot(RectTransform parent, int index)
        {
            float x = (index - (SlotCount - 1) * 0.5f) * 68f;
            GameObject slotObject = new GameObject("Hotbar Slot " + (index + 1), typeof(RectTransform), typeof(Image), typeof(EventTrigger));
            slotObject.transform.SetParent(parent, false);
            RectTransform slotRect = slotObject.GetComponent<RectTransform>();
            slotRect.anchorMin = new Vector2(0.5f, 0.5f);
            slotRect.anchorMax = new Vector2(0.5f, 0.5f);
            slotRect.pivot = new Vector2(0.5f, 0.5f);
            slotRect.sizeDelta = new Vector2(68f, 78f);
            slotRect.anchoredPosition = new Vector2(x, 0f);
            slotRects[index] = slotRect;

            Image border = slotObject.GetComponent<Image>();
            border.sprite = solidSprite;
            border.color = SlotIdle;
            border.raycastTarget = true;
            Image interior = CreateImage("Hotbar Slot Face", slotRect, new Vector2(62f, 72f), Vector2.zero, SlotInterior);
            CreateLabel(slotRect, "Hotbar Number", (index + 1).ToString(), new Vector2(-24f, 29f), new Vector2(13f, 14f), 9, TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);

            Image iconBase = CreateImage("Pixel Item Icon", slotRect, new Vector2(32f, 32f), Vector2.zero, Color.white);
            Image iconHighlight = CreateImage("Item Icon Light Pixel", slotRect, new Vector2(10f, 10f), new Vector2(-8f, 7f), Color.white);
            Image iconShadow = CreateImage("Item Icon Shadow Pixel", slotRect, new Vector2(10f, 9f), new Vector2(8f, -8f), Color.white);
            Image iconImage = CreateImage("Item Sprite", slotRect, new Vector2(36f, 36f), Vector2.zero, Color.white);
            iconImage.preserveAspect = true;
            Text itemName = CreateLabel(slotRect, "Component Name", string.Empty, new Vector2(0f, -28f), new Vector2(66f, 12f), 8, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            Text quantity = CreateLabel(slotRect, "Stack Count", string.Empty, new Vector2(20f, 22f), new Vector2(24f, 13f), 9, TextPrimary, TextAnchor.MiddleRight, FontStyle.Bold);

            slotViews[index] = new SlotView
            {
                border = border,
                interior = interior,
                iconImage = iconImage,
                iconBase = iconBase,
                iconHighlight = iconHighlight,
                iconShadow = iconShadow,
                itemName = itemName,
                quantity = quantity
            };

            EventTrigger eventTrigger = slotObject.GetComponent<EventTrigger>();
            eventTrigger.triggers = new List<EventTrigger.Entry>();
            AddTrigger(eventTrigger, EventTriggerType.PointerClick, data => HandleSlotClick(index, data));
            AddTrigger(eventTrigger, EventTriggerType.PointerEnter, data => HandlePointerEnter(index));
            AddTrigger(eventTrigger, EventTriggerType.PointerExit, data => HandlePointerExit(index));
            AddTrigger(eventTrigger, EventTriggerType.BeginDrag, data => BeginDrag(index, data));
            AddTrigger(eventTrigger, EventTriggerType.Drag, data => UpdateDrag(data));
            AddTrigger(eventTrigger, EventTriggerType.EndDrag, data => EndDrag(data));
        }

        private void HandleSlotClick(int index, BaseEventData data)
        {
            if (!(data is PointerEventData pointerData))
                return;

            if (pointerData.button == PointerEventData.InputButton.Right)
            {
                selectedIndex = index;
                UseItem(index);
                RefreshAll();
                return;
            }

            if (pointerData.button == PointerEventData.InputButton.Left)
                SelectSlot(index);
        }

        private void SelectSlot(int index)
        {
            if (!IsValidSlot(index))
                return;
            selectedIndex = index;
            InventoryItemData item = IsEmpty(items[index]) ? null : CopyItem(items[index]);
            RefreshAll();
            ItemSelected?.Invoke(index, item);
        }

        private void UseItem(int index)
        {
            if (!IsValidSlot(index) || IsEmpty(items[index]))
                return;

            InventoryItemData usedItem = CopyItem(items[index]);
            ItemUsed?.Invoke(usedItem);
            items[index].quantity--;
            if (items[index].quantity <= 0)
                items[index] = new InventoryItemData();
            InventoryChanged?.Invoke();
        }

        private void HandlePointerEnter(int index)
        {
            hoveredIndex = index;
            RefreshSlot(index);
        }

        private void HandlePointerExit(int index)
        {
            if (hoveredIndex == index)
                hoveredIndex = -1;
            RefreshSlot(index);
        }

        private void BeginDrag(int index, BaseEventData data)
        {
            if (!IsValidSlot(index) || IsEmpty(items[index]) || !(data is PointerEventData pointerData))
                return;

            draggingIndex = index;
            Image slotImage = slotViews[index].border;
            Color fadedColor = slotImage.color;
            fadedColor.a = 0.45f;
            slotImage.color = fadedColor;

            dragGhost = CreateImage("Inventory Drag Ghost", canvasRect, new Vector2(206f, 46f), Vector2.zero, HexColor("#6D5733"));
            dragGhost.rectTransform.position = pointerData.position;
            dragGhost.transform.SetAsLastSibling();
            dragGhostLabel = CreateLabel(dragGhost.rectTransform, "Dragged Item Label", string.Empty, Vector2.zero, new Vector2(190f, 38f), 15, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            UpdateDragLabel();
        }

        private void UpdateDrag(BaseEventData data)
        {
            if (dragGhost == null || !(data is PointerEventData pointerData))
                return;
            dragGhost.rectTransform.position = pointerData.position;
        }

        private void EndDrag(BaseEventData data)
        {
            if (draggingIndex < 0)
                return;

            int targetIndex = -1;
            if (data is PointerEventData pointerData)
            {
                for (int i = 0; i < slotViews.Length; i++)
                {
                    if (RectTransformUtility.RectangleContainsScreenPoint(slotViews[i].border.rectTransform, pointerData.position, null))
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }

            if (targetIndex >= 0 && targetIndex != draggingIndex)
            {
                InventoryItemData temp = items[targetIndex];
                items[targetIndex] = items[draggingIndex];
                items[draggingIndex] = temp;
                selectedIndex = targetIndex;
                InventoryChanged?.Invoke();
            }

            draggingIndex = -1;
            if (dragGhost != null)
                Destroy(dragGhost.gameObject);
            dragGhost = null;
            dragGhostLabel = null;
            RefreshAll();
        }

        private void UpdateDragLabel()
        {
            if (dragGhostLabel == null || draggingIndex < 0 || IsEmpty(items[draggingIndex]))
                return;
            InventoryItemData item = items[draggingIndex];
            dragGhostLabel.text = item.displayName + "   x" + item.quantity;
        }

        private void RefreshAll()
        {
            if (slotViews[0] == null)
                return;

            for (int i = 0; i < SlotCount; i++)
                RefreshSlot(i);
        }

        private void RefreshSlot(int index)
        {
            if (!IsValidSlot(index) || slotViews[index] == null)
                return;

            SlotView view = slotViews[index];
            bool selected = index == selectedIndex;
            bool hovered = index == hoveredIndex;
            view.border.color = selected ? SlotSelected : hovered ? SlotHover : SlotIdle;
            view.interior.color = selected ? HexColor("#51442F") : hovered ? HexColor("#494136") : SlotInterior;
            slotRects[index].sizeDelta = selected ? new Vector2(72f, 82f) : new Vector2(68f, 78f);

            InventoryItemData item = items[index];
            bool hasItem = !IsEmpty(item);
            if (view.itemName != null)
            {
                view.itemName.text = hasItem ? GetHotbarLabel(item) : "EMPTY";
                view.itemName.color = hasItem ? TextPrimary : HexColor("#8C8272");
            }
            view.quantity.text = hasItem ? item.quantity.ToString() : string.Empty;
            view.iconImage.enabled = hasItem && item.icon != null;
            if (view.iconImage.enabled)
            {
                view.iconImage.sprite = item.icon;
                view.iconImage.color = Color.white;
            }

            bool showGeneratedIcon = hasItem && item.icon == null;
            view.iconBase.enabled = showGeneratedIcon;
            view.iconHighlight.enabled = showGeneratedIcon;
            view.iconShadow.enabled = showGeneratedIcon;
            if (showGeneratedIcon)
            {
                view.iconBase.color = item.iconTint;
                view.iconHighlight.color = Color.Lerp(item.iconTint, Color.white, 0.34f);
                view.iconShadow.color = Color.Lerp(item.iconTint, Color.black, 0.38f);
            }
        }

        private Image CreateImage(string objectName, RectTransform parent, Vector2 size, Vector2 position, Color color)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = imageObject.GetComponent<Image>();
            image.sprite = solidSprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text CreateLabel(RectTransform parent, string objectName, string value, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor alignment, FontStyle style)
        {
            GameObject labelObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(parent, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text label = labelObject.GetComponent<Text>();
            label.font = uiFont;
            label.text = value;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform CreateRect(string objectName, RectTransform parent, Vector2 size, Vector2 position)
        {
            GameObject rectObject = new GameObject(objectName, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            RectTransform rect = rectObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType eventType, Action<BaseEventData> callback)
        {
            EventTrigger.Entry entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(data => callback(data));
            trigger.triggers.Add(entry);
        }

        private static string GetHotbarLabel(InventoryItemData item)
        {
            switch (item.id)
            {
                case "pvc_pipe": return "PVC PIPE";
                case "elbow_90": return "90° ELBOW";
                case "ball_valve": return "BALL VALVE";
                case "outlet_tap": return "OUTLET TAP";
                case "pressure_gauge": return "PRESSURE";
                default: return item.displayName.ToUpperInvariant();
            }
        }


        private static bool IsValidSlot(int index)
        {
            return index >= 0 && index < SlotCount;
        }

        private static bool IsEmpty(InventoryItemData item)
        {
            return item == null || string.IsNullOrWhiteSpace(item.id) || item.quantity <= 0;
        }

        private static InventoryItemData CopyItem(InventoryItemData item)
        {
            if (item == null)
                return new InventoryItemData();
            return new InventoryItemData(item.id, item.displayName, item.quantity, item.iconTint, item.icon);
        }

        private static Color HexColor(string html)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(html, out color) ? color : Color.white;
        }
    }
}


