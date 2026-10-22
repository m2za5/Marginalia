using System;
using System.Collections.Generic;
using UnityEngine;

// 문양의 체크포인트와 인식 난이도를 에셋으로 저장함.
[CreateAssetMenu(
    menuName = "Marginalia/Drawing/Pattern Definition",
    fileName = "DrawingPatternDefinition")]
public sealed class DrawingPatternDefinition : ScriptableObject
{
    [SerializeField] private string patternId = "pattern";
    [SerializeField] private Vector2[] checkpoints = Array.Empty<Vector2>();
    [SerializeField, Range(0.01f, 0.5f)] private float checkpointRadius = 0.085f;
    [SerializeField, Min(1)] private int minimumValidPointCount = 6;
    [SerializeField, Range(0.5f, 0.99f)] private float recognitionThreshold = 0.78f;
    [SerializeField] private bool allowReverseRecognition = true;
    [SerializeField, Min(0)] private int requiredStrokeCount = 0;
    [SerializeField] private bool requireCrossingStrokes = false;

    public string PatternId => patternId;
    public IReadOnlyList<Vector2> Checkpoints => checkpoints;
    public int CheckpointCount => checkpoints != null ? checkpoints.Length : 0;
    public float CheckpointRadius => checkpointRadius;
    public int MinimumValidPointCount => minimumValidPointCount;
    public float RecognitionThreshold => recognitionThreshold;
    public bool AllowReverseRecognition => allowReverseRecognition;
    public int RequiredStrokeCount => requiredStrokeCount;
    public bool RequireCrossingStrokes => requireCrossingStrokes;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(patternId) && CheckpointCount >= 2;

    public Vector2 GetCheckpoint(int index)
    {
        if (checkpoints == null)
        {
            throw new InvalidOperationException($"Drawing pattern '{name}' has no checkpoint array.");
        }

        return checkpoints[index];
    }

    private void OnValidate()
    {
        patternId = string.IsNullOrWhiteSpace(patternId) ? name : patternId.Trim();
        checkpointRadius = Mathf.Clamp(checkpointRadius, 0.01f, 0.5f);
        minimumValidPointCount = Mathf.Max(1, minimumValidPointCount);
        recognitionThreshold = Mathf.Clamp(recognitionThreshold, 0.5f, 0.99f);
        requiredStrokeCount = Mathf.Max(0, requiredStrokeCount);

        if (checkpoints == null)
        {
            checkpoints = Array.Empty<Vector2>();
            return;
        }

        for (int i = 0; i < checkpoints.Length; i++)
        {
            checkpoints[i] = new Vector2(
                Mathf.Clamp01(checkpoints[i].x),
                Mathf.Clamp01(checkpoints[i].y));
        }
    }
}
