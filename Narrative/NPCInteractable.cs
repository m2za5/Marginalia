using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class NPCInteractable : MonoBehaviour
{
    [SerializeField] private UnityEvent onInteract;
    [SerializeField] private Canvas promptCanvas;
    [SerializeField] private GameObject promptUI;

    [SerializeField] private bool requireProximity = true;

    [Header("Quest Offer")]
    [SerializeField] private Quest questOffer;
    [SerializeField] private bool questOfferAvailable;
    [SerializeField] private bool disableQuestOfferAfterAccepted = true;
    [SerializeField] private bool saveAfterQuestAccepted = true;

    [Header("Quest Events")]
    [SerializeField] private UnityEvent onQuestAccepted = new UnityEvent();

    private bool _playerInRange;
    private bool _interactable = true;
    private bool _promptShown;

    public bool QuestOfferAvailable => questOfferAvailable;

    public UnityEvent OnQuestAccepted => onQuestAccepted;

    public void SetInteractable(bool value)
    {
        if (_interactable == value) return;
        _interactable = value;
        UpdatePrompt();
    }

    public void SetQuestOfferAvailable(bool value)
    {
        questOfferAvailable = value;
    }

    public void EnableQuestOffer()
    {
        SetQuestOfferAvailable(true);
    }

    public void DisableQuestOffer()
    {
        SetQuestOfferAvailable(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = true;
        UpdatePrompt();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        UpdatePrompt();
    }

    private void Update()
    {
        if (!CanInteractNow())
        {
            if (_promptShown) SetPromptVisible(false);
            return;
        }

        if (!_promptShown) UpdatePrompt();

        if (!_interactable) return;
        if (requireProximity && !_playerInRange) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.eKey.wasPressedThisFrame)
        {
            if (!TryAcceptQuest())
            {
                onInteract.Invoke();
            }
        }
    }

    private bool CanInteractNow()
    {
        GameStateManager manager = GameStateManager.Instance;

        if (manager != null) return manager.IsState(GameState.Playing);

        DialogueManager dialogue = DialogueManager.Instance;
        return dialogue == null || !dialogue.IsDialogueActive;
    }

    public bool TryAcceptQuest()
    {
        if (!questOfferAvailable || questOffer == null || string.IsNullOrWhiteSpace(questOffer.questId))
        {
            return false;
        }

        QuestManager questManager = QuestManager.Instance;

        if (questManager == null)
        {
            Debug.LogWarning($"[NPCInteractable] QuestManager is missing. questId: {questOffer.questId}", this);
            return false;
        }

        if (!questManager.RegisterQuest(questOffer))
        {
            return false;
        }

        bool accepted = questManager.StartQuest(questOffer.questId);

        if (!accepted)
        {
            return false;
        }

        if (disableQuestOfferAfterAccepted)
        {
            questOfferAvailable = false;
        }

        if (saveAfterQuestAccepted && SaveManager.Instance != null)
        {
            SaveManager.Instance.SaveGame();
        }

        onQuestAccepted?.Invoke();
        return true;
    }

    private void UpdatePrompt()
    {
        bool show = _interactable && CanInteractNow() && (!requireProximity || _playerInRange);
        SetPromptVisible(show);
    }

    private void SetPromptVisible(bool visible)
    {
        _promptShown = visible;

        if (promptCanvas != null)
        {
            if (promptCanvas.enabled != visible) promptCanvas.enabled = visible;
            return;
        }

        if (promptUI != null && promptUI.activeSelf != visible)
            promptUI.SetActive(visible);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[NPCInteractable] {name}: Collider의 Is Trigger가 X. " +
                             "OnTriggerEnter가 호출X.", this);
        }

        if (GetComponent<Collider2D>() != null)
            Debug.LogError($"[NPCInteractable] {name}: 2D 콜라이더. " +
                           "이 프로젝트는 3D 충돌. BoxCollider로 교체.", this);

        if (questOffer != null)
        {
            questOffer.requiredAmount = Mathf.Max(1, questOffer.requiredAmount);
        }
    }
#endif
}