using UnityEngine;

[DisallowMultipleComponent]
public sealed class ShopCameraFocus : MonoBehaviour
{
    private enum FocusState
    {
        Idle,
        Entering,
        Holding,
        Returning
    }

    [Header("Framing")]
    [Tooltip("UI area the owner should occupy on screen. Falls back to Fallback Viewport Rect when empty.")]
    [SerializeField] private RectTransform framingArea;
    [SerializeField] private Rect fallbackViewportRect = new Rect(0.02f, 0.05f, 0.32f, 0.9f);
    [Tooltip("Fraction of the framing area's height the owner's sprite fills.")]
    [SerializeField, Range(0.2f, 1f)] private float heightFill = 0.9f;
    [Tooltip("World-space offset added to the owner's visual centre.")]
    [SerializeField] private Vector3 targetOffset = Vector3.zero;
    [SerializeField] private bool overridePitch = true;
    [SerializeField, Range(-10f, 60f)] private float focusPitch = 12f;
    [Tooltip("0 keeps the current field of view (perspective) or size (orthographic) and only moves the camera.")]
    [SerializeField, Min(0f)] private float focusFieldOfView;
    [SerializeField, Min(0.1f)] private float minimumDistance = 1f;

    [Header("Transition")]
    [SerializeField, Min(0.01f)] private float enterSeconds = 0.6f;
    [SerializeField, Min(0.01f)] private float exitSeconds = 0.5f;

    private FocusState state = FocusState.Idle;
    private Camera controlledCamera;
    private FixedAngleFollowCamera followCamera;
    private CameraBoundsClamp cameraBoundsClamp;
    private bool followCameraWasEnabled;
    private bool cameraBoundsWasEnabled;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float originalFieldOfView;
    private float originalOrthographicSize;

    private Transform focusTarget;
    private Renderer focusRenderer;
    private Vector2 focusSize;
    private Vector3 focusLocalCentre;
    private bool hasLocalCentre;
    private Vector3 fromPosition;
    private Quaternion fromRotation;
    private float fromLens;
    private float elapsed;

    public bool IsActive => state != FocusState.Idle;

    public void Focus(Component owner)
    {
        if (owner == null) return;

        if (state == FocusState.Idle && !TryTakeCamera()) return;

        focusTarget = owner.transform;
        focusRenderer = owner.GetComponentInChildren<Renderer>();
        focusSize = MeasureTarget();
        BeginTransition(FocusState.Entering);
    }

    public void Release()
    {
        if (state == FocusState.Idle || state == FocusState.Returning) return;

        if (controlledCamera == null)
        {
            RestoreCamera(false);
            return;
        }

        BeginTransition(FocusState.Returning);
    }

    private void LateUpdate()
    {
        if (state == FocusState.Idle) return;

        if (controlledCamera == null)
        {
            RestoreCamera(false);
            return;
        }

        float duration = state == FocusState.Returning ? exitSeconds : enterSeconds;
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
        float eased = t * t * (3f - 2f * t);

        Vector3 toPosition;
        Quaternion toRotation;
        float toLens;

        if (state == FocusState.Returning)
        {
            GetReturnPose(out toPosition, out toRotation, out toLens);
        }
        else if (!TryGetFocusPose(out toPosition, out toRotation, out toLens))
        {
            // Owner disappeared: hold the current pose until the shop closes.
            toPosition = controlledCamera.transform.position;
            toRotation = controlledCamera.transform.rotation;
            toLens = GetLens();
        }

        if (state == FocusState.Holding)
        {
            ApplyPose(toPosition, toRotation, toLens);
            return;
        }

        ApplyPose(
            Vector3.LerpUnclamped(fromPosition, toPosition, eased),
            Quaternion.SlerpUnclamped(fromRotation, toRotation, eased),
            Mathf.LerpUnclamped(fromLens, toLens, eased));

        if (t < 1f) return;

        if (state == FocusState.Entering) state = FocusState.Holding;
        else RestoreCamera(false);
    }

    private bool TryTakeCamera()
    {
        controlledCamera = Camera.main;
        if (controlledCamera == null)
        {
            Debug.LogWarning("[ShopCameraFocus] Main Camera not found.", this);
            return false;
        }

        originalPosition = controlledCamera.transform.position;
        originalRotation = controlledCamera.transform.rotation;
        originalFieldOfView = controlledCamera.fieldOfView;
        originalOrthographicSize = controlledCamera.orthographicSize;

        followCamera = controlledCamera.GetComponent<FixedAngleFollowCamera>();
        if (followCamera != null)
        {
            followCameraWasEnabled = followCamera.enabled;
            followCamera.enabled = false;
        }

        cameraBoundsClamp = controlledCamera.GetComponent<CameraBoundsClamp>();
        if (cameraBoundsClamp != null)
        {
            cameraBoundsWasEnabled = cameraBoundsClamp.enabled;
            cameraBoundsClamp.enabled = false;
        }

        return true;
    }

    private void BeginTransition(FocusState next)
    {
        fromPosition = controlledCamera.transform.position;
        fromRotation = controlledCamera.transform.rotation;
        fromLens = GetLens();
        elapsed = 0f;
        state = next;
    }

