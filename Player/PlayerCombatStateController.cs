using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerCombatState
{
    NonCombat,
    Combat
}

// 근처 몬스터를 확인해서 플레이어를 전투 상태나 비전투 상태로 전환함.
[DisallowMultipleComponent]
public sealed class PlayerCombatStateController : MonoBehaviour
{
    private const float MonsterRefreshInterval = 0.2f;

    [Header("Detection")]
    [SerializeField, Min(0.1f)] private float nearbyMonsterRadius = 8f;
    [SerializeField, Min(0f)] private float combatEntryDelay = 0.75f;
    [SerializeField, Min(0f)] private float combatExitDelay = 3f;
    [SerializeField] private bool forceBossStageCombat = true;

    private readonly HashSet<BasicMonsterAI> alertMonsters = new HashSet<BasicMonsterAI>();
    private readonly HashSet<BasicMonsterAI> nextAlertMonsters = new HashSet<BasicMonsterAI>();
    private PlayerCombatState currentState = PlayerCombatState.NonCombat;
    private bool desiredCombatState;
    private float transitionRemaining;
    private float nextMonsterRefreshTime;
    private bool isBossStage;

    public event Action<PlayerCombatState, PlayerCombatState> StateChanged;

    public PlayerCombatState CurrentState => currentState;
    public bool IsInCombat => currentState == PlayerCombatState.Combat;
    public bool IsBossStageCombat => isBossStage;
    public float NearbyMonsterRadius => nearbyMonsterRadius;
    public float CombatEntryDelay => combatEntryDelay;
    public float CombatExitDelay => combatExitDelay;
    public float TransitionRemaining => Mathf.Max(0f, transitionRemaining);

    private void Awake()
    {
        RefreshBossStageMode();

        if (isBossStage)
        {
            SetState(PlayerCombatState.Combat);
        }
    }

    private void Update()
    {
        if (GameStateManager.Instance != null
            && !GameStateManager.Instance.IsState(GameState.Playing))
        {
            return;
        }

        if (Time.unscaledTime >= nextMonsterRefreshTime)
        {
            nextMonsterRefreshTime = Time.unscaledTime + MonsterRefreshInterval;
            RefreshCombatSignals();
        }

        if (isBossStage)
        {
            if (!IsInCombat)
            {
                SetState(PlayerCombatState.Combat);
            }
            return;
        }

        if (desiredCombatState == IsInCombat)
        {
            transitionRemaining = 0f;
            return;
        }

        transitionRemaining -= Time.unscaledDeltaTime;
        if (transitionRemaining <= 0f)
        {
            SetState(desiredCombatState
                ? PlayerCombatState.Combat
                : PlayerCombatState.NonCombat);
        }
    }

    public void RequestCombatState(bool combat)
    {
        if (isBossStage && !combat)
        {
            return;
        }

        if (desiredCombatState == combat)
        {
            return;
        }

        desiredCombatState = combat;
        transitionRemaining = combat ? combatEntryDelay : combatExitDelay;
    }

    public void ForceCombatState(bool combat)
    {
        desiredCombatState = combat;
        transitionRemaining = 0f;
        SetState(combat ? PlayerCombatState.Combat : PlayerCombatState.NonCombat);
    }

    public void SetBossStageCombat(bool enabled)
    {
        isBossStage = enabled;
        if (isBossStage)
        {
            ForceCombatState(true);
        }
        else
        {
            RefreshCombatSignals();
        }
    }

    private void RefreshCombatSignals()
    {
        RefreshBossStageMode();
        nextAlertMonsters.Clear();
        bool hasCombatSignal = isBossStage;
        BasicMonsterAI[] monsters = FindObjectsByType<BasicMonsterAI>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        float radiusSquared = nearbyMonsterRadius * nearbyMonsterRadius;

        foreach (BasicMonsterAI monster in monsters)
        {
            if (monster == null || monster.IsDead)
            {
                continue;
            }

            Vector3 delta = monster.transform.position - transform.position;
            delta.y = 0f;
            bool isNearby = delta.sqrMagnitude <= radiusSquared;

            if (!isNearby && !isBossStage)
            {
                continue;
            }

            if (!monster.HasDetectedPlayer)
            {
                continue;
            }

            hasCombatSignal = true;
            if (monster is BossMonsterAI)
            {
                continue;
            }

            nextAlertMonsters.Add(monster);
            MonsterAlertIndicator indicator = monster.GetComponent<MonsterAlertIndicator>();
            indicator = indicator != null
                ? indicator
                : monster.gameObject.AddComponent<MonsterAlertIndicator>();
            indicator.SetAlertVisible(true);
        }

        foreach (BasicMonsterAI monster in alertMonsters)
        {
            if (monster == null || nextAlertMonsters.Contains(monster))
            {
                continue;
            }

            MonsterAlertIndicator indicator = monster.GetComponent<MonsterAlertIndicator>();
            indicator?.SetAlertVisible(false);
        }

        alertMonsters.Clear();
        foreach (BasicMonsterAI monster in nextAlertMonsters)
        {
            alertMonsters.Add(monster);
        }

        RequestCombatState(hasCombatSignal);
    }

    private void RefreshBossStageMode()
    {
        if (!forceBossStageCombat)
        {
            isBossStage = false;
            return;
        }

        BossBattleStageInitializer stage =
            FindFirstObjectByType<BossBattleStageInitializer>(FindObjectsInactive.Exclude);
        BossMonsterAI boss = FindFirstObjectByType<BossMonsterAI>(FindObjectsInactive.Exclude);
        isBossStage = stage != null && boss != null && boss.BattleActive;
    }

    private void SetState(PlayerCombatState nextState)
    {
        if (currentState == nextState)
        {
            return;
        }

        PlayerCombatState previousState = currentState;
        currentState = nextState;
        desiredCombatState = IsInCombat;
        transitionRemaining = 0f;
        StateChanged?.Invoke(previousState, currentState);
    }

    private void OnDisable()
    {
        foreach (BasicMonsterAI monster in alertMonsters)
        {
            if (monster != null)
            {
                monster.GetComponent<MonsterAlertIndicator>()?.SetAlertVisible(false);
            }
        }
        alertMonsters.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, nearbyMonsterRadius);
    }

    private void OnValidate()
    {
        nearbyMonsterRadius = Mathf.Max(0.1f, nearbyMonsterRadius);
        combatEntryDelay = Mathf.Max(0f, combatEntryDelay);
        combatExitDelay = Mathf.Max(0f, combatExitDelay);
    }
}
