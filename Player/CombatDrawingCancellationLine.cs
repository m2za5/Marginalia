using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CombatDrawingCancellationLine : MonoBehaviour
{
    public static readonly Vector3 StrokeBreak = new Vector3(float.NaN, float.NaN, float.NaN);

    private readonly List<Vector3> worldAnchors = new List<Vector3>();
    private readonly List<Vector2> screenLine = new List<Vector2>();
    private readonly Dictionary<EnemyProjectile, Vector2> previousScreenPositions =
        new Dictionary<EnemyProjectile, Vector2>();

    private Camera viewCamera;
    private Action firstCancelSucceeded;
    private float halfWidthScreenFraction;
    private float endsAt;
    private bool initialized;

    public int CanceledCount { get; private set; }

    public void Initialize(
        Camera camera,
        IReadOnlyList<Vector3> anchors,
        float halfWidthFraction,
        float duration,
        Action onFirstCancelSucceeded = null)
    {
        viewCamera = camera;
        firstCancelSucceeded = onFirstCancelSucceeded;
        worldAnchors.Clear();
        worldAnchors.AddRange(anchors);
        halfWidthScreenFraction = Mathf.Max(0.001f, halfWidthFraction);
        endsAt = Time.unscaledTime + Mathf.Max(0.01f, duration);
        initialized = true;
        CancelProjectilesOnLine();
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        if (viewCamera == null || Time.unscaledTime >= endsAt)
        {
            Destroy(gameObject);
            return;
        }

        CancelProjectilesOnLine();
    }

    private void CancelProjectilesOnLine()
    {
        if (!TryUpdateScreenLine())
        {
            return;
        }

        float halfWidth = halfWidthScreenFraction * Mathf.Max(1f, viewCamera.pixelHeight);
        foreach (EnemyProjectile projectile in FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None))
        {
            if (projectile == null)
            {
                continue;
            }

            Vector3 worldPosition = projectile.transform.position;
            Vector3 screenPosition = viewCamera.WorldToScreenPoint(worldPosition);
            if (screenPosition.z <= 0f)
            {
                previousScreenPositions.Remove(projectile);
                continue;
            }
            Vector2 current = screenPosition;
            Vector2 previous = previousScreenPositions.TryGetValue(projectile, out Vector2 stored)
                ? stored
                : current;

            if (DistanceToLine(previous, current) <= halfWidth && projectile.Cancel())
            {
                previousScreenPositions.Remove(projectile);
                CanceledCount++;
                if (CanceledCount == 1)
                {
                    GunHoSfxBus.PlayAt(GunHoSfxCue.GuardBlock, worldPosition);
                    firstCancelSucceeded?.Invoke();
                }
                continue;
            }

            previousScreenPositions[projectile] = current;
        }
    }

    private bool TryUpdateScreenLine()
    {
        screenLine.Clear();
        foreach (Vector3 anchor in worldAnchors)
        {
            if (float.IsNaN(anchor.x))
            {
                screenLine.Add(DrawingResult.StrokeBreak);
                continue;
            }

            Vector3 screenPoint = viewCamera.WorldToScreenPoint(anchor);
            if (screenPoint.z <= 0f)
            {
                return false;
            }

            screenLine.Add(screenPoint);
        }

        return screenLine.Count > 0;
    }

    private float DistanceToLine(Vector2 from, Vector2 to)
    {
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < screenLine.Count; i++)
        {
            Vector2 start = screenLine[i];
            if (DrawingResult.IsStrokeBreak(start))
            {
                continue;
            }

            bool hasNext = i + 1 < screenLine.Count && !DrawingResult.IsStrokeBreak(screenLine[i + 1]);
            bool hasPrevious = i > 0 && !DrawingResult.IsStrokeBreak(screenLine[i - 1]);
            if (!hasNext && hasPrevious)
            {
                continue;
            }

            Vector2 end = hasNext ? screenLine[i + 1] : start;
            nearest = Mathf.Min(nearest, SegmentDistance(from, to, start, end));
        }

        return nearest;
    }

    private static float SegmentDistance(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        if (SegmentsIntersect(a0, a1, b0, b1))
        {
            return 0f;
        }

        return Mathf.Min(
            Mathf.Min(PointToSegmentDistance(a0, b0, b1), PointToSegmentDistance(a1, b0, b1)),
            Mathf.Min(PointToSegmentDistance(b0, a0, a1), PointToSegmentDistance(b1, a0, a1)));
    }

    private static float PointToSegmentDistance(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        float t = lengthSquared > 0.0001f
            ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared)
            : 0f;
        return Vector2.Distance(point, start + segment * t);
    }

    private static bool SegmentsIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        float d1 = Cross(b1 - b0, a0 - b0);
        float d2 = Cross(b1 - b0, a1 - b0);
        float d3 = Cross(a1 - a0, b0 - a0);
        float d4 = Cross(a1 - a0, b1 - a0);
        return (d1 > 0f) != (d2 > 0f) && (d3 > 0f) != (d4 > 0f);
    }

    private static float Cross(Vector2 left, Vector2 right)
    {
        return left.x * right.y - left.y * right.x;
    }
}
