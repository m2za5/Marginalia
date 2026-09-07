using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class DialogueManager : MonoBehaviour
{
    private enum State
    {
        Inactive,
        Typing,
        WaitingForContinue,
        WaitingForChoice
    }

    private const float ArrivalEpsilon = 0.0001f;

    public static DialogueManager Instance;

    [Header("Root (Canvas 컴포넌트를 넣으면 SetActive 대신 Canvas X = rebuild 회피)")]
    [SerializeField] private Canvas dialogueCanvas;
    [SerializeField] private GameObject dialoguePanel;

    [Header("Text")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text dialogueText;

    [Header("Portrait")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private PortraitCalibration portraitCalibration;

    [Header("Choices (choicePanel에 중첩 Canvas 권장)")]
    [SerializeField] private Canvas choiceCanvas;
    [SerializeField] private GameObject choicePanel;
    [SerializeField] private DialogueView dynamicChoiceView;

    [Header("Typing")]
    [SerializeField, Min(0f)] private float typeSpeed = 0.03f;

    public bool IsDialogueActive => _state != State.Inactive;
    public bool IsWaitingForChoice => _state == State.WaitingForChoice;
    public Dialogue CurrentDialogue { get; private set; }
    public Dialogue LastFinishedDialogue { get; private set; }
    public event Action OnDialogueStarted;
    public event Action OnDialogueEnded;
    public event Action<Dialogue, int, int> OnChoiceSelected;

    private DialogueLine[] _lines;
    private int _index;
    private State _state = State.Inactive;
    private int _visibleChars;
    private int _totalChars;
    private float _typeAccumulator;
    private GameState _previousGameState;
    private bool _shouldRestorePreviousGameState;
    private int _inputUnlockFrame;
    private Vector2 _portraitBasePosition;
    private RectTransform _portraitRect;
    private readonly Dictionary<string, int> _idToIndex = new Dictionary<string, int>(16);
    private bool _hasIdMap;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (portraitImage != null)
        {
            _portraitRect = portraitImage.rectTransform;
            _portraitBasePosition = _portraitRect.anchoredPosition;
        }

        if (dynamicChoiceView == null) dynamicChoiceView = GetComponent<DialogueView>();

        SetRootVisible(false);
        SetChoicesVisible(false);
        dynamicChoiceView?.ClearChoices();
    }

    private void Update()
    {
        if (_state == State.Inactive) return;

        if (_state == State.Typing) TickTyping();

        if (_state == State.WaitingForChoice) return;

        if (Time.frameCount < _inputUnlockFrame) return;
        if (!ContinuePressed()) return;

        if (_state == State.Typing) CompleteLine();
        else Advance();
    }

    private void TickTyping()
    {
        if (dialogueText == null || _totalChars == 0)
        {
            FinishTyping();
            return;
        }

        if (typeSpeed <= 0f)
        {
            _visibleChars = _totalChars;
            dialogueText.maxVisibleCharacters = _totalChars;
            FinishTyping();
            return;
        }

        _typeAccumulator += Time.unscaledDeltaTime;

        int step = (int)(_typeAccumulator / typeSpeed);
        if (step > 0)
        {
            _typeAccumulator -= step * typeSpeed;
            _visibleChars = Mathf.Min(_visibleChars + step, _totalChars);
            dialogueText.maxVisibleCharacters = _visibleChars;
        }

        if (_visibleChars >= _totalChars) FinishTyping();
    }

    public void StartDialogue(Dialogue dialogue)
    {
        if (dialogue == null || dialogue.lines == null || dialogue.lines.Count == 0) return;
        if (IsDialogueActive) return;

        CurrentDialogue = dialogue;
        _lines = dialogue.lines.ToArray();
        BuildIdMap();

        _index = 0;
        _inputUnlockFrame = Time.frameCount + 1;

        CaptureAndEnterDialogueState();

        SetRootVisible(true);
        SetChoicesVisible(false);
        dynamicChoiceView?.ClearChoices();

        OnDialogueStarted?.Invoke();
        ShowLine(0);
    }

    private void BuildIdMap()
    {
        _idToIndex.Clear();
        _hasIdMap = false;

        for (int i = 0; i < _lines.Length; i++)
        {
            string id = _lines[i]?.lineId;
            if (string.IsNullOrEmpty(id)) continue;

            if (_idToIndex.ContainsKey(id))
            {
                Debug.LogError($"[DialogueManager] '{CurrentDialogue.name}' line {i}: lineId '{id}' 중복. 앞의 것이 유지된다.");
                continue;
            }

            _idToIndex.Add(id, i);
            _hasIdMap = true;
        }
    }

    private int ResolveIndex(string id, int fallbackIndex, string context)
    {
        if (string.IsNullOrEmpty(id)) return fallbackIndex;

        if (_hasIdMap && _idToIndex.TryGetValue(id, out int resolved)) return resolved;

        Debug.LogError($"[DialogueManager] {context}: targetLineId '{id}' 해석 실패. 대화를 종료한다.");
        return -1;
    }

    private void ShowLine(int lineIndex)
    {
        if (_lines == null || lineIndex < 0 || lineIndex >= _lines.Length)
        {
            EndDialogue();
            return;
        }

        _index = lineIndex;
        DialogueLine line = _lines[_index];
        if (line == null)
        {
            EndDialogue();
            return;
        }

        if (nameText != null) nameText.text = line.speakerName;
        ApplyPortrait(line.speakerPortrait);
        BeginTyping(line);
    }

    private void BeginTyping(DialogueLine line)
    {
        _state = State.Typing;
        _visibleChars = 0;
        _typeAccumulator = 0f;
        _totalChars = 0;

        if (dialogueText == null) return;
        dialogueText.text = line.dialogueText ?? string.Empty;
        dialogueText.maxVisibleCharacters = 0;
        dialogueText.ForceMeshUpdate();
        _totalChars = dialogueText.textInfo.characterCount;
    }

    private void CompleteLine()
    {
        if (dialogueText != null)
        {
            _visibleChars = _totalChars;
            dialogueText.maxVisibleCharacters = _totalChars;
        }

        FinishTyping();
    }

    private void FinishTyping()
    {
        _inputUnlockFrame = Time.frameCount + 1;

        DialogueLine line = _lines[_index];

        if (line.choices != null && line.choices.Count > 0)
        {
            ShowChoices(line);
            return;
        }

        _state = State.WaitingForContinue;
    }

    private bool ContinuePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
        {
            return true;
        }

        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private void ShowChoices(DialogueLine line)
    {
        _state = State.WaitingForChoice;

        if (EventSystem.current == null)
        {
            FailChoicePresentation("EventSystem이 없어 선택지 UI 입력을 받을 수 없다.");
            return;
        }

        if (TryShowDynamicChoices(line)) return;

        FailChoicePresentation("선택지가 있는 줄인데 DialogueView 선택지 스포너가 준비되지 않았다.");
    }

    private bool TryShowDynamicChoices(DialogueLine line)
    {
        if (dynamicChoiceView == null) dynamicChoiceView = GetComponent<DialogueView>();
        if (dynamicChoiceView == null) return false;

        SetChoicesVisible(true);

        if (!dynamicChoiceView.CanShowChoices)
        {
            SetChoicesVisible(false);
            return false;
        }

        if (!dynamicChoiceView.ShowChoices(line.choices, SelectChoice))
        {
            SetChoicesVisible(false);
            return false;
        }

        return true;
    }

    private void FailChoicePresentation(string reason)
    {
        Debug.LogError($"[DialogueManager] {reason} 대화를 종료하고 조작권을 복구한다.", this);
        EndDialogue();
    }

    public void SelectChoice(int choiceIndex)
    {
        if (_state != State.WaitingForChoice) return;

        DialogueLine line = _lines[_index];
        if (line.choices == null || choiceIndex < 0 || choiceIndex >= line.choices.Count) return;

        DialogueChoice choice = line.choices[choiceIndex];
        int fromLine = _index;

        SetChoicesVisible(false);
        dynamicChoiceView?.ClearChoices();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        _inputUnlockFrame = Time.frameCount + 1;

        OnChoiceSelected?.Invoke(CurrentDialogue, fromLine, choiceIndex);

        int target = ResolveIndex(choice.targetLineId, choice.targetLineIndex,
                                  $"line {fromLine} choice {choiceIndex}");

        if (target < 0 || target >= _lines.Length) EndDialogue();
        else ShowLine(target);
    }

    private void Advance()
    {
        DialogueLine line = _lines[_index];

        if (line.isEnd)
        {
            EndDialogue();
            return;
        }

        int next = line.hasJump
            ? ResolveIndex(line.jumpTargetId, line.jumpTargetIndex, $"line {_index} jump")
            : _index + 1;

        if (next < 0 || next >= _lines.Length)
        {
            EndDialogue();
            return;
        }

        ShowLine(next);
    }

    private void ApplyPortrait(Sprite sprite)
    {
        if (portraitImage == null) return;

        if (sprite == null)
        {
            if (portraitImage.enabled) portraitImage.enabled = false;
            return;
        }

        if (!portraitImage.enabled) portraitImage.enabled = true;
        if (portraitImage.sprite != sprite)
        {
            portraitImage.sprite = sprite;
            portraitImage.preserveAspect = true;

            float scale = 1f;
            Vector2 offset = Vector2.zero;
            portraitCalibration?.TryGet(sprite, out scale, out offset);

            if (_portraitRect != null)
            {
                Vector3 target = new Vector3(scale, scale, 1f);
                if ((_portraitRect.localScale - target).sqrMagnitude > ArrivalEpsilon)
                    _portraitRect.localScale = target;

                _portraitRect.anchoredPosition = _portraitBasePosition + offset;
            }
        }
    }

    private void EndDialogue()
    {
        SetChoicesVisible(false);
        dynamicChoiceView?.ClearChoices();

        if (nameText != null) nameText.text = string.Empty;

        if (dialogueText != null)
        {
            dialogueText.text = string.Empty;
            dialogueText.maxVisibleCharacters = int.MaxValue;
        }

        if (portraitImage != null)
        {
            portraitImage.sprite = null;
            portraitImage.enabled = false;
        }

        SetRootVisible(false);

        _state = State.Inactive;
        _lines = null;
        _totalChars = 0;
        _visibleChars = 0;
        LastFinishedDialogue = CurrentDialogue;
        CurrentDialogue = null;

        RestorePreviousGameState();
        OnDialogueEnded?.Invoke();
    }

    private void SetRootVisible(bool visible)
    {
        if (dialogueCanvas != null)
        {
            if (dialogueCanvas.enabled != visible) dialogueCanvas.enabled = visible;
            return;
        }

        if (dialoguePanel != null && dialoguePanel.activeSelf != visible)
            dialoguePanel.SetActive(visible);
    }

    private void SetChoicesVisible(bool visible)
    {
        if (choiceCanvas != null)
        {
            if (choiceCanvas.enabled != visible) choiceCanvas.enabled = visible;
            return;
        }

        if (choicePanel != null && choicePanel.activeSelf != visible)
            choicePanel.SetActive(visible);
    }

    private void CaptureAndEnterDialogueState()
    {
        if (GameStateManager.Instance == null)
        {
            _shouldRestorePreviousGameState = false;
            return;
        }

        _previousGameState = GameStateManager.Instance.CurrentState;
        _shouldRestorePreviousGameState = _previousGameState != GameState.Dialogue;
        GameStateManager.Instance.SetDialogue();
    }

    private void RestorePreviousGameState()
    {
        if (!_shouldRestorePreviousGameState || GameStateManager.Instance == null)
        {
            _shouldRestorePreviousGameState = false;
            return;
        }

        _shouldRestorePreviousGameState = false;

        if (GameStateManager.Instance.IsState(GameState.Dialogue))
            GameStateManager.Instance.ChangeState(_previousGameState);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
