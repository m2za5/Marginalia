using UnityEngine;
using UnityEngine.Events;

public class DialogueStarter : MonoBehaviour
{
    [SerializeField] private Dialogue dialogue;
    [SerializeField] private Animator animator;
    [SerializeField] private string endTriggerName = "";
    [SerializeField] private bool resumeIdleAfterDialogue = true;
    [SerializeField] private UnityEvent onDialogueEnded = new UnityEvent();

    // master 쪽에서 추가된 공개 접근자. 다른 스크립트가 런타임에 구독할 수 있다.
    public UnityEvent OnDialogueEnded => onDialogueEnded;

    private NPCIdleBehavior _idle;
    private int _endTriggerHash;
    private bool _hasEndTrigger;
    private bool _subscribed;

    private void Awake()
    {
        _idle = GetComponent<NPCIdleBehavior>();

        _hasEndTrigger = animator != null && !string.IsNullOrEmpty(endTriggerName);
        if (_hasEndTrigger) _endTriggerHash = Animator.StringToHash(endTriggerName);
    }

    public void StartDialogue()
    {
        if (dialogue == null) return;

        DialogueManager manager = DialogueManager.Instance;
        if (manager == null || manager.IsDialogueActive) return;
        if (!_subscribed)
        {
            manager.OnDialogueEnded += HandleEnd;
            _subscribed = true;
        }

        _idle?.Pause();
        manager.StartDialogue(dialogue);
    }

    private void HandleEnd()
    {
        DialogueManager manager = DialogueManager.Instance;

        if (manager == null || manager.LastFinishedDialogue != dialogue) return;

        Unsubscribe();

        if (_hasEndTrigger)
        {
            animator.SetTrigger(_endTriggerHash);
            _idle?.StopPermanently();
        }
        else if (resumeIdleAfterDialogue)
        {
            _idle?.Resume();
        }

        onDialogueEnded?.Invoke();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (DialogueManager.Instance != null) DialogueManager.Instance.OnDialogueEnded -= HandleEnd;
        _subscribed = false;
    }

    private void OnDisable() => Unsubscribe();
    private void OnDestroy() => Unsubscribe();
}