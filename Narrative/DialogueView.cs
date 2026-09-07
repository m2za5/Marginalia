using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DialogueView : MonoBehaviour
{
    [SerializeField] private Transform choicesContainer;
    [SerializeField] private Button choiceButtonPrefab;

    public bool CanShowChoices =>
        choiceButtonPrefab != null &&
        choicesContainer != null &&
        choicesContainer.gameObject.activeInHierarchy;

    private readonly List<Button> _spawnedChoices = new List<Button>();

    private void Awake() => ClearChoices();

    public bool ShowChoices(List<DialogueChoice> choices, Action<int> onSelect)
    {
        ClearChoices();

        if (choiceButtonPrefab == null || choicesContainer == null)
        {
            Debug.LogError("[DialogueView] 선택지 프리팹/컨테이너가 비어있다.", this);
            return false;
        }

        if (!choicesContainer.gameObject.activeInHierarchy)
        {
            Debug.LogError("[DialogueView] choicesContainer가 비활성 계층 아래에 있어 선택지를 표시할 수 없다.", this);
            return false;
        }

        if (choices == null || choices.Count == 0 || onSelect == null) return false;

        for (int i = 0; i < choices.Count; i++)
        {
            int idx = i;
            Button btn = GetOrCreateChoiceButton(i);
            btn.gameObject.SetActive(true);
            btn.onClick.RemoveAllListeners();

            TMP_Text label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = choices[i].choiceText ?? string.Empty;
            btn.onClick.AddListener(() => onSelect(idx));
        }

        return true;
    }

    public void ClearChoices()
    {
        foreach (Button btn in _spawnedChoices)
        {
            if (btn == null) continue;
            btn.onClick.RemoveAllListeners();
            btn.gameObject.SetActive(false);
        }
    }

    private Button GetOrCreateChoiceButton(int index)
    {
        while (_spawnedChoices.Count <= index)
        {
            Button btn = Instantiate(choiceButtonPrefab, choicesContainer);
            btn.gameObject.SetActive(false);
            _spawnedChoices.Add(btn);
        }

        return _spawnedChoices[index];
    }
}
