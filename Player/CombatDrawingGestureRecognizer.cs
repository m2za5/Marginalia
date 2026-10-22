using System.Collections.Generic;
using UnityEngine;

public sealed class CombatDrawingRecognitionResult
{
    internal CombatDrawingRecognitionResult(
        CombatDrawingSkillDefinition skill,
        DrawingEvaluation evaluation,
        float confidence,
        float runnerUpConfidence)
    {
        Skill = skill;
        Evaluation = evaluation;
        Confidence = Mathf.Clamp01(confidence);
        RunnerUpConfidence = Mathf.Clamp01(runnerUpConfidence);
    }

    public CombatDrawingSkillDefinition Skill { get; }
    public DrawingEvaluation Evaluation { get; }
    public float Confidence { get; }
    public float RunnerUpConfidence { get; }
    public bool IsRecognized => Skill != null
        && Evaluation != null
        && Evaluation.Status == DrawingEvaluationStatus.Success;
}

public static class CombatDrawingGestureRecognizer
{
    private const int ResampledPointCount = 64;
    private const float MinimumGestureSize = 0.035f;
    private const float MinimumConfidenceMargin = 0.03f;
    private const float PointDistanceScale = 0.4f;
    private const float MinimumCancellationHorizontalSpan = 0.35f;
    private const float MaximumCancellationVerticalRatio = 0.35f;
    private const float MinimumStrokeLength = 0.03f;
    private const float MinimumCrossingInset = 0.12f;

    private readonly struct PathSegment
    {
        public PathSegment(Vector2 start, Vector2 end)
        {
            Start = start;
            End = end;
            Length = Vector2.Distance(start, end);
        }

        public Vector2 Start { get; }
        public Vector2 End { get; }
        public float Length { get; }
    }

    private sealed class NormalizedPath
    {
        public NormalizedPath(List<Vector2> points, float normalizedLength)
        {
            Points = points;
            NormalizedLength = normalizedLength;
        }

        public List<Vector2> Points { get; }
        public float NormalizedLength { get; }
        public float EndpointDistance => Points.Count > 1
            ? Vector2.Distance(Points[0], Points[Points.Count - 1])
            : 0f;
    }

    public static CombatDrawingRecognitionResult Recognize(
        DrawingResult drawing,
        IReadOnlyList<CombatDrawingSkillDefinition> candidates)
    {
        return RecognizeNormalizedPoints(
            drawing != null ? drawing.NormalizedPaperPoints : null,
            drawing != null && drawing.Canceled,
            candidates);
    }

