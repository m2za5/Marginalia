using System;
using System.Collections.Generic;

// 대화 진행 상태.
public enum DialogueRunnerState
{
    Idle,
    ShowingLine,
    WaitingForChoice,
    Ended
}

public class DialogueRunner
{
    private List<DialogueLine> _lines;
    private int _currentIndex;

    public DialogueRunnerState State { get; private set; } = DialogueRunnerState.Idle;
    public int CurrentIndex => _currentIndex;
    public DialogueLine Current =>
        (_lines != null && _currentIndex >= 0 && _currentIndex < _lines.Count)
            ? _lines[_currentIndex]
            : null;
    public bool HasChoices =>
        Current != null && Current.choices != null && Current.choices.Count > 0;

    public event Action<DialogueLine> OnLineEntered;
    public event Action<List<DialogueChoice>> OnChoicesPresented;
    public event Action OnDialogueEnded;

    public void Start(Dialogue dialogue)
    {
        if (dialogue == null || dialogue.lines == null || dialogue.lines.Count == 0)
        {
            UnityEngine.Debug.LogWarning("[DialogueRunner] 빈 대화로 Start 호출");
            End();
            return;
        }
        _lines = dialogue.lines;
        _currentIndex = 0;
        EnterCurrentLine();
    }

    public void Advance()
    {
        if (State != DialogueRunnerState.ShowingLine) return;

        if (Current.isEnd)
        {
            End();
            return;
        }

        int next = _currentIndex + 1;
        if (next >= _lines.Count)
        {
            End();
            return;
        }

        _currentIndex = next;
        EnterCurrentLine();
    }

    public void Choose(int choiceIndex)
    {
        if (State != DialogueRunnerState.WaitingForChoice) return;
        if (Current == null) return;
        if (choiceIndex < 0 || choiceIndex >= Current.choices.Count) return;

        var choice = Current.choices[choiceIndex];

        if (choice.targetLineIndex < 0)
        {
            End();
            return;
        }

        if (choice.targetLineIndex >= _lines.Count)
        {
            UnityEngine.Debug.LogWarning(
                $"[DialogueRunner] 선택지 target {choice.targetLineIndex}이 줄 개수({_lines.Count}) 초과. 종료.");
            End();
            return;
        }

        _currentIndex = choice.targetLineIndex;
        EnterCurrentLine();
    }

    public void Cancel() => End();
    private void EnterCurrentLine()
    {
        if (Current == null)
        {
            End();
            return;
        }

        if (HasChoices)
        {
            State = DialogueRunnerState.WaitingForChoice;
            OnLineEntered?.Invoke(Current);
            OnChoicesPresented?.Invoke(Current.choices);
        }
        else
        {
            State = DialogueRunnerState.ShowingLine;
            OnLineEntered?.Invoke(Current);
        }
    }

    private void End()
    {
        State = DialogueRunnerState.Ended;
        OnDialogueEnded?.Invoke();
    }
}