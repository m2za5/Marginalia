using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PortraitCalibration", menuName = "Marginalia/Narrative/Portrait Calibration")]
public class PortraitCalibration : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public Sprite sprite;
        [Range(0.1f, 4f)] public float scale = 1f;
        public Vector2 offset;
    }

    [SerializeField] private Entry[] entries;

    private Dictionary<Sprite, Entry> _map;

    public bool TryGet(Sprite sprite, out float scale, out Vector2 offset)
    {
        scale = 1f;
        offset = Vector2.zero;

        if (sprite == null) return false;

        if (_map == null) BuildMap();

        if (_map.TryGetValue(sprite, out Entry entry) && entry != null)
        {
            scale = entry.scale;
            offset = entry.offset;
            return true;
        }

        return false;
    }

    private void BuildMap()
    {
        int capacity = entries != null ? entries.Length : 0;
        _map = new Dictionary<Sprite, Entry>(capacity);

        if (entries == null) return;

        foreach (Entry entry in entries)
        {
            if (entry == null || entry.sprite == null) continue;
            _map[entry.sprite] = entry;
        }
    }

#if UNITY_EDITOR
    private void OnValidate() => _map = null;
#endif
}