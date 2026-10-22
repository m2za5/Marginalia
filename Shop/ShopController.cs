using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

public enum ShopPurchaseResult
{
    Success,
    NotEnoughCurrency,
    OutOfStock,
    InventoryFull,
    InvalidEntry,
    ServiceUnavailable,
    Rejected
}

[DisallowMultipleComponent]
public sealed class ShopController : MonoBehaviour
{
    [SerializeField] private ShopUIView view;
    [SerializeField] private PlayerPurchaseService purchaseService;
    [SerializeField] private ShopCameraFocus cameraFocus;

    [SerializeField] private bool closeWithEscape = true;
    [SerializeField] private bool resetStockOnClose;

    [Header("Messages")]
    [SerializeField] private string messagePurchased = "구매했습니다.";
    [SerializeField] private string messageNotEnoughCurrency = "소지금이 부족합니다.";
    [SerializeField] private string messageOutOfStock = "품절입니다.";
    [SerializeField] private string messageInventoryFull = "더 이상 가질 수 없습니다.";
    [SerializeField] private string messageFailed = "구매할 수 없습니다.";

    private readonly List<ShopItemEntry> visibleEntries = new List<ShopItemEntry>();
    // Entries without a stable stock key (empty/duplicate id) keep the old per-controller, unsaved stock.
    private readonly Dictionary<ShopItemEntry, int> remainingStock = new Dictionary<ShopItemEntry, int>();
    private readonly HashSet<string> invalidEntryIds = new HashSet<string>();
    private bool shopIdValid;

    // Stock for keyed entries lives in SaveManager.CurrentSaveData (saved with SaveGame).
    // Without a SaveManager it falls back to this per-session store so it still survives scene reloads.
    private static readonly Dictionary<string, int> sessionStock = new Dictionary<string, int>();
    private static readonly Dictionary<string, ShopInventory> knownShopIds = new Dictionary<string, ShopInventory>();

    private ShopInventory currentInventory;
    private ShopItemEntry selectedEntry;
    private PlayerInventory playerInventory;
    private PlayerController playerController;
    private GameStateManager subscribedGameStateManager;
    private GameState previousGameState = GameState.Playing;
    private float previousTimeScale = 1f;
    private bool viewEventsBound;
    private int currentPage;

    public static ShopController Instance { get; private set; }
    public static bool IsAnyOpen => Instance != null && Instance.IsOpen;

