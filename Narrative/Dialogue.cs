using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DialogueChoice
{
    public string choiceText;
    public string targetLineId;
    public int targetLineIndex = -1;
}

[System.Serializable]
public class DialogueLine
{
    [Header("Identity")]
    public string lineId;

    [Header("Content")]
    public string speakerName;
    [TextArea(2, 5)]
    public string dialogueText;
    public Sprite speakerPortrait;

    [Header("Flow")]
    public bool isEnd;
    public bool hasJump;
    public string jumpTargetId;
    public int jumpTargetIndex;

    public List<DialogueChoice> choices = new List<DialogueChoice>();
}

[CreateAssetMenu(fileName = "NewDialogue", menuName = "Dialogue/Dialogue Asset")]
public class Dialogue : ScriptableObject
{
    public List<DialogueLine> lines = new List<DialogueLine>();

#if UNITY_EDITOR
    private void OnValidate()
    {
        var ids = new HashSet<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            DialogueLine line = lines[i];
            if (line == null) continue;

            if (!string.IsNullOrEmpty(line.lineId) && !ids.Add(line.lineId))
                Debug.LogError($"[{name}] line {i}: lineId '{line.lineId}' 중복.", this);

            if (line.hasJump && string.IsNullOrEmpty(line.jumpTargetId) &&
                (line.jumpTargetIndex < 0 || line.jumpTargetIndex >= lines.Count))
                Debug.LogError($"[{name}] line {i}: jumpTargetIndex {line.jumpTargetIndex} 범위 밖 (0~{lines.Count - 1}).", this);

            if (line.choices == null) continue;

            for (int c = 0; c < line.choices.Count; c++)
            {
                DialogueChoice choice = line.choices[c];
                if (choice == null) continue;

                if (string.IsNullOrEmpty(choice.targetLineId) && choice.targetLineIndex >= lines.Count)
                    Debug.LogError($"[{name}] line {i} choice {c}: targetLineIndex {choice.targetLineIndex} " +
                                   $"범위 밖 (0~{lines.Count - 1}). 리스트 0-based.", this);
            }
        }

        foreach (DialogueLine line in lines)
        {
            if (line == null) continue;

            if (line.hasJump && !string.IsNullOrEmpty(line.jumpTargetId) && !ids.Contains(line.jumpTargetId))
                Debug.LogError($"[{name}] jumpTargetId '{line.jumpTargetId}' 에 해당하는 줄X.", this);

            if (line.choices == null) continue;

            foreach (DialogueChoice choice in line.choices)
            {
                if (choice == null || string.IsNullOrEmpty(choice.targetLineId)) continue;
                if (!ids.Contains(choice.targetLineId))
                    Debug.LogError($"[{name}] targetLineId '{choice.targetLineId}' 에 해당하는 줄X.", this);
            }
        }
    }
#endif
}