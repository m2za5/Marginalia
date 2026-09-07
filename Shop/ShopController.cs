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

    [SerializeField] private bool closeWithEscape = true;
    [SerializeField] private bool resetStockOnClose;

    [Header("Messages")]
    [SerializeField] private string messagePurchased = "구매했습니다.";
    [SerializeField] private string messageNotEnoughCurrency = "소지금이 부족합니다.";
    [SerializeField] private string messageOutOfStock = "품절입니다.";
    [SerializeField] private string messageInventoryFull = "더 이상 가질 수 없습니다.";
    [SerializeField] private string messageFailed = "구매할 수 없습니다.";

    private readonly List<ShopItemEntry> visibleEntries = new List<ShopItemEntry>();
    private readonly Dictionary<ShopItemEntry, int> remainingStock = new Dictionary<ShopItemEntry, int>();

    private ShopInventory currentInventory;
    private ShopItemEntry selectedEntry;
    private PlayerInventory playerInventory;
    private PlayerController playerController;
    private GameStateManager subscribedGameStateManager;
    private GameState previousGameState = GameState.Playing;
    private float previousTimeScale = 1f;
    private bool viewEventsBound;

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

    public bool Open(ShopInventory inventory)
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
        PlayerInventoryUI.TryCloseAny();

        currentInventory = inventory;
        selectedEntry = null;
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

        return true;
    }

    public bool Close() => Close(true);

    private bool Close(bool restorePreviousState)
    {
        if (!IsOpen) return false;

        IsOpen = false;
        SetCanvasVisible(false);
        UnsubscribeFromPlayerInventory();
        playerController?.SetLifecycleInputBlocked(false);

        if (resetStockOnClose) remainingStock.Clear();

        currentInventory = null;
        selectedEntry = null;
        visibleEntries.Clear();

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
        return remainingStock.TryGetValue(entry, out int left) ? left : entry.InitialStock;
    }

    private void ConsumeStock(ShopItemEntry entry)
    {
        if (entry == null || !entry.HasLimitedStock) return;

        int left = GetRemainingStock(entry);
        remainingStock[entry] = Mathf.Max(0, left - 1);
    }

    public void ResetStock() => remainingStock.Clear();


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

        for (int i = 0; i < slots.Count; i++)
        {
            ShopSlotView slot = slots[i];
            if (slot == null) continue;

            ShopItemEntry entry = i < visibleEntries.Count ? visibleEntries[i] : null;
            bool hasEntry = entry != null;
            bool soldOut = hasEntry && GetRemainingStock(entry) == 0;

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
                slot.Icon.enabled = slot.Icon.sprite != null;
            }
        }

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
            view.PreviewIcon.enabled = view.PreviewIcon.sprite != null;
        }

        if (view.BuyButton != null)
        {
            view.BuyButton.interactable =
                selectedEntry != null && Evaluate(selectedEntry) == ShopPurchaseResult.Success;
        }
    }

    private void SelectVisibleEntry(int index)
    {
        if (index < 0 || index >= visibleEntries.Count) return;
        selectedEntry = visibleEntries[index];
        SetMessage(string.Empty);
        view.ShowDetail();
        Refresh();
    }

    private void BackToList()
    {
        selectedEntry = null;
        SetMessage(string.Empty);
        view.ShowList();
        Refresh();
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