    public bool IsOpen { get; private set; }
    public ShopInventory CurrentInventory => currentInventory;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[ShopController] {name}: 씬에 ShopController가 둘 이상. " +
                             "이 인스턴스는 비활성화.", this);
            enabled = false;
            return;
        }

        Instance = this;
        ResolveReferences();
        BindViewEvents();
        SetCanvasVisible(false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindViewEvents();
        RefreshGameStateSubscription();
    }

    private void Update()
    {
        if (!IsOpen) return;

        RefreshGameStateSubscription();

        if (!closeWithEscape) return;

        // When the player owns the Pause action, ESC reaches the shop only through
        // PlayerController.RequestContextualCancelOrPause, so one press is handled once.
        if (playerController != null && playerController.HandlesContextualCancelInput) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromPlayerInventory();
        UnsubscribeFromGameStateManager();

        if (IsOpen) Close(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool Open(ShopInventory inventory) => Open(inventory, null);

    public bool Open(ShopInventory inventory, Component owner)
    {
        if (inventory == null || view == null || IsOpen) return false;

        ResolveReferences();

        if (purchaseService == null)
        {
            Debug.LogError("[ShopController] PlayerPurchaseService를 찾지 못했다. 상점을 열 수 없다.", this);
            return false;
        }

        GameStateManager manager = GameStateManager.Instance;
        if (manager != null && !manager.IsState(GameState.Playing)) return false;
        PlayerInventoryUI.TryCloseAny(false);

        currentInventory = inventory;
        selectedEntry = null;
        currentPage = 0;
        ValidateStockIds();
        BuildVisibleEntries();

        playerController?.CancelDrawingSkill(DrawingCancelReason.StateChanged);
        playerController?.SetLifecycleInputBlocked(true);

        previousTimeScale = Time.timeScale;
        IsOpen = true;
        SetCanvasVisible(true);
        SubscribeToPlayerInventory();
        SetMessage(string.Empty);
        view.ShowList();
        Refresh();

        if (manager != null)
        {
            previousGameState = manager.CurrentState;
            manager.SetPaused();
        }
        else
        {
            Time.timeScale = 0f;
        }

        if (cameraFocus != null && owner != null)
        {
            cameraFocus.Focus(owner);
        }

        GunHoSfxBus.Play(GunHoSfxCue.UiOpen);

        return true;
    }

    public bool Close()
    {
        bool didClose = Close(true);
        if (didClose) GunHoSfxBus.Play(GunHoSfxCue.UiClose);
        return didClose;
    }

    private bool Close(bool restorePreviousState)
    {
        if (!IsOpen) return false;

        IsOpen = false;
        SetCanvasVisible(false);
        if (cameraFocus != null) cameraFocus.Release();
        UnsubscribeFromPlayerInventory();
        playerController?.SetLifecycleInputBlocked(false);

        if (resetStockOnClose) ResetStock();

        currentInventory = null;
        selectedEntry = null;
        visibleEntries.Clear();
        currentPage = 0;

        GameStateManager manager = GameStateManager.Instance;

        if (restorePreviousState && manager != null
            && manager.IsState(GameState.Paused)
            && previousGameState == GameState.Playing)
        {
            manager.ChangeState(previousGameState);
        }
        else if (restorePreviousState && manager == null)
        {
            Time.timeScale = previousTimeScale;
        }

        return true;
    }

    public static bool TryCloseAny()
    {
        return Instance != null && Instance.Close();
    }

    // Cancel-input entry point for PlayerController. Consumes the press while the shop is open.
    public static bool TryHandleCancelInput()
    {
        if (Instance == null || !Instance.IsOpen) return false;

        if (Instance.closeWithEscape) Instance.Close();
        return true;
    }

    public ShopPurchaseResult TryPurchaseSelected()
    {
        return TryPurchase(selectedEntry);
    }

    public ShopPurchaseResult TryPurchase(ShopItemEntry entry)
    {
        ShopPurchaseResult result = Evaluate(entry);

        if (result != ShopPurchaseResult.Success)
        {
            ShowResultMessage(result);
            return result;
        }

        bool granted = entry.Kind switch
        {
            ShopItemKind.InventoryItem =>
                purchaseService.TryPurchaseItem(entry.ItemDefinition, entry.Quantity, entry.Price),
            ShopItemKind.MaxHealth =>
                purchaseService.TryPurchaseMaxHealth(entry.StatIncrease, entry.Price),
            ShopItemKind.MaxSkillPoints =>
                purchaseService.TryPurchaseMaxSkillPoints(entry.StatIncrease, entry.Price),
            _ => false
        };

        if (!granted)
        {
            ShowResultMessage(ShopPurchaseResult.Rejected);
            return ShopPurchaseResult.Rejected;
        }

        ConsumeStock(entry);
        ShowResultMessage(ShopPurchaseResult.Success);
        Refresh();
        return ShopPurchaseResult.Success;
    }

    public ShopPurchaseResult Evaluate(ShopItemEntry entry)
    {
        if (entry == null || !entry.IsConfigured) return ShopPurchaseResult.InvalidEntry;
        if (purchaseService == null) return ShopPurchaseResult.ServiceUnavailable;
        if (GetRemainingStock(entry) == 0) return ShopPurchaseResult.OutOfStock;
        if (!purchaseService.CanAfford(entry.Price)) return ShopPurchaseResult.NotEnoughCurrency;

        if (entry.Kind == ShopItemKind.InventoryItem)
        {
            InventoryItemDefinition definition = entry.ItemDefinition;
            PlayerInventory inventory = purchaseService.Inventory;

            if (inventory == null) return ShopPurchaseResult.ServiceUnavailable;

            if (inventory.GetQuantity(definition) + entry.Quantity > definition.MaxStack)
            {
                return ShopPurchaseResult.InventoryFull;
            }
        }

        return ShopPurchaseResult.Success;
    }

    public int GetRemainingStock(ShopItemEntry entry)
    {
        if (entry == null || !entry.HasLimitedStock) return -1;

        if (TryGetStockKey(entry, out string shopId, out string entryId))
        {
            SaveManager saveManager = SaveManager.Instance;

            if (saveManager != null)
            {
                return saveManager.TryGetShopStock(shopId, entryId, out int saved)
                    ? Mathf.Max(0, saved)
                    : entry.InitialStock;
            }

            return sessionStock.TryGetValue(StockKey(shopId, entryId), out int session)
                ? session
                : entry.InitialStock;
        }

        return remainingStock.TryGetValue(entry, out int left) ? left : entry.InitialStock;
    }

    private void ConsumeStock(ShopItemEntry entry)
    {
        if (entry == null || !entry.HasLimitedStock) return;

        int left = Mathf.Max(0, GetRemainingStock(entry) - 1);

        if (TryGetStockKey(entry, out string shopId, out string entryId))
        {
            SaveManager saveManager = SaveManager.Instance;

            if (saveManager != null) saveManager.SetShopStock(shopId, entryId, left);
            else sessionStock[StockKey(shopId, entryId)] = left;

            return;
        }

        remainingStock[entry] = left;
    }

    // Resets the current shop's stock to the asset's initial values.
    public void ResetStock()
    {
        remainingStock.Clear();

        if (currentInventory == null || !shopIdValid) return;

        string shopId = currentInventory.ShopId;
        SaveManager.Instance?.ClearShopStocks(shopId);

        string prefix = StockKey(shopId, string.Empty);
        List<string> keys = new List<string>(sessionStock.Keys);
        foreach (string key in keys)
        {
            if (key.StartsWith(prefix, System.StringComparison.Ordinal)) sessionStock.Remove(key);
        }
    }

    private bool TryGetStockKey(ShopItemEntry entry, out string shopId, out string entryId)
    {
        shopId = currentInventory != null ? currentInventory.ShopId : null;
        entryId = entry != null ? entry.EntryId : null;

        if (!shopIdValid
            || string.IsNullOrWhiteSpace(entryId)
            || invalidEntryIds.Contains(entryId)
            || currentInventory.Entries == null)
        {
            return false;
        }

        foreach (ShopItemEntry candidate in currentInventory.Entries)
        {
            if (candidate == entry) return true;
        }

        return false;
    }

    private static string StockKey(string shopId, string entryId) => shopId + "/" + entryId;

    private void ValidateStockIds()
    {
        invalidEntryIds.Clear();
        shopIdValid = false;

        if (currentInventory == null) return;

        string shopId = currentInventory.ShopId;
        bool hasLimitedStock = false;
        HashSet<string> seenEntryIds = new HashSet<string>();

        if (currentInventory.Entries != null)
        {
            foreach (ShopItemEntry entry in currentInventory.Entries)
            {
                if (entry == null || !entry.HasLimitedStock) continue;

                hasLimitedStock = true;

                if (string.IsNullOrWhiteSpace(entry.EntryId))
                {
                    Debug.LogWarning($"[ShopController] '{currentInventory.name}': limited entry without entryId. " +
                                     "Its stock is not saved.", currentInventory);
                }
                else if (!seenEntryIds.Add(entry.EntryId))
                {
                    invalidEntryIds.Add(entry.EntryId);
                    Debug.LogError($"[ShopController] '{currentInventory.name}': duplicate entryId '{entry.EntryId}'. " +
                                   "Its stock is not saved.", currentInventory);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(shopId))
        {
            if (hasLimitedStock)
            {
                Debug.LogWarning($"[ShopController] '{currentInventory.name}': shopId is empty. " +
                                 "Limited stock is not saved.", currentInventory);
            }

            return;
        }

        if (knownShopIds.TryGetValue(shopId, out ShopInventory owner) && owner != null && owner != currentInventory)
        {
            Debug.LogError($"[ShopController] shopId '{shopId}' is used by both '{owner.name}' and " +
                           $"'{currentInventory.name}'. Stock of '{currentInventory.name}' is not saved.", currentInventory);
            return;
        }

        knownShopIds[shopId] = currentInventory;
        shopIdValid = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticStock()
    {
        sessionStock.Clear();
        knownShopIds.Clear();
    }


    public void Refresh()
    {
        if (view == null || currentInventory == null) return;

        if (view.TitleText != null) view.TitleText.text = currentInventory.ShopDisplayName;
        if (view.CurrencyText != null) view.CurrencyText.text = CurrentCurrency().ToString("N0");

        if (selectedEntry != null && !visibleEntries.Contains(selectedEntry))
        {
            selectedEntry = null;
        }

        IReadOnlyList<ShopSlotView> slots = view.Slots;
        ClampPage();
        int pageStart = currentPage * PageSize;

        for (int i = 0; i < slots.Count; i++)
        {
            ShopSlotView slot = slots[i];
            if (slot == null) continue;

            int entryIndex = pageStart + i;
            ShopItemEntry entry = entryIndex < visibleEntries.Count ? visibleEntries[entryIndex] : null;
            bool hasEntry = entry != null;
            bool soldOut = hasEntry && GetRemainingStock(entry) == 0;

            if (slot.Button != null) slot.Button.gameObject.SetActive(hasEntry);

            if (slot.Button != null)
            {
                slot.Button.interactable = hasEntry && !soldOut;

                if (slot.Button.image != null)
                {
                    slot.Button.image.color = !hasEntry
                        ? view.NormalSlotColor
                        : soldOut
                            ? view.SoldOutColor
                            : entry == selectedEntry
                                ? view.SelectedColor
                                : view.NormalSlotColor;
                }
            }

            if (slot.Label != null)
            {
                slot.Label.text = !hasEntry
                    ? string.Empty
                    : entry.Quantity > 1
                        ? $"{entry.DisplayName} ×{entry.Quantity}"
                        : entry.DisplayName;
            }

            if (slot.PriceLabel != null)
            {
                slot.PriceLabel.text = !hasEntry
                    ? string.Empty
                    : soldOut
                        ? messageOutOfStock
                        : entry.Price.ToString("N0");
            }

            if (slot.Icon != null)
            {
                slot.Icon.sprite = hasEntry ? entry.Icon : null;
                slot.Icon.preserveAspect = true;
                slot.Icon.enabled = slot.Icon.sprite != null;
            }
        }

        RefreshPageButtons();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        bool soldOut = selectedEntry != null && GetRemainingStock(selectedEntry) == 0;

        if (view.PreviewNameText != null)
            view.PreviewNameText.text = selectedEntry != null ? selectedEntry.DisplayName : string.Empty;

        if (view.PreviewDescriptionText != null)
            view.PreviewDescriptionText.text = selectedEntry != null ? selectedEntry.Description : string.Empty;

        if (view.PreviewPriceText != null)
            view.PreviewPriceText.text = selectedEntry == null
                ? string.Empty
                : soldOut
                    ? messageOutOfStock
                    : selectedEntry.Price.ToString("N0");

        if (view.PreviewIcon != null)
        {
            view.PreviewIcon.sprite = selectedEntry != null ? selectedEntry.Icon : null;
            view.PreviewIcon.preserveAspect = true;
            view.PreviewIcon.enabled = view.PreviewIcon.sprite != null;
        }

        if (view.BuyButton != null)
        {
            view.BuyButton.interactable =
                selectedEntry != null && Evaluate(selectedEntry) == ShopPurchaseResult.Success;
        }
    }

    private void SelectVisibleEntry(int slotIndex)
    {
        int index = currentPage * PageSize + slotIndex;

        if (slotIndex < 0 || slotIndex >= PageSize
            || index < 0 || index >= visibleEntries.Count)
        {
            return;
        }

        bool hasChanged = selectedEntry != visibleEntries[index];

        selectedEntry = visibleEntries[index];
        SetMessage(string.Empty);
        view.ShowDetail();
        Refresh();

        if (IsOpen && hasChanged)
        {
            GunHoSfxBus.Play(GunHoSfxCue.UiConfirm);
        }
    }

    private int PageSize => view != null ? Mathf.Max(1, view.Slots.Count) : 1;

    private int PageCount => Mathf.Max(1, (visibleEntries.Count + PageSize - 1) / PageSize);

    private void ClampPage()
    {
        currentPage = Mathf.Clamp(currentPage, 0, PageCount - 1);
    }

    private void ChangePage(int delta)
    {
        if (!IsOpen) return;

        int next = Mathf.Clamp(currentPage + delta, 0, PageCount - 1);
        if (next == currentPage) return;

        currentPage = next;
        selectedEntry = null;
        SetMessage(string.Empty);
        view.ShowList();
        Refresh();
    }

    private void PreviousPage() => ChangePage(-1);

    private void NextPage() => ChangePage(1);

    private void RefreshPageButtons()
    {
        if (view.PreviousPageButton != null) view.PreviousPageButton.interactable = currentPage > 0;
        if (view.NextPageButton != null) view.NextPageButton.interactable = currentPage < PageCount - 1;
    }

    private void BackToList()
    {
        bool hadSelection = selectedEntry != null;
        selectedEntry = null;
        SetMessage(string.Empty);
        view.ShowList();
        Refresh();
        if (IsOpen && hadSelection) GunHoSfxBus.Play(GunHoSfxCue.UiBack);
    }

    private void BuildVisibleEntries()
    {
        visibleEntries.Clear();
        if (currentInventory?.Entries == null) return;

        foreach (ShopItemEntry entry in currentInventory.Entries)
        {
            if (entry != null && entry.IsConfigured) visibleEntries.Add(entry);
        }
    }

    private void ShowResultMessage(ShopPurchaseResult result)
    {
        SetMessage(result switch
        {
            ShopPurchaseResult.Success => messagePurchased,
            ShopPurchaseResult.NotEnoughCurrency => messageNotEnoughCurrency,
            ShopPurchaseResult.OutOfStock => messageOutOfStock,
            ShopPurchaseResult.InventoryFull => messageInventoryFull,
            _ => messageFailed
        });
    }

    private void SetMessage(string message)
    {
        if (view != null && view.MessageText != null) view.MessageText.text = message;
    }

    private int CurrentCurrency() => purchaseService != null ? purchaseService.Currency : 0;

    private void SetCanvasVisible(bool visible)
    {
        if (view == null) return;

        if (view.Canvas != null) view.Canvas.enabled = visible;

        if (view.CanvasGroup != null)
        {
            view.CanvasGroup.alpha = visible ? 1f : 0f;
            view.CanvasGroup.interactable = visible;
            view.CanvasGroup.blocksRaycasts = visible;
        }
    }

    private void ResolveReferences()
    {
        if (view == null) view = GetComponentInChildren<ShopUIView>(true);
        if (cameraFocus == null) cameraFocus = GetComponent<ShopCameraFocus>();

        if (purchaseService == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) purchaseService = player.GetComponent<PlayerPurchaseService>();
        }

        if (purchaseService != null)
        {
            playerInventory = purchaseService.Inventory;
            playerController = purchaseService.GetComponent<PlayerController>();
        }
    }

    private void BindViewEvents()
    {
        if (viewEventsBound || view == null) return;

        if (view.CloseButton != null) view.CloseButton.onClick.AddListener(CloseFromButton);
        if (view.BuyButton != null) view.BuyButton.onClick.AddListener(BuyFromButton);
        if (view.BackButton != null) view.BackButton.onClick.AddListener(BackToList);
        if (view.PreviousPageButton != null) view.PreviousPageButton.onClick.AddListener(PreviousPage);
        if (view.NextPageButton != null) view.NextPageButton.onClick.AddListener(NextPage);

        IReadOnlyList<ShopSlotView> slots = view.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i]?.Button == null) continue;
            int slotIndex = i;
            slots[i].Button.onClick.AddListener(() => SelectVisibleEntry(slotIndex));
        }

        viewEventsBound = true;
        EnsureEventSystem();
    }

    private void CloseFromButton() => Close();

    private void BuyFromButton() => TryPurchaseSelected();

    private void SubscribeToPlayerInventory()
    {
        if (playerInventory == null) return;
        playerInventory.Changed -= Refresh;
        playerInventory.Changed += Refresh;
    }

    private void UnsubscribeFromPlayerInventory()
    {
        if (playerInventory != null) playerInventory.Changed -= Refresh;
    }

    private void RefreshGameStateSubscription()
    {
        GameStateManager manager = GameStateManager.Instance;
        if (subscribedGameStateManager == manager) return;

        UnsubscribeFromGameStateManager();
        subscribedGameStateManager = manager;

        if (subscribedGameStateManager != null)
        {
            subscribedGameStateManager.OnGameStateChanged += HandleGameStateChanged;
        }
    }

    private void UnsubscribeFromGameStateManager()
    {
        if (subscribedGameStateManager == null) return;
        subscribedGameStateManager.OnGameStateChanged -= HandleGameStateChanged;
        subscribedGameStateManager = null;
    }

    private void HandleGameStateChanged(GameState previousState, GameState currentState)
    {
        if (IsOpen && currentState != GameState.Paused) Close(false);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        GameObject eventSystemObject = new GameObject(
            "ShopEventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        Object.DontDestroyOnLoad(eventSystemObject);
    }
}