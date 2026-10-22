using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Header("Save Settings")]
    [SerializeField] private string saveFileName = "save_data.json"; // 임시
    [SerializeField] private string settingsFileName = "settings_data.json";

    private string SavePath => Path.Combine(Application.persistentDataPath, ResolveSaveFileName());
    private string SettingsPath => Path.Combine(Application.persistentDataPath, settingsFileName);

#if UNITY_EDITOR
    public static string EditorSaveFileNameOverride { get; set; }
    public string EditorSaveFilePath => SavePath;
#endif

    private string ResolveSaveFileName()
    {
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(EditorSaveFileNameOverride))
        {
            return EditorSaveFileNameOverride;
        }
#endif
        return saveFileName;
    }

    public SaveData CurrentSaveData { get; private set; }
    public UserSettingsData CurrentSettings { get; private set; }

    public string playerTag = "Player"; // 태그는 임시

    private bool _isWaitingForSceneLoad;

    private void Awake()
    {
        if (Instance != null && Instance != this) // 중복 방지
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        CurrentSaveData = new SaveData();
        LoadSettings();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleUnitySceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleUnitySceneLoaded;
    }

    private void HandleUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyUserSettings();
        if (_isWaitingForSceneLoad && scene.name == GetLoadTargetSceneName(CurrentSaveData))
        {
            // Start에서 보스 연출이 시작되기 전에 플레이어 위치와 능력치를 복원한다.
            ApplySaveDataToCurrentScene(CurrentSaveData);
            _isWaitingForSceneLoad = false;
        }
    }

    public void SetBGMVolume(float volume)
    {
        EnsureSettings();
        CurrentSettings.bgmVolume = Mathf.Clamp01(volume);
        ApplyUserSettings();
        SaveSettings();
    }

    public void SetSFXVolume(float volume)
    {
        EnsureSettings();
        CurrentSettings.sfxVolume = Mathf.Clamp01(volume);
        ApplyUserSettings();
        SaveSettings();
    }

    public void SetMouseSensitivity(float sensitivity)
    {
        EnsureSettings();
        CurrentSettings.mouseSensitivity = Mathf.Clamp(sensitivity, 0.25f, 2f);
        ApplyUserSettings();
        SaveSettings();
    }

    public void ApplyUserSettings()
    {
        EnsureSettings();

        SoundManager.Instance?.SetBGMVolume(CurrentSettings.bgmVolume);
        SoundManager.Instance?.SetSFXVolume(CurrentSettings.sfxVolume);

        FixedAngleFollowCamera[] cameras = FindObjectsByType<FixedAngleFollowCamera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (FixedAngleFollowCamera camera in cameras)
        {
            camera.SetMouseSensitivity(
                CurrentSettings.mouseSensitivity,
                CurrentSettings.mouseSensitivity);
        }
    }

    private void LoadSettings()
    {
        CurrentSettings = new UserSettingsData();

        if (File.Exists(SettingsPath))
        {
            try
            {
                UserSettingsData loaded = JsonUtility.FromJson<UserSettingsData>(
                    File.ReadAllText(SettingsPath));

                if (loaded != null)
                {
                    CurrentSettings = loaded;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SaveManager] 옵션 불러오기 실패, 기본값을 사용합니다: {exception.Message}");
            }
        }

        CurrentSettings.bgmVolume = Mathf.Clamp01(CurrentSettings.bgmVolume);
        CurrentSettings.sfxVolume = Mathf.Clamp01(CurrentSettings.sfxVolume);
        CurrentSettings.mouseSensitivity = Mathf.Clamp(CurrentSettings.mouseSensitivity, 0.25f, 2f);
    }

    private bool SaveSettings()
    {
        try
        {
            EnsureSettings();
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(CurrentSettings, true));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveManager] 옵션 저장 실패: {exception.Message}");
            return false;
        }
    }

    private void EnsureSettings()
    {
        CurrentSettings ??= new UserSettingsData();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    // 게임 저장하는 부분

    public bool SaveGame() // exception 처리
    {
        try
        {
            SaveData data = CreateSaveData();

            string json = JsonUtility.ToJson(data, true);

            File.WriteAllText(SavePath, json);

            CurrentSaveData = data;

            Debug.Log($"[SaveManager] 게임 저장 완료: {SavePath}");

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveManager] 게임 저장 실패: {exception.Message}, 다시 시도해 주세요.");
            Debug.LogError(exception.StackTrace);

            return false;
        }
    }

    private SaveData CreateSaveData()
    {
        SaveData data = new SaveData();

        if (CurrentSaveData != null && CurrentSaveData.unlockedCombatDrawingSkillIds != null)
        {
            data.unlockedCombatDrawingSkillIds =
                new List<string>(CurrentSaveData.unlockedCombatDrawingSkillIds);
        }

        if (CurrentSaveData != null && CurrentSaveData.shopStocks != null)
        {
            foreach (ShopStockSaveData stock in CurrentSaveData.shopStocks)
            {
                if (stock != null)
                {
                    data.shopStocks.Add(new ShopStockSaveData(stock.shopId, stock.entryId, stock.remaining));
                }
            }
        }

        data.sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        GameObject player = FindPlayerInCurrentScene();

        if (player != null)
        {
            data.playerPosition = new Vector3Data(player.transform.position);

            PlayerInventory inventory = player.GetComponent<PlayerInventory>();

            if (inventory != null)
            {
                data.inventoryCurrency = inventory.Currency;
                data.inventoryItems = inventory.CreateSaveDataList();
            }

            Health health = player.GetComponent<Health>();
            PlayerCombatInk combatInk = player.GetComponent<PlayerCombatInk>();
            data.playerMaxHealth = health != null ? health.MaxHealth : 0;
            data.playerMaxSkillPoints = combatInk != null ? combatInk.MaxCharges : 0;
        }

        if (StageGateManager.Instance != null)
        {
            StageGateManager.Instance.SaveCurrentStagePosition();

            data.currentStageId = StageGateManager.Instance.CurrentStageId;
            data.lastPlayedStageId = StageGateManager.Instance.LastPlayedStageId;
            data.lastPlayedStageSceneName = StageGateManager.Instance.GetLastPlayedStageSceneName();

            data.stageSaveDataList = StageGateManager.Instance.CreateStageSaveDataList();
        }

        if (QuestManager.Instance != null)
        {
            data.questSaveDataList = QuestManager.Instance.CreateQuestSaveDataList();
            foreach (Quest quest in QuestManager.Instance.GetQuestsByStatus(QuestStatus.Completed))
                data.completedQuestIds.Add(quest.questId);
        }
        else if (CurrentSaveData != null)
        {
            data.questSaveDataList = CurrentSaveData.questSaveDataList;
            data.completedQuestIds = CurrentSaveData.completedQuestIds;
        }

        return data;
    }

    public bool IsCombatDrawingSkillUnlocked(string skillId)
    {
        return !string.IsNullOrWhiteSpace(skillId)
            && CurrentSaveData != null
            && CurrentSaveData.unlockedCombatDrawingSkillIds != null
            && CurrentSaveData.unlockedCombatDrawingSkillIds.Contains(skillId);
    }

    public bool UnlockCombatDrawingSkill(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return false;
        }

        CurrentSaveData ??= new SaveData();
        CurrentSaveData.unlockedCombatDrawingSkillIds ??= new List<string>();

        if (CurrentSaveData.unlockedCombatDrawingSkillIds.Contains(skillId))
        {
            return false;
        }

        CurrentSaveData.unlockedCombatDrawingSkillIds.Add(skillId);
        return true;
    }

    // 상점 한정 재고. 기록이 없으면 false를 반환하고, 호출 측은 에셋의 초기 재고를 사용한다.
    public bool TryGetShopStock(string shopId, string entryId, out int remaining)
    {
        ShopStockSaveData stock = FindShopStock(shopId, entryId);
        remaining = stock != null ? stock.remaining : 0;
        return stock != null;
    }

    public void SetShopStock(string shopId, string entryId, int remaining)
    {
        if (string.IsNullOrWhiteSpace(shopId) || string.IsNullOrWhiteSpace(entryId))
        {
            return;
        }

        CurrentSaveData ??= new SaveData();
        CurrentSaveData.shopStocks ??= new List<ShopStockSaveData>();

        ShopStockSaveData stock = FindShopStock(shopId, entryId);

        if (stock != null)
        {
            stock.remaining = remaining;
            return;
        }

        CurrentSaveData.shopStocks.Add(new ShopStockSaveData(shopId, entryId, remaining));
    }

    public void ClearShopStocks(string shopId = null)
    {
        if (CurrentSaveData?.shopStocks == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(shopId))
        {
            CurrentSaveData.shopStocks.Clear();
            return;
        }

        CurrentSaveData.shopStocks.RemoveAll(stock => stock == null || stock.shopId == shopId);
    }

    private ShopStockSaveData FindShopStock(string shopId, string entryId)
    {
        if (CurrentSaveData?.shopStocks == null
            || string.IsNullOrWhiteSpace(shopId)
            || string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        foreach (ShopStockSaveData stock in CurrentSaveData.shopStocks)
        {
            if (stock != null && stock.shopId == shopId && stock.entryId == entryId)
            {
                return stock;
            }
        }

        return null;
    }
    public bool HasSaveFile()
    {
        return File.Exists(SavePath);
    }

    public void DeleteSaveFile()
    {
        if (File.Exists(SavePath))
        {
            File.Delete(SavePath);
            Debug.Log("[SaveManager] Save file deleted.");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    // 게임 로드하는 부분

    public bool LoadGame()
    {
        if (_isWaitingForSceneLoad || (SceneLoader.Instance != null && SceneLoader.Instance.IsLoading))
        {
            Debug.LogWarning("[SaveManager] 씬을 로드하는 동안에는 저장 파일을 불러올 수 없습니다.");
            return false;
        }

        try
        {
            if (!File.Exists(SavePath))
            {
                Debug.LogWarning("[SaveManager] 저장 파일이 없습니다."); // 파일 없으면 오류 반환
                return false;
            }

            string json = File.ReadAllText(SavePath);
            SaveData data = JsonUtility.FromJson<SaveData>(json);

            if (data == null)
            {
                Debug.LogWarning("[SaveManager] 저장 데이터를 읽었지만 내용이 비어 있습니다.");
                return false;
            }

            data.shopStocks ??= new List<ShopStockSaveData>();

            ApplySaveData(data);

            Debug.Log("[SaveManager] 저장 데이터 로드 시작.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveManager] 게임 불러오기 실패: {exception.Message}");
            Debug.LogError(exception.StackTrace);

            _isWaitingForSceneLoad = false;
            return false;
        }
    }

    public void ApplySaveData(SaveData saveData)
    {
        if (saveData == null)
        {
            Debug.LogWarning("[SaveManager] 적용할 SaveData가 null입니다.");
            return;
        }

        if (_isWaitingForSceneLoad || (SceneLoader.Instance != null && SceneLoader.Instance.IsLoading))
        {
            Debug.LogWarning("[SaveManager] 씬을 로드하는 동안에는 저장 데이터를 적용할 수 없습니다.");
            return;
        }

        CurrentSaveData = saveData;
        GunHoPlayerSpawner.RequestDefaultSpawnForScene(null);
        ApplyStageData(saveData);
        ApplyQuestData(saveData);

        string targetSceneName = GetLoadTargetSceneName(saveData);
        string currentSceneName = SceneManager.GetActiveScene().name;

        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogWarning("[SaveManager] 이동할 씬 이름이 비어 있습니다. 현재 씬에 데이터를 바로 적용합니다.");
            ApplySaveDataToCurrentScene(saveData);
            return;
        }

        if (targetSceneName != currentSceneName)
        {
            _isWaitingForSceneLoad = true;

            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadSceneByName(targetSceneName);
            }
            else
            {
                SceneManager.LoadScene(targetSceneName);
            }

            return;
        }

        ApplySaveDataToCurrentScene(saveData);
    }

    private void ApplySaveDataToCurrentScene(SaveData saveData)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GunHoPlayerSpawner[] spawners = FindObjectsByType<GunHoPlayerSpawner>(FindObjectsSortMode.None);
        foreach (GunHoPlayerSpawner spawner in spawners)
        {
            if (spawner.isActiveAndEnabled && spawner.gameObject.scene == activeScene)
            {
                spawner.EnsurePlayerBySceneSaveOrDefault();
                break;
            }
        }

        ApplyInventoryData(saveData);

        Debug.Log("[SaveManager] 저장 데이터 적용 완료.");
    }

    private void ApplyInventoryData(SaveData saveData)
    {
        GameObject player = FindPlayerInCurrentScene();
        PlayerInventory inventory = player != null ? player.GetComponent<PlayerInventory>() : null;

        if (inventory != null)
        {
            inventory.ApplySaveData(saveData.inventoryCurrency, saveData.inventoryItems);
        }

        if (player == null)
        {
            return;
        }

        Health health = player.GetComponent<Health>();
        PlayerCombatInk combatInk = player.GetComponent<PlayerCombatInk>();

        if (health != null && saveData.playerMaxHealth > 0)
        {
            health.SetMaxHealth(saveData.playerMaxHealth, true);
        }

        if (combatInk != null && saveData.playerMaxSkillPoints > 0)
        {
            combatInk.SetMaxCharges(Mathf.Max(10, saveData.playerMaxSkillPoints), true);
        }
    }

    private GameObject FindPlayerInCurrentScene()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        foreach (GameObject player in GameObject.FindGameObjectsWithTag(playerTag))
        {
            if (player.scene == currentScene)
            {
                return player;
            }
        }
        return null;
    }

    private void ApplyStageData(SaveData saveData)
    {
        if (StageGateManager.Instance == null)
        {
            Debug.LogWarning("[SaveManager] StageGateManager.Instance가 없어 스테이지 데이터를 복원하지 못했습니다.");
            return;
        }

        string sceneStageId = StageGateManager.Instance.CurrentStageId;
        StageGateManager.Instance.ApplyStageSaveData(
            saveData.currentStageId,
            saveData.lastPlayedStageId,
            saveData.stageSaveDataList
        );

        if (string.IsNullOrWhiteSpace(saveData.currentStageId)
            && saveData.sceneName == SceneManager.GetActiveScene().name
            && !string.IsNullOrWhiteSpace(sceneStageId))
        {
            StageGateManager.Instance.SetCurrentStage(sceneStageId);
        }

        Debug.Log("[SaveManager] 스테이지 데이터 복원 완료");
    }

    public bool TryGetSavedPlayerPosition(string sceneName, out Vector3 position)
    {
        position = Vector3.zero;

        if (CurrentSaveData == null || CurrentSaveData.playerPosition == null
            || string.IsNullOrWhiteSpace(sceneName)
            || !string.Equals(CurrentSaveData.sceneName, sceneName, StringComparison.Ordinal))
        {
            return false;
        }

        position = CurrentSaveData.playerPosition.ToVector3();
        return true;
    }

    private void ApplyQuestData(SaveData saveData)
    {
        if (QuestManager.Instance == null)
        {
            Debug.LogWarning("[SaveManager] QuestManager.Instance가 없어 퀘스트 데이터를 복원하지 못했습니다.");
            return;
        }

        QuestManager.Instance.ApplyQuestSaveData(saveData.questSaveDataList, saveData.completedQuestIds);

        Debug.Log("[SaveManager] 퀘스트 데이터 복원 완료.");
    }

    private string GetLoadTargetSceneName(SaveData saveData)
    {
        if (!string.IsNullOrWhiteSpace(saveData.lastPlayedStageSceneName))
        {
            return saveData.lastPlayedStageSceneName;
        }

        if (!string.IsNullOrWhiteSpace(saveData.sceneName))
        {
            return saveData.sceneName;
        }

        return string.Empty;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
