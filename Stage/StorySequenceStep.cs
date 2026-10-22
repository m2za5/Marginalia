using System;
using UnityEngine;
using UnityEngine.Playables;

public enum StoryStepType
{
    WaitForLanding,
    PlayTimeline,
    AutoWalk,
    Dialogue,
    SetActive
}

[Serializable]
public sealed class StorySequenceStep
{
    public StoryStepType type;

    [Header("Timeline")]
    public string cutsceneId;
    public PlayableDirector director;
    [Tooltip("Optional when the Timeline owns camera switching. Otherwise use a separate, active Camera.")]
    public Camera cutsceneCamera;

    [Header("Auto Walk (short, clear, level paths)")]
    public Transform destination;
    [Min(0.1f)] public float moveSpeed = 2.5f;
    [Min(0.01f)] public float stopDistance = 0.15f;

    [Header("Dialogue")]
    public Dialogue dialogue;

    [Header("Set Active")]
    public GameObject target;
    public bool active;

    [Tooltip("Seconds allowed for landing or walking. Does not time out player dialogue choices.")]
    [Min(0.1f)] public float timeout = 10f;
}