    public static CombatDrawingRecognitionResult RecognizeNormalizedPoints(
        IReadOnlyList<Vector2> normalizedPoints,
        bool canceled,
        IReadOnlyList<CombatDrawingSkillDefinition> candidates)
    {
        int validPointCount = CountValidPoints(
            normalizedPoints);

        if (canceled)
        {
            return CreateTerminalResult(
                DrawingEvaluationStatus.Canceled,
                validPointCount);
        }

        if (validPointCount == 0)
        {
            return CreateTerminalResult(DrawingEvaluationStatus.Empty, 0);
        }

        NormalizedPath inputPath = PreparePath(normalizedPoints);

        if (inputPath == null || candidates == null || candidates.Count == 0)
        {
            return CreateTerminalResult(
                DrawingEvaluationStatus.Incomplete,
                validPointCount);
        }

        MeasureSpans(normalizedPoints, out float horizontalSpan, out float verticalSpan);
        CombatDrawingSkillDefinition cancellationSkill = FindDedicatedLineCandidate(
            candidates,
            CombatDrawingEffectKind.CancellationLine,
            validPointCount);

        if (cancellationSkill != null
            && IsLongAxisLine(horizontalSpan, verticalSpan))
        {
            DrawingPatternDefinition cancellationPattern = cancellationSkill.Pattern;
            float confidence = Mathf.Lerp(
                cancellationPattern.RecognitionThreshold,
                1f,
                Mathf.InverseLerp(
                    MinimumCancellationHorizontalSpan,
                    0.8f,
                    horizontalSpan));
            return new CombatDrawingRecognitionResult(
                cancellationSkill,
                new DrawingEvaluation(
                    DrawingEvaluationStatus.Success,
                    cancellationPattern.PatternId,
                    cancellationPattern.CheckpointCount,
                    cancellationPattern.CheckpointCount,
                    validPointCount),
                confidence,
                0f);
        }

        List<PathSegment> strokeChords = ExtractStrokeChords(normalizedPoints);
        CombatDrawingSkillDefinition eruptionSkill = FindDedicatedLineCandidate(
            candidates,
            CombatDrawingEffectKind.InkEruption,
            validPointCount);

        if (eruptionSkill != null
            && MeetsStrokeConstraints(eruptionSkill.Pattern, strokeChords)
            && IsLongAxisLine(verticalSpan, horizontalSpan))
        {
            DrawingPatternDefinition eruptionPattern = eruptionSkill.Pattern;
            float confidence = Mathf.Lerp(
                eruptionPattern.RecognitionThreshold,
                1f,
                Mathf.InverseLerp(
                    MinimumCancellationHorizontalSpan,
                    0.8f,
                    verticalSpan));
            return new CombatDrawingRecognitionResult(
                eruptionSkill,
                new DrawingEvaluation(
                    DrawingEvaluationStatus.Success,
                    eruptionPattern.PatternId,
                    eruptionPattern.CheckpointCount,
                    eruptionPattern.CheckpointCount,
                    validPointCount),
                confidence,
                0f);
        }

        CombatDrawingSkillDefinition bestSkill = null;
        float bestConfidence = 0f;
        float runnerUpConfidence = 0f;
        CombatDrawingSkillDefinition bestQualifiedSkill = null;
        float bestQualifiedConfidence = 0f;
        float qualifiedRunnerUpConfidence = 0f;

        for (int i = 0; i < candidates.Count; i++)
        {
            CombatDrawingSkillDefinition skill = candidates[i];
            DrawingPatternDefinition pattern = skill != null ? skill.Pattern : null;

            if (skill == null
                || !skill.IsConfigured
                || skill.EffectKind == CombatDrawingEffectKind.CancellationLine
                || skill.EffectKind == CombatDrawingEffectKind.InkEruption
                || pattern == null
                || validPointCount < pattern.MinimumValidPointCount
                || !MeetsStrokeConstraints(pattern, strokeChords))
            {
                continue;
            }

            NormalizedPath templatePath = PreparePath(pattern.Checkpoints);

            if (templatePath == null)
            {
                continue;
            }

            float confidence = CompareShape(inputPath, templatePath);

            if (confidence > bestConfidence)
            {
                runnerUpConfidence = bestConfidence;
                bestConfidence = confidence;
                bestSkill = skill;
            }
            else if (confidence > runnerUpConfidence)
            {
                runnerUpConfidence = confidence;
            }

            if (confidence < pattern.RecognitionThreshold)
            {
                continue;
            }

            if (confidence > bestQualifiedConfidence)
            {
                qualifiedRunnerUpConfidence = bestQualifiedConfidence;
                bestQualifiedConfidence = confidence;
                bestQualifiedSkill = skill;
            }
            else if (confidence > qualifiedRunnerUpConfidence)
            {
                qualifiedRunnerUpConfidence = confidence;
            }
        }

        bool hasQualifiedSkill = bestQualifiedSkill != null;
        CombatDrawingSkillDefinition selectedSkill = hasQualifiedSkill
            ? bestQualifiedSkill
            : bestSkill;
        float selectedConfidence = hasQualifiedSkill
            ? bestQualifiedConfidence
            : bestConfidence;
        float selectedRunnerUpConfidence = hasQualifiedSkill
            ? qualifiedRunnerUpConfidence
            : runnerUpConfidence;
        DrawingPatternDefinition bestPattern = selectedSkill != null ? selectedSkill.Pattern : null;
        bool isUnambiguous = selectedSkill != null
            && (selectedRunnerUpConfidence <= 0f
                || selectedConfidence - selectedRunnerUpConfidence >= MinimumConfidenceMargin);
        bool recognized = hasQualifiedSkill && isUnambiguous;
        int checkpointCount = bestPattern != null ? bestPattern.CheckpointCount : 0;
        int matchedCheckpointCount = recognized
            ? checkpointCount
            : Mathf.Clamp(
                Mathf.FloorToInt(bestConfidence * checkpointCount),
                0,
                checkpointCount);
        DrawingEvaluation evaluation = new DrawingEvaluation(
            recognized ? DrawingEvaluationStatus.Success : DrawingEvaluationStatus.Incomplete,
            bestPattern != null ? bestPattern.PatternId : string.Empty,
            matchedCheckpointCount,
            checkpointCount,
            validPointCount);

        return new CombatDrawingRecognitionResult(
            recognized ? selectedSkill : null,
            evaluation,
            selectedConfidence,
            selectedRunnerUpConfidence);
    }

