using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class ShopDebugOpener : MonoBehaviour
{
    [SerializeField] private ShopInventory shopInventory;
    [SerializeField] private Key openKey = Key.F9;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (!keyboard[openKey].wasPressedThisFrame) return;

        if (ShopController.Instance == null)
        {
            Debug.LogError("[ShopDebugOpener] 씬에 ShopController X.", this);
            return;
        }

        if (ShopController.Instance.IsOpen)
        {
            ShopController.Instance.Close();
            return;
        }

        if (shopInventory == null)
        {
            Debug.LogError("[ShopDebugOpener] shopInventory 미할당.", this);
            return;
        }

        if (!ShopController.Instance.Open(shopInventory))
        {
            Debug.LogWarning(
                "[ShopDebugOpener] 상점을 열지 X. " +
                $"GameState={GameStateManager.Instance?.CurrentState.ToString() ?? "매니저 X"} " +
                "(Playing이 아니면 X)", this);
        }
    }
}