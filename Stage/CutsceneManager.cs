using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

[Flags]
public enum CutsceneState : byte
{
    None = 0,
    Playing = 1 << 0,
    Paused = 1 << 1,
    Skipped = 1 << 2,
    Completed = 1 << 3
}

[Serializable]
public class CutsceneEntry
{
    public string cutsceneId;
    public PlayableAsset asset;
}

public class CutsceneManager : MonoBehaviour
{
    public static CutsceneManager Instance { get; private set; }

    [Header("Cutscene Database")]
    [SerializeField] private List<CutsceneEntry> cutsceneDatabase = new();

    public event Action<string> OnCutsceneStarted;
    public event Action<string> OnCutsceneFinished;
    public bool IsCutsceneActive => _activeDirectors.Count > 0;

    private readonly Dictionary<string, PlayableAsset> _assetLookup = new();
    private readonly Dictionary<string, byte> _cutsceneStates = new();
    private readonly Dictionary<string, PlayableDirector> _activeDirectors = new();
    private readonly Dictionary<PlayableDirector, string> _directorToId = new();
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildAssetLookup();
    }
    private void OnDestroy()
    {
        if (Instance != this) return;

        foreach (var director in _activeDirectors.Values)
        {
            if (director == null) continue;
            director.stopped -= OnDirectorStopped;
            director.Stop();
            if (director.gameObject != null) Destroy(director.gameObject);
        }
        _activeDirectors.Clear();
        _directorToId.Clear();
        _cutsceneStates.Clear();
        _assetLookup.Clear();
    }
    private void BuildAssetLookup()
    {
        _assetLookup.Clear();
        foreach (var entry in cutsceneDatabase)
        {
            if (string.IsNullOrWhiteSpace(entry.cutsceneId))
            {
                Debug.LogWarning("[CutsceneManager] cutsceneId가 비어있는 엔트리가 있습니다.");
                continue;
            }
            if (_assetLookup.ContainsKey(entry.cutsceneId))
            {
                Debug.LogWarning($"[CutsceneManager] 중복 cutsceneId: {entry.cutsceneId}");
                continue;
            }
            if (entry.asset == null)
            {
                Debug.LogWarning($"[CutsceneManager] asset이 비어있음: {entry.cutsceneId}");
                continue;
            }
            _assetLookup.Add(entry.cutsceneId, entry.asset);
        }
    }
    public bool PlayCutscene(string cutsceneId)
    {
        if (!_assetLookup.TryGetValue(cutsceneId, out var asset))
        {
            Debug.LogWarning($"[CutsceneManager] cutsceneId not found: {cutsceneId}");
            return false;
        }
        if (_activeDirectors.ContainsKey(cutsceneId))
        {
            Debug.LogWarning($"[CutsceneManager] 이미 재생 중: {cutsceneId}");
            return false;
        }

        var go = new GameObject($"Cutscene_{cutsceneId}");
        var director = go.AddComponent<PlayableDirector>();
        director.playableAsset = asset;
        director.playOnAwake = false;

        _activeDirectors[cutsceneId] = director;
        _directorToId[director] = cutsceneId;
        _cutsceneStates[cutsceneId] = (byte)CutsceneState.Playing;

        director.stopped += OnDirectorStopped;
        director.Play();

        OnCutsceneStarted?.Invoke(cutsceneId);
        return true;
    }
    public void SkipCutscene()
    {
        var snapshot = new List<KeyValuePair<string, PlayableDirector>>(_activeDirectors);
        foreach (var (id, director) in snapshot)
            SkipInternal(id, director);
    }
    public bool SkipCutscene(string cutsceneId)
    {
        if (!_activeDirectors.TryGetValue(cutsceneId, out var director)) return false;
        SkipInternal(cutsceneId, director);
        return true;
    }
    private void SkipInternal(string cutsceneId, PlayableDirector director)
    {
        if (director == null) return;
        if (director.state != PlayState.Playing && director.state != PlayState.Paused) return;

        director.stopped -= OnDirectorStopped;
        director.Stop();

        _cutsceneStates[cutsceneId] &= (byte)~CutsceneState.Playing;
        _cutsceneStates[cutsceneId] &= (byte)~CutsceneState.Paused;
        _cutsceneStates[cutsceneId] |= (byte)(CutsceneState.Skipped | CutsceneState.Completed);

        Cleanup(director, cutsceneId);
        OnCutsceneFinished?.Invoke(cutsceneId);
    }
    public void PauseCutscene()
    {
        foreach (var (id, director) in _activeDirectors)
            PauseInternal(id, director);
    }
    public bool PauseCutscene(string cutsceneId)
    {
        if (!_activeDirectors.TryGetValue(cutsceneId, out var director)) return false;
        PauseInternal(cutsceneId, director);
        return true;
    }
    private void PauseInternal(string cutsceneId, PlayableDirector director)
    {
        if (director == null || director.state != PlayState.Playing) return;
        director.Pause();
        _cutsceneStates[cutsceneId] &= (byte)~CutsceneState.Playing;
        _cutsceneStates[cutsceneId] |= (byte)CutsceneState.Paused;
    }
    public void ResumeCutscene()
    {
        foreach (var (id, director) in _activeDirectors)
            ResumeInternal(id, director);
    }
    public bool ResumeCutscene(string cutsceneId)
    {
        if (!_activeDirectors.TryGetValue(cutsceneId, out var director)) return false;
        ResumeInternal(cutsceneId, director);
        return true;
    }
    private void ResumeInternal(string cutsceneId, PlayableDirector director)
    {
        if (director == null || director.state != PlayState.Paused) return;
        director.Play();
        _cutsceneStates[cutsceneId] &= (byte)~CutsceneState.Paused;
        _cutsceneStates[cutsceneId] |= (byte)CutsceneState.Playing;
    }
    private void OnDirectorStopped(PlayableDirector director)
    {
        if (!_directorToId.TryGetValue(director, out var cutsceneId)) return;

        _cutsceneStates[cutsceneId] &= (byte)~CutsceneState.Playing;
        _cutsceneStates[cutsceneId] &= (byte)~CutsceneState.Paused;
        _cutsceneStates[cutsceneId] |= (byte)CutsceneState.Completed;

        Cleanup(director, cutsceneId);
        OnCutsceneFinished?.Invoke(cutsceneId);
    }
    public bool HasPlayedCutscene(string cutsceneId)
    {
        return _cutsceneStates.TryGetValue(cutsceneId, out var state)
               && (state & (byte)CutsceneState.Completed) != 0;
    }
    public bool WasSkipped(string cutsceneId)
    {
        return _cutsceneStates.TryGetValue(cutsceneId, out var state)
               && (state & (byte)CutsceneState.Skipped) != 0;
    }
    public void MarkCutsceneAsPlayed(string cutsceneId)
    {
        if (!_cutsceneStates.ContainsKey(cutsceneId))
            _cutsceneStates[cutsceneId] = 0;
        _cutsceneStates[cutsceneId] |= (byte)CutsceneState.Completed;
    }
    private void Cleanup(PlayableDirector director, string cutsceneId)
    {
        _activeDirectors.Remove(cutsceneId);
        _directorToId.Remove(director);
        if (director != null && director.gameObject != null)
            Destroy(director.gameObject);
    }
}