    private static CombatDrawingSkillDefinition FindDedicatedLineCandidate(
        IReadOnlyList<CombatDrawingSkillDefinition> candidates,
        CombatDrawingEffectKind effectKind,
        int validPointCount)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            CombatDrawingSkillDefinition skill = candidates[i];
            DrawingPatternDefinition pattern = skill != null ? skill.Pattern : null;

            if (skill != null
                && skill.IsConfigured
                && skill.EffectKind == effectKind
                && pattern != null
                && validPointCount >= pattern.MinimumValidPointCount)
            {
                return skill;
            }
        }

        return null;
    }

    private static List<PathSegment> ExtractStrokeChords(IReadOnlyList<Vector2> points)
    {
        List<PathSegment> chords = new List<PathSegment>();
        bool hasStroke = false;
        Vector2 strokeStart = Vector2.zero;
        Vector2 previous = Vector2.zero;
        float strokeLength = 0f;

        for (int i = 0; i <= points.Count; i++)
        {
            bool isBreak = i == points.Count || DrawingResult.IsStrokeBreak(points[i]);

            if (isBreak)
            {
                if (hasStroke && strokeLength >= MinimumStrokeLength)
                {
                    chords.Add(new PathSegment(strokeStart, previous));
                }

                hasStroke = false;
                strokeLength = 0f;
                continue;
            }

            Vector2 point = points[i];

            if (hasStroke)
            {
                strokeLength += Vector2.Distance(previous, point);
            }
            else
            {
                strokeStart = point;
                hasStroke = true;
            }

            previous = point;
        }

        return chords;
    }

    private static bool MeetsStrokeConstraints(
        DrawingPatternDefinition pattern,
        List<PathSegment> strokeChords)
    {
        if (pattern.RequiredStrokeCount > 0
            && strokeChords.Count != pattern.RequiredStrokeCount)
        {
            return false;
        }

        return !pattern.RequireCrossingStrokes
            || (strokeChords.Count >= 2
                && ChordsCrossInInterior(strokeChords[0], strokeChords[1]));
    }

    private static bool ChordsCrossInInterior(PathSegment first, PathSegment second)
    {
        Vector2 firstDirection = first.End - first.Start;
        Vector2 secondDirection = second.End - second.Start;
        float denominator = Cross(firstDirection, secondDirection);

        if (Mathf.Abs(denominator) <= 1e-6f)
        {
            return false;
        }

        Vector2 offset = second.Start - first.Start;
        float firstProgress = Cross(offset, secondDirection) / denominator;
        float secondProgress = Cross(offset, firstDirection) / denominator;
        return firstProgress >= MinimumCrossingInset
            && firstProgress <= 1f - MinimumCrossingInset
            && secondProgress >= MinimumCrossingInset
            && secondProgress <= 1f - MinimumCrossingInset;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static bool IsLongAxisLine(float mainSpan, float crossSpan)
    {
        return mainSpan >= MinimumCancellationHorizontalSpan
            && crossSpan <= mainSpan * MaximumCancellationVerticalRatio;
    }

    private static void MeasureSpans(
        IReadOnlyList<Vector2> points,
        out float horizontalSpan,
        out float verticalSpan)
    {
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 point = points[i];

            if (DrawingResult.IsStrokeBreak(point))
            {
                continue;
            }

            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        horizontalSpan = maximum.x - minimum.x;
        verticalSpan = maximum.y - minimum.y;
    }

    private static CombatDrawingRecognitionResult CreateTerminalResult(
        DrawingEvaluationStatus status,
        int validPointCount)
    {
        return new CombatDrawingRecognitionResult(
            null,
            new DrawingEvaluation(status, string.Empty, 0, 0, validPointCount),
            0f,
            0f);
    }

    private static int CountValidPoints(IReadOnlyList<Vector2> points)
    {
        if (points == null)
        {
            return 0;
        }

        int count = 0;

        for (int i = 0; i < points.Count; i++)
        {
            if (!DrawingResult.IsStrokeBreak(points[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static NormalizedPath PreparePath(IReadOnlyList<Vector2> source)
    {
        if (source == null || source.Count < 2)
        {
            return null;
        }

        List<PathSegment> segments = new List<PathSegment>(source.Count - 1);
        bool hasPrevious = false;
        Vector2 previous = Vector2.zero;
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        float totalLength = 0f;

        for (int i = 0; i < source.Count; i++)
        {
            Vector2 point = source[i];

            if (DrawingResult.IsStrokeBreak(point))
            {
                hasPrevious = false;
                continue;
            }

            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);

            if (hasPrevious)
            {
                PathSegment segment = new PathSegment(previous, point);

                if (segment.Length > Mathf.Epsilon)
                {
                    segments.Add(segment);
                    totalLength += segment.Length;
                }
            }

            previous = point;
            hasPrevious = true;
        }

        Vector2 size = maximum - minimum;
        float scale = Mathf.Max(size.x, size.y);

        if (segments.Count == 0
            || totalLength <= Mathf.Epsilon
            || float.IsNaN(scale)
            || float.IsInfinity(scale)
            || scale < MinimumGestureSize)
        {
            return null;
        }

        List<Vector2> resampled = Resample(segments, totalLength);
        Vector2 center = (minimum + maximum) * 0.5f;

        for (int i = 0; i < resampled.Count; i++)
        {
            resampled[i] = (resampled[i] - center) / scale;
        }

        return new NormalizedPath(resampled, totalLength / scale);
    }

    private static List<Vector2> Resample(
        List<PathSegment> segments,
        float totalLength)
    {
        List<Vector2> samples = new List<Vector2>(ResampledPointCount);
        int segmentIndex = 0;
        float distanceBeforeSegment = 0f;

        for (int i = 0; i < ResampledPointCount; i++)
        {
            float targetDistance = totalLength * i / (ResampledPointCount - 1);

            while (segmentIndex < segments.Count - 1
                && distanceBeforeSegment + segments[segmentIndex].Length < targetDistance)
            {
                distanceBeforeSegment += segments[segmentIndex].Length;
                segmentIndex++;
            }

            PathSegment segment = segments[segmentIndex];
            float segmentProgress = segment.Length > Mathf.Epsilon
                ? Mathf.Clamp01(
                    (targetDistance - distanceBeforeSegment) / segment.Length)
                : 0f;
            samples.Add(Vector2.Lerp(segment.Start, segment.End, segmentProgress));
        }

        return samples;
    }

    private static float CompareShape(
        NormalizedPath input,
        NormalizedPath template)
    {
        float averageDistance = (
            GetAverageNearestDistance(input.Points, template.Points)
            + GetAverageNearestDistance(template.Points, input.Points)) * 0.5f;
        float pointScore = Mathf.Clamp01(
            1f - averageDistance / PointDistanceScale);
        return pointScore;
    }

    private static float GetAverageNearestDistance(
        IReadOnlyList<Vector2> source,
        IReadOnlyList<Vector2> destination)
    {
        float totalDistance = 0f;

        for (int i = 0; i < source.Count; i++)
        {
            float nearestDistance = float.PositiveInfinity;

            for (int j = 0; j < destination.Count; j++)
            {
                nearestDistance = Mathf.Min(
                    nearestDistance,
                    Vector2.Distance(source[i], destination[j]));
            }

            totalDistance += nearestDistance;
        }

        return source.Count > 0 ? totalDistance / source.Count : float.PositiveInfinity;
    }
}
