using System;
using System.Collections.Generic;
using UnityEngine;

public enum ShopItemKind
{
    InventoryItem = 0,
    MaxHealth = 1,
    MaxSkillPoints = 2
}

[Serializable]
public sealed class ShopItemEntry
{
    [Tooltip("Stable save key for this entry's limited stock. Unique within the shop; never reuse or regenerate.")]
    [SerializeField] private string entryId = "";
    [SerializeField] private ShopItemKind kind = ShopItemKind.InventoryItem;
    [SerializeField] private InventoryItemDefinition itemDefinition;
    [SerializeField, Min(1)] private int quantity = 1;
    [SerializeField, Min(1)] private int statIncrease = 1;
    [SerializeField] private string statDisplayName = "";
    [SerializeField, TextArea] private string statDescription = "";
    [Tooltip("Shop icon for this entry. Shown in the slot ItemIcon and the detail preview. " +
             "If empty on an InventoryItem entry, the item definition icon is used.")]
    [SerializeField] private Sprite statIcon;
    [SerializeField, Min(0)] private int price;
    [SerializeField, Min(-1)] private int stock = -1;

    public string EntryId => entryId;
    public ShopItemKind Kind => kind;
    public InventoryItemDefinition ItemDefinition => itemDefinition;
    public int Quantity => Mathf.Max(1, quantity);
    public int StatIncrease => Mathf.Max(1, statIncrease);
    public int Price => Mathf.Max(0, price);
    public int InitialStock => stock;
    public bool HasLimitedStock => stock >= 0;

    public bool IsInventoryItem => kind == ShopItemKind.InventoryItem;

    public string DisplayName => IsInventoryItem
        ? (itemDefinition != null ? itemDefinition.DisplayName : string.Empty)
        : statDisplayName;

    public string Description => IsInventoryItem
        ? (itemDefinition != null ? itemDefinition.Description : string.Empty)
        : statDescription;

    public Sprite Icon => statIcon != null
        ? statIcon
        : IsInventoryItem && itemDefinition != null ? itemDefinition.Icon : null;
    public bool IsConfigured =>
        IsInventoryItem
            ? itemDefinition != null && itemDefinition.IsConfigured
            : !string.IsNullOrWhiteSpace(statDisplayName);
}

[CreateAssetMenu(
    menuName = "Marginalia/Shop/Shop Inventory",
    fileName = "ShopInventory")]
public sealed class ShopInventory : ScriptableObject
{
    [SerializeField] private string shopDisplayName = "상점";
    [Tooltip("Stable save key for this shop's limited stock. Unique across shops; never reuse or regenerate.")]
    [SerializeField] private string shopId = "";
    [SerializeField] private ShopItemEntry[] entries = Array.Empty<ShopItemEntry>();

    public string ShopDisplayName => shopDisplayName;
    public string ShopId => shopId;
    public IReadOnlyList<ShopItemEntry> Entries => entries;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (entries == null) return;

        ValidateStockIds();

        for (int i = 0; i < entries.Length; i++)
        {
            ShopItemEntry entry = entries[i];
            if (entry == null) continue;

            if (entry.IsConfigured) continue;

            if (entry.IsInventoryItem)
            {
                Debug.LogWarning(
                    $"[{name}] entries[{i}]: InventoryItem인데 itemDefinition이 비었거나 " +
                    "정의가 불완전. 이 칸은 진열X.", this);
            }
            else
            {
                Debug.LogWarning(
                    $"[{name}] entries[{i}]: 능력치 상품인데 statDisplayName X. " +
                    "이 칸은 진열X.", this);
            }
        }
    }

    private void ValidateStockIds()
    {
        bool hasLimitedStock = false;
        HashSet<string> seenEntryIds = new HashSet<string>();

        for (int i = 0; i < entries.Length; i++)
        {
            ShopItemEntry entry = entries[i];
            if (entry == null || !entry.HasLimitedStock) continue;

            hasLimitedStock = true;

            if (string.IsNullOrWhiteSpace(entry.EntryId))
            {
                Debug.LogWarning($"[{name}] entries[{i}]: entryId is empty. Limited stock will not be saved.", this);
            }
            else if (!seenEntryIds.Add(entry.EntryId))
            {
                Debug.LogError($"[{name}] entries[{i}]: duplicate entryId '{entry.EntryId}'. Stock will not be saved.", this);
            }
        }

        if (hasLimitedStock && string.IsNullOrWhiteSpace(shopId))
        {
            Debug.LogWarning($"[{name}] shopId is empty. Limited stock will not be saved.", this);
        }
    }
#endif
}