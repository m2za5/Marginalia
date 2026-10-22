using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory))]
public sealed class PlayerPurchaseService : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private Health health;
    [SerializeField] private PlayerCombatInk combatInk;

    public event Action<int> PurchaseCompleted;
    public event Action<int> PurchaseFailed;

    public PlayerInventory Inventory => inventory;
    public int Currency => inventory != null ? inventory.Currency : 0;

    private void Awake()
    {
        ResolveReferences();
    }

    public bool CanAfford(int price)
    {
        ResolveReferences();
        return inventory != null && inventory.CanAfford(price);
    }

    public bool TryPurchaseItem(InventoryItemDefinition definition, int quantity, int price)
    {
        ResolveReferences();

        if (definition == null
            || quantity <= 0
            || !definition.IsConfigured
            || inventory.GetQuantity(definition) + quantity > definition.MaxStack)
        {
            return Fail(price);
        }

        return TryPurchase(price, () => inventory.TryAdd(definition, quantity));
    }

    public bool TryPurchaseMaxHealth(int increase, int price, bool healAddedHealth = true)
    {
        ResolveReferences();

        if (health == null || increase <= 0)
        {
            return Fail(price);
        }

        return TryPurchase(price, () => health.IncreaseMaxHealth(increase, healAddedHealth));
    }

    public bool TryPurchaseMaxSkillPoints(int increase, int price, bool restoreAddedPoints = true)
    {
        ResolveReferences();

        if (combatInk == null || increase <= 0)
        {
            return Fail(price);
        }

        return TryPurchase(price, () => combatInk.IncreaseMaxCharges(increase, restoreAddedPoints));
    }

    public bool TryPurchase(int price, Func<bool> tryGrantReward)
    {
        ResolveReferences();

        if (inventory == null || !inventory.TryPurchase(price, tryGrantReward))
        {
            return Fail(price);
        }

        GunHoSfxBus.Play(GunHoSfxCue.PurchaseSuccess);
        PurchaseCompleted?.Invoke(price);
        return true;
    }

    private bool Fail(int price)
    {
        PurchaseFailed?.Invoke(Mathf.Max(0, price));
        return false;
    }

    private void ResolveReferences()
    {
        inventory = inventory != null ? inventory : GetComponent<PlayerInventory>();
        health = health != null ? health : GetComponent<Health>();
        combatInk = combatInk != null ? combatInk : GetComponent<PlayerCombatInk>();
    }
}
