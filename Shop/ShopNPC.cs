using System;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class ShopNPC : MonoBehaviour
{

    [Serializable]
    public sealed class ShopOpenTrigger
    {
        [SerializeField] private string lineId;
        [SerializeField, Min(0)] private int choiceIndex;

        public string LineId => lineId;
        public int ChoiceIndex => choiceIndex;

        public bool Matches(string id, int index) =>
            !string.IsNullOrEmpty(lineId) && lineId == id && choiceIndex == index;
    }
    [SerializeField] private ShopInventory shopInventory;
    [SerializeField] private Dialogue greetingDialogue;
    [SerializeField] private ShopOpenTrigger[] openTriggers = Array.Empty<ShopOpenTrigger>();
    [SerializeField] private bool openWhenDialogueEnds;
    [SerializeField] private UnityEvent onShopOpened = new UnityEvent();
    [SerializeField] private UnityEvent onShopOpenFailed = new UnityEvent();

    private bool pendingOpen;
    private bool subscribed;

    public ShopInventory ShopInventory => shopInventory;
    public UnityEvent OnShopOpened => onShopOpened;
    public UnityEvent OnShopOpenFailed => onShopOpenFailed;

    private void OnEnable() => TrySubscribe();

    private void Start()
    {
        if (!subscribed && !TrySubscribe())
        {
            Debug.LogWarning($"[ShopNPC] {name}: DialogueManager.Instance가 X. " +
                             "대화로 상점을 열 수 X.", this);
        }
    }

    private void OnDisable() => Unsubscribe();
    private void OnDestroy() => Unsubscribe();

    public void OpenShop()
    {
        pendingOpen = false;

        if (shopInventory == null)
        {
            Debug.LogError($"[ShopNPC] {name}: shopInventory 미할당.", this);
            onShopOpenFailed?.Invoke();
            return;
        }

        if (ShopController.Instance == null)
        {
            Debug.LogError($"[ShopNPC] {name}: 씬에 ShopController가 X.", this);
            onShopOpenFailed?.Invoke();
            return;
        }

        if (ShopController.Instance.Open(shopInventory)) onShopOpened?.Invoke();
        else onShopOpenFailed?.Invoke();
    }

    private bool TrySubscribe()
    {
        if (subscribed) return true;

        DialogueManager manager = DialogueManager.Instance;
        if (manager == null) return false;

        manager.OnChoiceSelected += HandleChoiceSelected;
        manager.OnDialogueEnded += HandleDialogueEnded;
        subscribed = true;
        return true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;

        DialogueManager manager = DialogueManager.Instance;
        if (manager != null)
        {
            manager.OnChoiceSelected -= HandleChoiceSelected;
            manager.OnDialogueEnded -= HandleDialogueEnded;
        }

        subscribed = false;
        pendingOpen = false;
    }

    private void HandleChoiceSelected(Dialogue dialogue, int fromLineIndex, int choiceIndex)
    {
        if (greetingDialogue == null || dialogue != greetingDialogue) return;
        if (openTriggers == null || openTriggers.Length == 0) return;

        string lineId = ResolveLineId(dialogue, fromLineIndex);
        if (string.IsNullOrEmpty(lineId)) return;

        foreach (ShopOpenTrigger trigger in openTriggers)
        {
            if (trigger != null && trigger.Matches(lineId, choiceIndex))
            {
                pendingOpen = true;
                return;
            }
        }
    }

    private void HandleDialogueEnded()
    {
        DialogueManager manager = DialogueManager.Instance;
        if (manager == null || greetingDialogue == null) return;
        if (manager.LastFinishedDialogue != greetingDialogue) return;

        bool shouldOpen = pendingOpen
                          || (openWhenDialogueEnds && (openTriggers == null || openTriggers.Length == 0));

        pendingOpen = false;

        if (shouldOpen) OpenShop();
    }

    private static string ResolveLineId(Dialogue dialogue, int lineIndex)
    {
        if (dialogue?.lines == null) return null;
        if (lineIndex < 0 || lineIndex >= dialogue.lines.Count) return null;
        return dialogue.lines[lineIndex]?.lineId;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (shopInventory == null)
            Debug.LogWarning($"[ShopNPC] {name}: shopInventory가 X.", this);

        if (greetingDialogue == null)
        {
            Debug.LogWarning($"[ShopNPC] {name}: greetingDialogue가 X. " +
                             "대화 기반 열기가 동작하지 X.", this);
            return;
        }

        if (openTriggers == null) return;

        for (int i = 0; i < openTriggers.Length; i++)
        {
            ShopOpenTrigger trigger = openTriggers[i];
            if (trigger == null) continue;

            if (string.IsNullOrEmpty(trigger.LineId))
            {
                Debug.LogWarning($"[ShopNPC] {name}: openTriggers[{i}]의 lineId가 X.", this);
                continue;
            }

            bool found = false;
            foreach (DialogueLine line in greetingDialogue.lines)
            {
                if (line != null && line.lineId == trigger.LineId)
                {
                    found = true;

                    if (line.choices == null || trigger.ChoiceIndex >= line.choices.Count)
                    {
                        Debug.LogError(
                            $"[ShopNPC] {name}: openTriggers[{i}] choiceIndex " +
                            $"{trigger.ChoiceIndex}가 '{trigger.LineId}' 줄의 선택지 개수 Over.", this);
                    }

                    break;
                }
            }

            if (!found)
            {
                Debug.LogError(
                    $"[ShopNPC] {name}: openTriggers[{i}]의 lineId '{trigger.LineId}'에 " +
                    $"해당하는 줄이 '{greetingDialogue.name}'에 X.", this);
            }
        }
    }
#endif
}