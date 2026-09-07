using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(DialogueStarter))]
public sealed class DialogueSceneTransition : MonoBehaviour
{
    public const string DefaultAliceBattleSceneName = "AliceBattleStage";

    [Header("Dialogue")]
    [SerializeField] private DialogueStarter dialogueStarter;
    [SerializeField] private bool transitionWhenDialogueEnds = true;

    [Header("Target Scene")]
    [SerializeField] private string targetSceneName = DefaultAliceBattleSceneName;
    [SerializeField] private bool saveCurrentStagePosition = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onTransitionStarted = new UnityEvent();

    private bool transitionRequested;

    private void Awake()
    {
        ResolveDialogueStarter();
    }

    private void OnEnable()
    {
        if (transitionWhenDialogueEnds && ResolveDialogueStarter() != null)
        {
            dialogueStarter.OnDialogueEnded.AddListener(MoveToTargetScene);
        }
    }

    private void OnDisable()
    {
        if (dialogueStarter != null)
        {
            dialogueStarter.OnDialogueEnded.RemoveListener(MoveToTargetScene);
        }
    }

    public void MoveToTargetScene()
    {
        if (transitionRequested)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogWarning("[DialogueSceneTransition] Target scene name is empty.", this);
            return;
        }

        transitionRequested = true;

        if (saveCurrentStagePosition && StageGateManager.Instance != null)
        {
            StageGateManager.Instance.SaveCurrentStagePosition();
        }

        onTransitionStarted?.Invoke();

        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadSceneByName(targetSceneName);
            return;
        }

        SceneManager.LoadScene(targetSceneName);
    }

    private DialogueStarter ResolveDialogueStarter()
    {
        if (dialogueStarter == null)
        {
            dialogueStarter = GetComponent<DialogueStarter>();
        }

        return dialogueStarter;
    }

    private void Reset()
    {
        dialogueStarter = GetComponent<DialogueStarter>();
        targetSceneName = DefaultAliceBattleSceneName;
    }
}
