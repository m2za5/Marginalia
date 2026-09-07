using UnityEngine;

[CreateAssetMenu(fileName = "New IdleBehavior", menuName = "Marginalia/NPC/Idle Behavior Data")]
public class NPCIdleBehaviorData : ScriptableObject
{
    public IdleAction[] actions;

    [Header("States")]
    public string walkStateName = "WR_Walk";
    public string defaultStateName = "WR_Idle";

    [Header("Movement")]
    [Min(0f)] public float strollRadius = 2f;
    [Min(0f)] public float moveSpeed = 1f;

    [Header("Pause")]
    [Min(0f)] public float pauseMin = 1f;
    [Min(0f)] public float pauseMax = 3f;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Random.Range(min, max)에서 min > max면 조용히 뒤집힌 결과가 나온다. 저장 시점에 막는다.
        if (pauseMax < pauseMin) pauseMax = pauseMin;

        if (string.IsNullOrEmpty(walkStateName))
            Debug.LogWarning($"[{name}] walkStateName이 비어 있다. Stroll 액션이 애니메이션 없이 이동한다.", this);

        if (actions == null) return;

        for (int i = 0; i < actions.Length; i++)
        {
            IdleAction action = actions[i];
            if (action == null) continue;

            if (action.weight < 1) action.weight = 1;
            if (action.duration < 0f) action.duration = 0f;

            if (action.type == IdleAction.ActionType.Animate && string.IsNullOrEmpty(action.stateName))
                Debug.LogWarning($"[{name}] actions[{i}]: Animate인데 stateName이 비어 있다. " +
                                 "defaultStateName으로 폴백된다.", this);
        }
    }
#endif
}

[System.Serializable]
public class IdleAction
{
    public enum ActionType
    {
        Animate,
        Stroll
    }

    public ActionType type;
    public string stateName;
    [Min(0f)] public float duration = 3f;
    [Range(1, 10)] public int weight = 1;
}