using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SaveData
{
    public string sceneName;
    public string currentStageId;

    public string lastPlayedStageId;
    public string lastPlayedStageSceneName;

    public Vector3Data playerPosition;

    public List<QuestSaveData> questSaveDataList = new List<QuestSaveData>();
    public List<string> completedQuestIds = new List<string>();

    public List<StageSaveData> stageSaveDataList = new List<StageSaveData>();

    public int inventoryCurrency;
    public List<InventoryItemSaveData> inventoryItems = new List<InventoryItemSaveData>();
    public int playerMaxHealth;
    public int playerMaxSkillPoints;
    public List<string> unlockedCombatDrawingSkillIds = new List<string>();
    public List<ShopStockSaveData> shopStocks = new List<ShopStockSaveData>();

    public SaveData()
    {
        sceneName = "";
        currentStageId = "";

        lastPlayedStageId = "";
        lastPlayedStageSceneName = "";

        playerPosition = new Vector3Data(Vector3.zero);

    }
}

[Serializable]
public class UserSettingsData
{
    public float bgmVolume = 1f;
    public float sfxVolume = 1f;
    public float mouseSensitivity = 1f;
}

[Serializable]
public class InventoryItemSaveData
{
    public string itemId;
    public int quantity;

    public InventoryItemSaveData(string itemId, int quantity)
    {
        this.itemId = itemId;
        this.quantity = quantity;
    }
}

[Serializable]
public class ShopStockSaveData
{
    public string shopId;
    public string entryId;
    public int remaining;

    public ShopStockSaveData(string shopId, string entryId, int remaining)
    {
        this.shopId = shopId;
        this.entryId = entryId;
        this.remaining = remaining;
    }
}

[Serializable]
public class Vector3Data
{
    public float x;
    public float y;
    public float z;

    public Vector3Data(Vector3 vector)
    {
        x = vector.x;
        y = vector.y;
        z = vector.z;
    }

    public Vector3 ToVector3()
    {
        return new Vector3(x, y, z);
    }
}

[Serializable]
public class QuestSaveData
{
    public string questId;
    public List<QuestObjectiveSaveData> objectives = new List<QuestObjectiveSaveData>();
    // 이전 단일 목표 세이브를 읽기 위한 값이며 새 저장에는 objectives를 사용한다.
    public int currentAmount;
    public QuestStatus status;

    public QuestSaveData(Quest quest)
    {
        questId = quest.questId;
        foreach (QuestObjective objective in quest.Objectives)
            objectives.Add(new QuestObjectiveSaveData
            {
                objectiveId = objective.objectiveId,
                currentAmount = objective.currentAmount
            });
        status = quest.status;
    }
}

[Serializable]
public class QuestObjectiveSaveData
{
    public string objectiveId;
    public int currentAmount;
}

[Serializable]
public class StageSaveData
{
    public string stageId;

    public bool isUnlocked;
    public bool isCleared;

    public Vector3Data lastPlayerPosition;
    public bool hasLastPlayerPosition;

    public string lastSceneName;

    public List<ScenePositionSaveData> scenePositionSaveDataList =
        new List<ScenePositionSaveData>();

    public StageSaveData(string stageId)
    {
        this.stageId = stageId;

        isUnlocked = false;
        isCleared = false;

        lastSceneName = "";
    }
}

[Serializable]
public class ScenePositionSaveData
{
    public string sceneName;
    public bool hasPlayerPosition;
    public Vector3Data playerPosition;

    public ScenePositionSaveData(string sceneName)
    {
        this.sceneName = sceneName;
        hasPlayerPosition = false;
        playerPosition = new Vector3Data(Vector3.zero);
    }
}
