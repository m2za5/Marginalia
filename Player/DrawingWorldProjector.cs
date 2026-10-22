using UnityEngine;

public enum DrawingWorldProjectionFailure
{
    None = 0,
    NoCamera = 1,
    InvalidRange = 2,
    NoValidSurface = 3,
    InvalidSurface = 4,
    OutOfRange = 5,
    Occluded = 6
}

public readonly struct DrawingWorldProjectionResult
{
    public DrawingWorldProjectionResult(
        Vector3 position,
        Vector3 normal,
        Collider surface,
        float planarDistance)
    {
        Succeeded = true;
        Failure = DrawingWorldProjectionFailure.None;
        Position = position;
        Normal = normal;
        Surface = surface;
        PlanarDistance = planarDistance;
    }

    public DrawingWorldProjectionResult(DrawingWorldProjectionFailure failure)
    {
        Succeeded = false;
        Failure = failure;
        Position = Vector3.zero;
        Normal = Vector3.up;
        Surface = null;
        PlanarDistance = 0f;
    }

    public bool Succeeded { get; }
    public DrawingWorldProjectionFailure Failure { get; }
    public Vector3 Position { get; }
    public Vector3 Normal { get; }
    public Collider Surface { get; }
    public float PlanarDistance { get; }
}

[DisallowMultipleComponent]
public sealed class DrawingWorldProjector : MonoBehaviour
{
    private const int HitBufferSize = 32;

    [Header("References")]
    [SerializeField] private Camera projectionCamera;
    [SerializeField] private Transform ignoreRoot;

    [Header("Surface")]
    [SerializeField] private LayerMask surfaceLayers = ~0;
    [SerializeField, Min(1f)] private float rayDistance = 500f;
    [SerializeField, Range(0f, 1f)] private float minimumSurfaceNormalY = 0.5f;
    [SerializeField] private bool ignoreDamageableColliders = true;

    [Header("Occlusion")]
    [SerializeField] private LayerMask occlusionLayers = ~0;
    [SerializeField, Min(0f)] private float occlusionEndPadding = 0.05f;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[HitBufferSize];

    public LayerMask SurfaceLayers => surfaceLayers;
    public LayerMask OcclusionLayers => occlusionLayers;
    public float RayDistance => rayDistance;
    public float MinimumSurfaceNormalY => minimumSurfaceNormalY;

    public bool TryProject(
        Vector2 screenPoint,
        Vector3 origin,
        float maximumPlanarDistance,
        out DrawingWorldProjectionResult result)
    {
        Camera camera = ResolveCamera();

        if (camera == null)
        {
            result = new DrawingWorldProjectionResult(DrawingWorldProjectionFailure.NoCamera);
            return false;
        }

        if (maximumPlanarDistance <= 0f
            || float.IsNaN(maximumPlanarDistance)
            || float.IsInfinity(maximumPlanarDistance))
        {
            result = new DrawingWorldProjectionResult(DrawingWorldProjectionFailure.InvalidRange);
            return false;
        }

        Ray ray = camera.ScreenPointToRay(screenPoint);

        if (!TryFindNearestSurfaceCandidate(ray, out RaycastHit candidate))
        {
            result = new DrawingWorldProjectionResult(DrawingWorldProjectionFailure.NoValidSurface);
            return false;
        }

        if (candidate.normal.y < minimumSurfaceNormalY)
        {
            result = new DrawingWorldProjectionResult(DrawingWorldProjectionFailure.InvalidSurface);
            return false;
        }

        if (IsOccluded(ray, candidate))
        {
            result = new DrawingWorldProjectionResult(DrawingWorldProjectionFailure.Occluded);
            return false;
        }

        Vector3 planarDelta = candidate.point - origin;
        planarDelta.y = 0f;
        float planarDistance = planarDelta.magnitude;

        if (planarDistance > maximumPlanarDistance)
        {
            result = new DrawingWorldProjectionResult(DrawingWorldProjectionFailure.OutOfRange);
            return false;
        }

        result = new DrawingWorldProjectionResult(
            candidate.point,
            candidate.normal,
            candidate.collider,
            planarDistance);
        return true;
    }

    private bool TryFindNearestSurfaceCandidate(Ray ray, out RaycastHit nearest)
    {
        nearest = default;
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            hitBuffer,
            Mathf.Max(1f, rayDistance),
            surfaceLayers,
            QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hitBuffer[i];

            if (ShouldIgnoreCollider(hit.collider) || hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            nearest = hit;
        }

        return nearestDistance < float.PositiveInfinity;
    }

    private bool IsOccluded(Ray ray, RaycastHit surface)
    {
        float checkDistance = surface.distance - occlusionEndPadding;

        if (checkDistance <= 0f)
        {
            return false;
        }

        int hitCount = Physics.RaycastNonAlloc(
            ray,
            hitBuffer,
            checkDistance,
            occlusionLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider blocker = hitBuffer[i].collider;

            if (blocker == null || blocker == surface.collider || ShouldIgnoreCollider(blocker))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool ShouldIgnoreCollider(Collider candidate)
    {
        if (candidate == null)
        {
            return true;
        }

        Transform root = ignoreRoot != null ? ignoreRoot : transform;

        if (candidate.transform == root || candidate.transform.IsChildOf(root))
        {
            return true;
        }

        return ignoreDamageableColliders
            && candidate.GetComponentInParent<IDamageable>() != null;
    }

    private Camera ResolveCamera()
    {
        return projectionCamera != null ? projectionCamera : Camera.main;
    }

    private void OnValidate()
    {
        rayDistance = Mathf.Max(1f, rayDistance);
        minimumSurfaceNormalY = Mathf.Clamp01(minimumSurfaceNormalY);
        occlusionEndPadding = Mathf.Max(0f, occlusionEndPadding);
    }
}
