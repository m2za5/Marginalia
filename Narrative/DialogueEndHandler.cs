using UnityEngine;
using UnityEngine.Events;

public class DialogueEndHandler : MonoBehaviour
{
    [Header("Filter")]
    [SerializeField] private Dialogue targetDialogue;
    [SerializeField] private bool fireOnce = true;
    [SerializeField] private UnityEvent onDialogueEnded;

    private bool _fired;
    private bool _subscribed;

    private void OnEnable() => TrySubscribe();

    private void Start()
    {
        if (!_subscribed && !TrySubscribe())
        {
            Debug.LogWarning($"[DialogueEndHandler] {name}: DialogueManager.Instance 없음. " +
                             "이 오브젝트는 대화 종료에 반응하지 않는다.", this);
        }
    }

    private bool TrySubscribe()
    {
        if (_subscribed) return true;
        if (DialogueManager.Instance == null) return false;

        DialogueManager.Instance.OnDialogueEnded += HandleDialogueEnded;
        _subscribed = true;
        return true;
    }

    private void OnDisable() => Unsubscribe();
    private void OnDestroy() => Unsubscribe();

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.OnDialogueEnded -= HandleDialogueEnded;
        _subscribed = false;
    }

    private void HandleDialogueEnded()
    {
        if (fireOnce && _fired) return;

        if (targetDialogue != null)
        {
            DialogueManager manager = DialogueManager.Instance;
            if (manager == null || manager.LastFinishedDialogue != targetDialogue) return;
        }

        _fired = true;
        onDialogueEnded?.Invoke();
    }

    public void ResetFired() => _fired = false;
}