    // Measures the visible sprite outline (sprite mesh vertices) rather than the full
    // sprite rect, so transparent padding does not shrink the framed owner.
    private Vector2 MeasureTarget()
    {
        hasLocalCentre = false;

        if (focusRenderer is SpriteRenderer sprite && sprite.sprite != null)
        {
            Vector2[] vertices = sprite.sprite.vertices;
            Vector2 min = vertices.Length > 0 ? vertices[0] : Vector2.zero;
            Vector2 max = min;
            foreach (Vector2 vertex in vertices)
            {
                min = Vector2.Min(min, vertex);
                max = Vector2.Max(max, vertex);
            }

            Vector2 centre = (min + max) * 0.5f;
            focusLocalCentre = new Vector3(sprite.flipX ? -centre.x : centre.x, sprite.flipY ? -centre.y : centre.y, 0f);
            hasLocalCentre = true;

            Vector3 scale = sprite.transform.lossyScale;
            return new Vector2(Mathf.Abs((max.x - min.x) * scale.x), Mathf.Abs((max.y - min.y) * scale.y));
        }

        if (focusRenderer != null)
        {
            Vector3 size = focusRenderer.bounds.size;
            return new Vector2(Mathf.Max(size.x, size.z), size.y);
        }

        return Vector2.one;
    }

    private Vector3 GetFocusCentre()
    {
        if (hasLocalCentre && focusRenderer != null) return focusRenderer.transform.TransformPoint(focusLocalCentre);
        return focusRenderer != null ? focusRenderer.bounds.center : focusTarget.position;
    }

    private bool TryGetFocusPose(out Vector3 position, out Quaternion rotation, out float lens)
    {
        position = default;
        rotation = default;
        lens = GetLens();

        if (focusTarget == null) return false;

        Vector3 centre = GetFocusCentre() + targetOffset;
        Vector3 euler = originalRotation.eulerAngles;
        rotation = Quaternion.Euler(overridePitch ? focusPitch : euler.x, euler.y, 0f);

        Rect area = GetFramingViewportRect();
        float aspect = Mathf.Max(0.01f, controlledCamera.aspect);
        float screenHeight = Mathf.Max(0.05f, area.height * heightFill);
        float screenWidth = Mathf.Max(0.05f, area.width * heightFill);
        Vector2 viewportCentre = area.center;

        if (controlledCamera.orthographic)
        {
            lens = focusFieldOfView > 0f ? focusFieldOfView : originalOrthographicSize;
            float requiredSize = Mathf.Max(
                focusSize.y / (2f * screenHeight),
                focusSize.x / (2f * screenWidth * aspect));
            if (focusFieldOfView <= 0f) lens = requiredSize;
            float halfHeight = lens;
            float halfWidth = halfHeight * aspect;
            Vector3 local = new Vector3((viewportCentre.x * 2f - 1f) * halfWidth, (viewportCentre.y * 2f - 1f) * halfHeight, Mathf.Max(minimumDistance, 10f));
            position = centre - rotation * local;
            return true;
        }

        lens = focusFieldOfView > 0f ? focusFieldOfView : originalFieldOfView;
        float tanHalf = Mathf.Tan(lens * 0.5f * Mathf.Deg2Rad);
        float distance = Mathf.Max(
            minimumDistance,
            focusSize.y / (2f * tanHalf * screenHeight),
            focusSize.x / (2f * tanHalf * aspect * screenWidth));
        Vector3 offset = new Vector3(
            (viewportCentre.x * 2f - 1f) * distance * tanHalf * aspect,
            (viewportCentre.y * 2f - 1f) * distance * tanHalf,
            distance);
        position = centre - rotation * offset;
        return true;
    }

    private Rect GetFramingViewportRect()
    {
        if (framingArea == null || Screen.width <= 0 || Screen.height <= 0) return fallbackViewportRect;

        Canvas canvas = framingArea.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector3[] corners = new Vector3[4];
        framingArea.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
        Rect rect = Rect.MinMaxRect(min.x / Screen.width, min.y / Screen.height, max.x / Screen.width, max.y / Screen.height);
        return rect.width > 0.01f && rect.height > 0.01f ? rect : fallbackViewportRect;
    }

    private void GetReturnPose(out Vector3 position, out Quaternion rotation, out float lens)
    {
        lens = controlledCamera.orthographic ? originalOrthographicSize : originalFieldOfView;

        if (followCamera != null && followCameraWasEnabled && followCamera.TryGetFollowPose(out position, out rotation))
        {
            return;
        }

        position = originalPosition;
        rotation = originalRotation;
    }

    private float GetLens()
    {
        return controlledCamera.orthographic ? controlledCamera.orthographicSize : controlledCamera.fieldOfView;
    }

    private void ApplyPose(Vector3 position, Quaternion rotation, float lens)
    {
        controlledCamera.transform.SetPositionAndRotation(position, rotation);
        if (controlledCamera.orthographic) controlledCamera.orthographicSize = lens;
        else controlledCamera.fieldOfView = lens;
    }

    private void RestoreCamera(bool snapToStoredPose)
    {
        if (controlledCamera != null)
        {
            if (snapToStoredPose) controlledCamera.transform.SetPositionAndRotation(originalPosition, originalRotation);
            controlledCamera.fieldOfView = originalFieldOfView;
            controlledCamera.orthographicSize = originalOrthographicSize;
        }

        if (cameraBoundsClamp != null) cameraBoundsClamp.enabled = cameraBoundsWasEnabled;

        if (followCamera != null)
        {
            followCamera.enabled = followCameraWasEnabled;
            if (followCameraWasEnabled) followCamera.SnapToTarget();
        }

        state = FocusState.Idle;
        controlledCamera = null;
        followCamera = null;
        cameraBoundsClamp = null;
        focusTarget = null;
        focusRenderer = null;
    }

    private void OnDisable()
    {
        if (state != FocusState.Idle) RestoreCamera(true);
    }

    private void OnValidate()
    {
        enterSeconds = Mathf.Max(0.01f, enterSeconds);
        exitSeconds = Mathf.Max(0.01f, exitSeconds);
        minimumDistance = Mathf.Max(0.1f, minimumDistance);
    }
}
