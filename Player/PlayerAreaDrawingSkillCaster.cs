using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum EnvironmentDrawingHandleResult
{
    NotHandled,
    Succeeded,
    Failed
}

public interface IEnvironmentDrawingHandler
{
    bool CanAcceptEnvironmentDrawing { get; }
    EnvironmentDrawingHandleResult HandleEnvironmentDrawing(DrawingResult drawing);
}

// 인식된 문양에 맞춰 잉크 소모, 쿨타임, 실제 스킬 발동을 처리함.
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerDrawingSkill), typeof(PlayerCombatDrawingLoadout), typeof(PlayerCombatInk))]
public sealed class PlayerAreaDrawingSkillCaster : MonoBehaviour
{
    private const int HeartJudgmentMaximumTargets = 5;
    private const float HeartJudgmentCastDelay = 1f;
    private const float HeartJudgmentStunDuration = 3f;

    private sealed class SkillIconBinding
    {
        public CombatDrawingSkillDefinition Skill;
        public Image Background;
        public TMP_Text CooldownText;
    }

    [SerializeField] private Transform castOrigin;
    [SerializeField] private LayerMask projectileTargetLayers = ~0;
    [Header("World Projection")]
    [SerializeField] private DrawingWorldProjector worldProjector;
    [Header("World Placement Test")]
    [SerializeField, Min(0f)] private float placementSurfaceOffset = 0.02f;
    [Header("World Placement Drawing Size")]
    [SerializeField, Range(0.01f, 1f)] private float minimumPlacementDrawingSize = 0.15f;
    [SerializeField, Range(0.01f, 1f)] private float maximumPlacementDrawingSize = 0.65f;
    [SerializeField, Min(0.01f)] private float minimumPlacementEffectScale = 0.75f;
    [SerializeField, Min(0.01f)] private float maximumPlacementEffectScale = 2.5f;
    [SerializeField, Min(0)] private int minimumPlacementInkCost = 1;
    [SerializeField, Min(0)] private int maximumPlacementInkCost = 5;
    [Header("Cancellation Line")]
    [Tooltip("취소선 판정 띠의 반폭. 화면 높이 대비 비율(0.035 ≈ 1080p 기준 38px).")]
    [SerializeField, Range(0.005f, 0.2f)] private float cancellationLineHalfWidth = 0.035f;
    private readonly List<CombatDrawingSkillDefinition> candidates = new List<CombatDrawingSkillDefinition>();
    private readonly RaycastHit[] drawnPointHits = new RaycastHit[16];
    private PlayerDrawingSkill drawingSkill;
    private PlayerCombatDrawingLoadout loadout;
    private PlayerCombatInk mana;
    private PlayerTimeScaleController timeScaleController;
    private PlayerCancellationGuard cancellationGuard;
    private StatusEffectController statusEffects;
    private bool awaitingDrawing;
    private IEnvironmentDrawingHandler environmentDrawingHandler;
    private CombatDrawingSkillDefinition cancellationSkillAwaitingRefund;
    private readonly Dictionary<CombatDrawingSkillDefinition, float> cooldownEnds =
        new Dictionary<CombatDrawingSkillDefinition, float>();
    private readonly List<SkillIconBinding> skillIcons = new List<SkillIconBinding>();
    private RectTransform skillIconRoot;
    private Canvas skillStatusCanvas;
    private TMP_FontAsset uiFont;

    public bool HasActiveEnvironmentDrawing =>
        environmentDrawingHandler != null
        && environmentDrawingHandler.CanAcceptEnvironmentDrawing;

    private void Awake()
    {
        drawingSkill = GetComponent<PlayerDrawingSkill>();
        loadout = GetComponent<PlayerCombatDrawingLoadout>();
        mana = GetComponent<PlayerCombatInk>();
        timeScaleController = GetComponent<PlayerTimeScaleController>();
        statusEffects = GetComponent<StatusEffectController>();
        statusEffects = statusEffects != null
            ? statusEffects
            : gameObject.AddComponent<StatusEffectController>();
        cancellationGuard = GetComponent<PlayerCancellationGuard>();
        cancellationGuard = cancellationGuard != null
            ? cancellationGuard
            : gameObject.AddComponent<PlayerCancellationGuard>();
        castOrigin = castOrigin != null ? castOrigin : transform;
        worldProjector = worldProjector != null
            ? worldProjector
            : GetComponent<DrawingWorldProjector>();
        uiFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Hakgyoansim_NalgaeR SDF");
        BuildSkillStatusUI();
    }

    private void OnEnable()
    {
        DebugUIVisibilityController.VisibilityChanged += HandleDebugUIVisibilityChanged;

        if (loadout != null)
        {
            loadout.SkillsChanged -= BuildSkillStatusUI;
            loadout.SkillsChanged += BuildSkillStatusUI;
        }
        cancellationGuard.BlockSucceeded -= HandleCancellationBlockSucceeded;
        cancellationGuard.BlockSucceeded += HandleCancellationBlockSucceeded;
    }

    private void Update()
    {
        if (DebugUIVisibilityController.IsVisible)
        {
            UpdateSkillStatusUI();
        }
    }

    public bool TryBeginDrawing(PlayerController controller, Vector2 screenPosition)
    {
        if (awaitingDrawing
            || controller == null
            || controller.IsDrawingSkillActive
            || !controller.IsInCombat
            || mana == null
            || !mana.CanUseCombatDrawing
            || (statusEffects != null && statusEffects.IsAttackRestricted))
        {
            return false;
        }

        candidates.Clear();
        foreach (CombatDrawingSkillDefinition skill in loadout.UnlockedSkills)
        {
            if (skill != null)
            {
                candidates.Add(skill);
            }
        }

        if (candidates.Count == 0 || !controller.RequestCombatDrawingSkillStart(screenPosition))
        {
            return false;
        }

        drawingSkill.DrawingCompleted -= HandleDrawingCompleted;
        drawingSkill.DrawingCompleted += HandleDrawingCompleted;
        awaitingDrawing = true;
        return true;
    }

    public bool TryBeginEnvironmentDrawing(PlayerController controller, Vector2 screenPosition)
    {
        if (!HasActiveEnvironmentDrawing
            || controller == null
            || !controller.IsInCombat
            || mana == null
            || !mana.CanUseCombatDrawing
            || (statusEffects != null && statusEffects.IsAttackRestricted))
        {
            return false;
        }

        candidates.Clear();
        foreach (CombatDrawingSkillDefinition skill in loadout.UnlockedSkills)
        {
            if (skill != null)
            {
                candidates.Add(skill);
            }
        }

        if (!controller.RequestCombatDrawingSkillStart(screenPosition))
        {
            return false;
        }

        drawingSkill.DrawingCompleted -= HandleDrawingCompleted;
        drawingSkill.DrawingCompleted += HandleDrawingCompleted;
        awaitingDrawing = true;
        return true;
    }

    public bool TrySetEnvironmentDrawingHandler(IEnvironmentDrawingHandler handler)
    {
        if (handler == null
            || (environmentDrawingHandler != null && environmentDrawingHandler != handler))
        {
            return false;
        }

        environmentDrawingHandler = handler;
        return true;
    }

    public void ClearEnvironmentDrawingHandler(IEnvironmentDrawingHandler handler)
    {
        if (environmentDrawingHandler == handler)
        {
            environmentDrawingHandler = null;
        }
    }

    private void HandleDrawingCompleted(DrawingResult drawing)
    {
        drawingSkill.DrawingCompleted -= HandleDrawingCompleted;
        awaitingDrawing = false;
        if (HasActiveEnvironmentDrawing)
        {
            EnvironmentDrawingHandleResult environmentResult =
                environmentDrawingHandler.HandleEnvironmentDrawing(drawing);
            if (environmentResult != EnvironmentDrawingHandleResult.NotHandled)
            {
                ShowEnvironmentFeedback(drawing, environmentResult);
                return;
            }
        }

        CombatDrawingRecognitionResult result = CombatDrawingGestureRecognizer.Recognize(drawing, candidates);
        
        if (result.IsRecognized && result.Skill.IsInkSlashSkill)
        {
            InkSlashDebugTrace.BeginCast();
        }
        InkSlashDebugTrace.Log("1.RECOGNIZE",
            $"skill={(result.IsRecognized ? result.Skill.SkillId : "none")} confidence={result.Confidence:F3} "
            + $"points={drawing?.ScreenPoints?.Count ?? 0} raw=[{InkSlashDebugTrace.DescribePoints(drawing?.ScreenPoints)}]",
            this);

        if (!result.IsRecognized)
        {
            Debug.Log(
                $"[CombatDrawing] Recognition failed. pattern='{result.Evaluation?.PatternId}', "
                + $"confidence={result.Confidence:F3}, runnerUp={result.RunnerUpConfidence:F3}, "
                + $"points={result.Evaluation?.ValidPointCount ?? 0}.",
                this);
            ShowDrawingFeedback(result.Evaluation);
            return;
        }

        if (result.Skill.IsHeartProjectileSkill)
        {
            ShowCastFeedback(result.Evaluation, TryCastHeartProjectile(result.Skill));
            return;
        }

        if (result.Skill.IsTimeSlowSkill)
        {
            ShowCastFeedback(result.Evaluation, TryCastTimeSlow(result.Skill));
            return;
        }

        if (result.Skill.EffectKind == CombatDrawingEffectKind.CancellationLine)
        {
            ShowCastFeedback(result.Evaluation, TryCastCancellationLine(result.Skill, drawing));
            return;
        }

        if (result.Skill.IsWorldPlacementTestSkill)
        {
            ShowCastFeedback(result.Evaluation, TryCastWorldPlacementTest(result.Skill, drawing));
            return;
        }

        if (result.Skill.IsInkSlashSkill)
        {
            ShowCastFeedback(result.Evaluation, TryCastInkSlash(result.Skill, drawing));
            return;
        }

        if (result.Skill.IsInkEruptionSkill)
        {
            ShowCastFeedback(result.Evaluation, TryCastInkEruption(result.Skill, drawing));
            return;
        }

        if (!mana.TrySpend(result.Skill.ManaCost))
        {
            ShowCastFeedback(result.Evaluation, false);
            return;
        }

        GameObject effect = result.Skill.EffectPrefab != null
            ? Instantiate(result.Skill.EffectPrefab, castOrigin.position, Quaternion.identity)
            : new GameObject(result.Skill.DisplayName + " Effect");
        effect.transform.position = castOrigin.position;
        CombatDrawingAreaEffect area = effect.GetComponent<CombatDrawingAreaEffect>();
        area = area != null ? area : effect.AddComponent<CombatDrawingAreaEffect>();
        area.Initialize(result.Skill, gameObject);
        loadout.NotifySkillApplied(result.Skill);
        ShowCastFeedback(result.Evaluation, true);
    }

    private void ShowCastFeedback(DrawingEvaluation evaluation, bool applied)
    {
        // Recognition alone is not a successful cast (cooldown, ink or placement may reject it).
        ShowDrawingFeedback(applied ? evaluation : new DrawingEvaluation(
            DrawingEvaluationStatus.InvalidPattern, evaluation.PatternId,
            evaluation.MatchedCheckpointCount, evaluation.TotalCheckpointCount, evaluation.ValidPointCount));
    }

    private void ShowEnvironmentFeedback(DrawingResult drawing, EnvironmentDrawingHandleResult result)
    {
        int pointCount = 0;
        if (drawing != null)
            foreach (Vector2 point in drawing.ScreenPoints)
                if (!DrawingResult.IsStrokeBreak(point)) pointCount++;
        DrawingEvaluationStatus status = drawing != null && drawing.Canceled
            ? DrawingEvaluationStatus.Canceled
            : pointCount == 0 ? DrawingEvaluationStatus.Empty
            : result == EnvironmentDrawingHandleResult.Succeeded
                ? DrawingEvaluationStatus.Success : DrawingEvaluationStatus.Incomplete;
        ShowDrawingFeedback(new DrawingEvaluation(status, string.Empty, 0, 0, pointCount));
    }

    private void ShowDrawingFeedback(DrawingEvaluation evaluation)
    {
        // Combat/environment sessions bypass DrawingPatternTarget; reuse its existing presenter.
        GetComponent<DrawingResultFeedbackPresenter>()?.ShowEvaluation(evaluation);
    }

    private bool TryCastTimeSlow(CombatDrawingSkillDefinition skill)
    {
        if (timeScaleController == null)
        {
            return false;
        }

        if (timeScaleController.IsTemporalSkillActive)
        {
            return timeScaleController.TryCancelTemporalSkill(skill);
        }

        if (GetRemainingCooldown(skill) > 0f
            || mana.CurrentInk < skill.ManaCost
            || !timeScaleController.TryActivateTemporalSkill(skill))
        {
            return false;
        }

        if (!mana.TrySpend(skill.ManaCost))
        {
            timeScaleController.ClearAllEffects();
            return false;
        }

        cooldownEnds[skill] = Time.unscaledTime + Mathf.Max(0f, skill.Cooldown);
        GunHoSfxBus.Play(GunHoSfxCue.TemporalStart);
        loadout.NotifySkillApplied(skill);
        return true;
    }

    private bool TryCastHeartProjectile(CombatDrawingSkillDefinition skill)
    {
        List<Health> targets = FindNearestTargets(skill.Range, HeartJudgmentMaximumTargets);

        if (targets.Count == 0
            || GetRemainingCooldown(skill) > 0f
            || !mana.TrySpend(skill.ManaCost))
        {
            return false;
        }

        foreach (Health target in targets)
        {
            GameObject effect = skill.EffectPrefab != null
                ? Instantiate(skill.EffectPrefab, castOrigin.position, Quaternion.identity)
                : new GameObject(skill.DisplayName + " Effect");
            effect.transform.position = castOrigin.position;
            effect.transform.localScale = Vector3.one;
            CombatDrawingHeartProjectile projectile = effect.GetComponent<CombatDrawingHeartProjectile>();
            projectile = projectile != null ? projectile : effect.AddComponent<CombatDrawingHeartProjectile>();
            projectile.Initialize(
                skill,
                gameObject,
                target,
                HeartJudgmentCastDelay,
                HeartJudgmentStunDuration);
        }

        cooldownEnds[skill] = Time.unscaledTime + Mathf.Max(0f, skill.Cooldown);
        StartCoroutine(LockMovementForHeartCast());
        loadout.NotifySkillApplied(skill);
        return true;
    }

    private bool TryCastWorldPlacementTest(
        CombatDrawingSkillDefinition skill,
        DrawingResult drawing)
    {
        if (!CanUseCombatSkill(skill))
        {
            Debug.LogWarning("[WorldPlacementTest] Skill is unavailable.", this);
            return false;
        }

        if (skill.EffectPrefab == null)
        {
            Debug.LogError("[WorldPlacementTest] Effect prefab is missing.", this);
            return false;
        }

        if (!TryGetDrawingMetrics(drawing, out Vector2 screenPoint, out float drawingSize))
        {
            Debug.LogWarning("[WorldPlacementTest] Drawing has no valid screen points.", this);
            return false;
        }

        float sizeProgress = Mathf.InverseLerp(
            minimumPlacementDrawingSize,
            Mathf.Max(minimumPlacementDrawingSize, maximumPlacementDrawingSize),
            drawingSize);
        float effectScale = Mathf.Lerp(
            minimumPlacementEffectScale,
            maximumPlacementEffectScale,
            sizeProgress);
        int inkCost = Mathf.RoundToInt(Mathf.Lerp(
            minimumPlacementInkCost,
            maximumPlacementInkCost,
            sizeProgress));

        if (mana == null || mana.CurrentInk + Mathf.Epsilon < inkCost)
        {
            Debug.LogWarning(
                $"[WorldPlacementTest] Not enough ink. required={inkCost}, "
                + $"current={mana?.CurrentInk ?? 0f:F2}, drawingSize={drawingSize:F3}.",
                this);
            return false;
        }

        if (!TryProjectToWorld(skill, screenPoint, out DrawingWorldProjectionResult projection))
        {
            return false;
        }

        if (inkCost > 0 && !mana.TrySpend(inkCost))
        {
            Debug.LogWarning("[WorldPlacementTest] Ink spend failed.", this);
            return false;
        }

        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, projection.Normal);
        GameObject effect = Instantiate(
            skill.EffectPrefab,
            projection.Position + projection.Normal * placementSurfaceOffset,
            rotation);
        effect.name = skill.EffectPrefab.name + " (Drawing Placement)";
        effect.transform.localScale *= effectScale;
        Debug.Log(
            $"[WorldPlacementTest] Spawned '{effect.name}' at {effect.transform.position} "
            + $"from screen point {screenPoint} on '{projection.Surface.name}'. "
            + $"drawingSize={drawingSize:F3}, scale={effectScale:F2}, ink={inkCost}, "
            + $"distance={projection.PlanarDistance:F2}/{skill.Range:F2}.",
            effect);
        cooldownEnds[skill] = Time.unscaledTime + Mathf.Max(0f, skill.Cooldown);

        loadout.NotifySkillApplied(skill);

        if (skill.SpawnedEffectLifetime > 0f)
        {
            Destroy(effect, skill.SpawnedEffectLifetime);
        }

        return true;
    }

    private bool TryCastInkSlash(
        CombatDrawingSkillDefinition skill,
        DrawingResult drawing)
    {
        if (!CanUseCombatSkill(skill))
        {
            Debug.LogWarning("[InkSlash] Skill is unavailable (cooldown or ink).", this);
            return false;
        }

        if (skill.EffectPrefab == null)
        {
            Debug.LogError("[InkSlash] Effect prefab is missing.", this);
            return false;
        }

        if (!TryGetDrawingMetrics(drawing, out Vector2 screenPoint, out _))
        {
            Debug.LogWarning("[InkSlash] Drawing has no valid screen points.", this);
            return false;
        }

        Camera viewCamera = Camera.main;
        if (viewCamera == null)
        {
            return false;
        }

        Vector3 origin = castOrigin.position;
        // 그린 점 아래에 실제로 보이는 몸체·지면을 먼저 조준한다. 발 높이 평면만 쓰면 몸체 위에 그린 점이
        // 카메라 반대쪽으로 밀려, 플레이어 뒤(카메라 쪽) 몬스터를 노려도 앞쪽으로 뒤집힌다.
        bool aimedAtVisibleSurface = TryRaycastDrawnPoint(viewCamera, screenPoint, out Vector3 aimPoint, out Collider aimCollider);
        bool aimedAtCastPlane = !aimedAtVisibleSurface && TryProjectToCastPlane(viewCamera, screenPoint, out aimPoint);
        Vector3 aimDirection = aimedAtVisibleSurface || aimedAtCastPlane
            ? aimPoint - origin
            : viewCamera.ScreenPointToRay(screenPoint).direction;
        aimDirection.y = 0f;

        // [InkSlashTrace] 임시 진단: 조준 화면 좌표 → 월드 투영 → 최종 방향
        bool traceCastPlane = TryProjectToCastPlane(viewCamera, screenPoint, out Vector3 traceCastPlanePoint);
        InkSlashDebugTrace.Log("2.AIM",
            $"screenPoint(bboxCenter)={screenPoint} screen={Screen.width}x{Screen.height} "
            + $"camera={viewCamera.name} camPos={viewCamera.transform.position:F2} camFwd={viewCamera.transform.forward:F2} "
            + $"camPixelRect={viewCamera.pixelRect} camTargetTexture={(viewCamera.targetTexture != null ? viewCamera.targetTexture.name : "none")} "
            + $"underCursor={(aimedAtVisibleSurface ? aimCollider.name : "none")} aimPoint={aimPoint:F2} "
            + $"castPlanePoint(old)={(traceCastPlane ? traceCastPlanePoint.ToString("F2") : "none")} origin={origin:F2} "
            + $"aimDirection={aimDirection.normalized:F2} playerForward={transform.forward:F2}",
            this);
        InkSlashDebugTrace.Line(viewCamera.transform.position, aimPoint, Color.yellow);
        InkSlashDebugTrace.Line(origin, origin + aimDirection.normalized * 3f, Color.cyan);

        if (aimDirection.sqrMagnitude <= 0.0001f)
        {
            Debug.LogWarning("[InkSlash] Target point is on top of the player; no direction.", this);
            return false;
        }

        if (skill.ManaCost > 0 && !mana.TrySpend(skill.ManaCost))
        {
            return false;
        }

        GameObject effect = Instantiate(
            skill.EffectPrefab,
            origin,
            Quaternion.LookRotation(aimDirection.normalized, Vector3.up));
        effect.name = skill.EffectPrefab.name + " (Ink Slash)";
        CombatDrawingInkSlash slash = effect.GetComponent<CombatDrawingInkSlash>();
        slash = slash != null ? slash : effect.AddComponent<CombatDrawingInkSlash>();
        slash.Initialize(skill, gameObject, origin,
            origin + Vector3.ClampMagnitude(aimDirection, skill.Range));

        cooldownEnds[skill] = Time.unscaledTime + Mathf.Max(0f, skill.Cooldown);
        loadout.NotifySkillApplied(skill);
        return true;
    }

    // 먹빛 파열: 그린 위치의 바닥에 이펙트를 놓고 지연/피해/수명은 CombatDrawingInkEruption이 처리
    private bool TryCastInkEruption(
        CombatDrawingSkillDefinition skill,
        DrawingResult drawing)
    {
        if (!CanUseCombatSkill(skill))
        {
            Debug.LogWarning("[InkEruption] Skill is unavailable (cooldown or ink).", this);
            return false;
        }

        if (skill.EffectPrefab == null)
        {
            Debug.LogError("[InkEruption] Effect prefab is missing.", this);
            return false;
        }

        if (!TryGetDrawingMetrics(drawing, out Vector2 screenPoint, out _))
        {
            Debug.LogWarning("[InkEruption] Drawing has no valid screen points.", this);
            return false;
        }

        if (!TryProjectToWorld(skill, screenPoint, out DrawingWorldProjectionResult projection))
        {
            return false;
        }

        if (skill.ManaCost > 0 && !mana.TrySpend(skill.ManaCost))
        {
            return false;
        }

        GameObject effect = Instantiate(
            skill.EffectPrefab,
            projection.Position + projection.Normal * placementSurfaceOffset,
            Quaternion.identity);
        effect.name = skill.EffectPrefab.name + " (Ink Eruption)";
        CombatDrawingInkEruption eruption = effect.GetComponent<CombatDrawingInkEruption>();
        eruption = eruption != null ? eruption : effect.AddComponent<CombatDrawingInkEruption>();
        eruption.Initialize(skill, gameObject, projection.Position);

        cooldownEnds[skill] = Time.unscaledTime + Mathf.Max(0f, skill.Cooldown);
        loadout.NotifySkillApplied(skill);
        return true;
    }

    private static bool TryGetDrawingMetrics(
        DrawingResult drawing,
        out Vector2 center,
        out float size)
    {
        center = Vector2.zero;
        size = 0f;
        if (drawing == null || drawing.ScreenPoints == null) return false;

        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        int validPointCount = 0;

        foreach (Vector2 point in drawing.ScreenPoints)
        {
            if (DrawingResult.IsStrokeBreak(point)) continue;
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
            validPointCount++;
        }

        if (validPointCount == 0) return false;
        center = (minimum + maximum) * 0.5f;
        Vector2 screenSize = new Vector2(
            Mathf.Max(1f, Screen.width),
            Mathf.Max(1f, Screen.height));
        Vector2 normalizedBounds = Vector2.Scale(maximum - minimum, new Vector2(
            1f / screenSize.x,
            1f / screenSize.y));
        size = Mathf.Sqrt(Mathf.Max(0f, normalizedBounds.x * normalizedBounds.y));
        return true;
    }

    private void OnValidate()
    {
        placementSurfaceOffset = Mathf.Max(0f, placementSurfaceOffset);
        minimumPlacementDrawingSize = Mathf.Clamp(minimumPlacementDrawingSize, 0.01f, 1f);
        maximumPlacementDrawingSize = Mathf.Clamp(
            maximumPlacementDrawingSize,
            minimumPlacementDrawingSize,
            1f);
        minimumPlacementEffectScale = Mathf.Max(0.01f, minimumPlacementEffectScale);
        maximumPlacementEffectScale = Mathf.Max(
            minimumPlacementEffectScale,
            maximumPlacementEffectScale);
        minimumPlacementInkCost = Mathf.Max(0, minimumPlacementInkCost);
        maximumPlacementInkCost = Mathf.Max(minimumPlacementInkCost, maximumPlacementInkCost);
    }

    // 그린 화면 점을 지나는 카메라 광선이 처음 닿는 보이는 콜라이더(플레이어 자신·트리거 제외)의 지점을 찾는다.
    // 거리 기준 대상 선택이 아니라 화면에서 그 점 아래에 실제로 보이는 것을 고른다.
    private bool TryRaycastDrawnPoint(
        Camera viewCamera,
        Vector2 screenPoint,
        out Vector3 worldPoint,
        out Collider hitCollider)
    {
        Ray ray = viewCamera.ScreenPointToRay(screenPoint);
        int count = Physics.RaycastNonAlloc(
            ray,
            drawnPointHits,
            viewCamera.farClipPlane,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        worldPoint = Vector3.zero;
        hitCollider = null;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = drawnPointHits[i];
            if (hit.collider == null
                || hit.collider.transform.IsChildOf(transform)
                || hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            worldPoint = hit.point;
            hitCollider = hit.collider;
        }

        return hitCollider != null;
    }

    // 화면 점을 castOrigin 높이의 수평면에 투영한다. Ink Slash 조준점과 취소선 고정점이 함께 쓴다.
    private bool TryProjectToCastPlane(Camera viewCamera, Vector2 screenPoint, out Vector3 worldPoint)
    {
        Ray ray = viewCamera.ScreenPointToRay(screenPoint);
        Plane plane = new Plane(Vector3.up, castOrigin.position);
        bool projected = plane.Raycast(ray, out float distance) && distance > 0f;
        worldPoint = projected ? ray.GetPoint(distance) : Vector3.zero;
        return projected;
    }

    private bool TryProjectToWorld(
        CombatDrawingSkillDefinition skill,
        Vector2 screenPoint,
        out DrawingWorldProjectionResult projection)
    {
        if (worldProjector == null)
        {
            projection = new DrawingWorldProjectionResult(DrawingWorldProjectionFailure.NoValidSurface);
            Debug.LogWarning(
                $"[CombatDrawing] '{name}' has no {nameof(DrawingWorldProjector)}. "
                + "Run Marginalia > GunHo > Player > Patch Drawing Skills.",
                this);
            return false;
        }

        if (worldProjector.TryProject(screenPoint, castOrigin.position, skill.Range, out projection))
        {
            return true;
        }

        Debug.LogWarning(
            $"[CombatDrawing] '{skill.DisplayName}' projection failed: {projection.Failure} "
            + $"at screen point {screenPoint}, range={skill.Range:F2}.",
            this);
        return false;
    }

    private List<Health> FindNearestTargets(float range, int maximumCount)
    {
        Collider[] hits = Physics.OverlapSphere(
            castOrigin.position,
            Mathf.Max(0f, range),
            projectileTargetLayers,
            QueryTriggerInteraction.Collide);
        List<Health> results = new List<Health>();

        foreach (Collider hit in hits)
        {
            Health candidate = hit != null ? hit.GetComponentInParent<Health>() : null;

            if (candidate == null
                || !candidate.IsAlive
                || !candidate.CanTakeDamage
                || candidate.transform.root == transform.root
                || candidate.transform.root.CompareTag("Player"))
            {
                continue;
            }

            if (!results.Contains(candidate))
            {
                results.Add(candidate);
            }
        }
        results.Sort((left, right) =>
            ((left.transform.position - castOrigin.position).sqrMagnitude)
            .CompareTo((right.transform.position - castOrigin.position).sqrMagnitude));
        if (results.Count > maximumCount)
        {
            results.RemoveRange(maximumCount, results.Count - maximumCount);
        }
        return results;
    }

    public bool CanUseCombatSkill(CombatDrawingSkillDefinition skill)
    {
        return skill != null
            && GetRemainingCooldown(skill) <= 0f
            && mana != null
            && mana.CurrentInk >= skill.ManaCost;
    }

    // 취소선: 그린 선 위치·방향의 적 투사체만 EffectDuration 동안 지운다(전방위 방어 없음).
    // 첫 투사체를 지우면 기존 방어 성공과 같이 쿨타임을 한 번 반환한다.
    private bool TryCastCancellationLine(
        CombatDrawingSkillDefinition skill,
        DrawingResult drawing)
    {
        if (!CanUseCombatSkill(skill))
        {
            Debug.LogWarning("[CancellationLine] Skill is unavailable (cooldown or ink).", this);
            return false;
        }

        Camera viewCamera = Camera.main;
        if (viewCamera == null || drawing == null || drawing.ScreenPoints == null)
        {
            return false;
        }

        List<Vector3> anchors = new List<Vector3>(drawing.ScreenPoints.Count);
        int validPointCount = 0;
        foreach (Vector2 point in drawing.ScreenPoints)
        {
            if (DrawingResult.IsStrokeBreak(point))
            {
                anchors.Add(CombatDrawingCancellationLine.StrokeBreak);
                continue;
            }

            if (!TryProjectToCastPlane(viewCamera, point, out Vector3 anchor))
            {
                Debug.LogWarning($"[CancellationLine] Projection failed at screen point {point}.", this);
                return false;
            }

            anchors.Add(anchor);
            validPointCount++;
        }

        if (validPointCount < 2)
        {
            Debug.LogWarning("[CancellationLine] Drawing has too few valid screen points.", this);
            return false;
        }

        if (skill.ManaCost > 0 && !mana.TrySpend(skill.ManaCost))
        {
            return false;
        }

        // Initialize가 시전 프레임에 바로 투사체를 지워 쿨타임을 반환할 수 있으므로 쿨타임을 먼저 건다.
        cooldownEnds[skill] = Time.unscaledTime + Mathf.Max(0f, skill.Cooldown);
        GameObject zone = new GameObject(skill.DisplayName + " (Cancellation Line)");
        zone.AddComponent<CombatDrawingCancellationLine>().Initialize(
            viewCamera,
            anchors,
            cancellationLineHalfWidth,
            skill.EffectDuration,
            () => RefundCancellationCooldown(skill));

        loadout.NotifySkillApplied(skill);
        return true;
    }

    private void HandleCancellationBlockSucceeded()
    {
        RefundCancellationCooldown(cancellationSkillAwaitingRefund);
    }


    private void RefundCancellationCooldown(CombatDrawingSkillDefinition skill)
    {
        if (skill == null) return;
        cooldownEnds.Remove(skill);
        cancellationSkillAwaitingRefund = null;
    }

    private IEnumerator LockMovementForHeartCast()
    {
        PlayerMovement movement = GetComponent<PlayerMovement>();
        movement?.SetMovementLocked(true);
        float endsAt = Time.unscaledTime + HeartJudgmentCastDelay;
        while (Time.unscaledTime < endsAt)
        {
            yield return null;
        }
        movement?.SetMovementLocked(false);
    }

    private void OnDisable()
    {
        DebugUIVisibilityController.VisibilityChanged -= HandleDebugUIVisibilityChanged;

        if (awaitingDrawing && drawingSkill != null)
        {
            drawingSkill.DrawingCompleted -= HandleDrawingCompleted;
        }
        awaitingDrawing = false;
        environmentDrawingHandler = null;

        if (loadout != null)
        {
            loadout.SkillsChanged -= BuildSkillStatusUI;
        }
        if (cancellationGuard != null)
        {
            cancellationGuard.BlockSucceeded -= HandleCancellationBlockSucceeded;
        }
    }

    public float GetRemainingCooldown(CombatDrawingSkillDefinition skill)
    {
        return skill != null && cooldownEnds.TryGetValue(skill, out float endTime)
            ? Mathf.Max(0f, endTime - Time.unscaledTime)
            : 0f;
    }

    private void BuildSkillStatusUI()
    {
        if (loadout == null)
        {
            return;
        }

        if (!DebugUIVisibilityController.IsVisible)
        {
            if (skillStatusCanvas != null)
            {
                skillStatusCanvas.gameObject.SetActive(false);
            }
            return;
        }

        if (skillIconRoot == null)
        {
            GameObject canvasObject = new GameObject(
                "CombatDrawingSkillStatusCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            
            skillStatusCanvas = canvasObject.GetComponent<Canvas>();
            skillStatusCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            skillStatusCanvas.overrideSorting = true;
            skillStatusCanvas.sortingOrder = 900;
            skillStatusCanvas.enabled = !RecordingMode.HideDebugOverlays;
            
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject rootObject = new GameObject(
                "SkillIcons",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup));
            rootObject.transform.SetParent(canvasObject.transform, false);
            skillIconRoot = rootObject.GetComponent<RectTransform>();
            skillIconRoot.anchorMin = skillIconRoot.anchorMax = new Vector2(1f, 0.5f);
            skillIconRoot.pivot = new Vector2(1f, 0.5f);
            skillIconRoot.anchoredPosition = new Vector2(-30f, 0f);
            skillIconRoot.sizeDelta = new Vector2(88f, 460f);
            VerticalLayoutGroup layout = rootObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
        }

        skillStatusCanvas.gameObject.SetActive(true);

        for (int i = skillIconRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(skillIconRoot.GetChild(i).gameObject);
        }
        skillIcons.Clear();

        foreach (CombatDrawingSkillDefinition skill in loadout.UnlockedSkills)
        {
            if (skill == null)
            {
                continue;
            }

            GameObject iconObject = new GameObject(
                skill.DisplayName + " Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement));
            iconObject.transform.SetParent(skillIconRoot, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.sizeDelta = new Vector2(76f, 76f);
            LayoutElement element = iconObject.GetComponent<LayoutElement>();
            element.preferredWidth = 76f;
            element.preferredHeight = 76f;
            Image background = iconObject.GetComponent<Image>();
            Color backgroundColor = skill.EffectColor;
            backgroundColor.a = 0.48f;
            background.color = backgroundColor;
            background.raycastTarget = false;

            TMP_Text symbol = CreateIconText(iconObject.transform, "Symbol", GetSkillSymbol(skill), 27f);
            symbol.color = new Color(1f, 1f, 1f, 0.9f);
            TMP_Text cooldown = CreateIconText(iconObject.transform, "Cooldown", string.Empty, 24f);
            cooldown.fontStyle = FontStyles.Bold;
            skillIcons.Add(new SkillIconBinding
            {
                Skill = skill,
                Background = background,
                CooldownText = cooldown
            });
        }
    }

    private void HandleDebugUIVisibilityChanged(bool visible)
    {
        BuildSkillStatusUI();
    }

    private TMP_Text CreateIconText(Transform parent, string name, string value, float size)
    {
        GameObject textObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -3f);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = uiFont;
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private void UpdateSkillStatusUI()
    {
        foreach (SkillIconBinding binding in skillIcons)
        {
            float remaining = GetRemainingCooldown(binding.Skill);
            binding.CooldownText.text = remaining > 0f
                ? Mathf.CeilToInt(remaining).ToString()
                : string.Empty;
            Color color = binding.Skill.EffectColor;
            color.a = remaining > 0f ? 0.24f : 0.48f;
            binding.Background.color = color;
        }
    }

    private static string GetSkillSymbol(CombatDrawingSkillDefinition skill)
    {
        switch (skill.EffectKind)
        {
            case CombatDrawingEffectKind.TimeSlow:
                return "시간";
            case CombatDrawingEffectKind.HeartProjectile:
                return "하트";
            case CombatDrawingEffectKind.AreaDamage:
                return "번개";
            case CombatDrawingEffectKind.CancellationLine:
                return "취소";
            case CombatDrawingEffectKind.WorldPlacementTest:
                return "배치";
            case CombatDrawingEffectKind.InkSlash:
                return "참격";
            case CombatDrawingEffectKind.InkEruption:
                return "파열";
            default:
                return "문양";
        }
    }
}

// 하트 문양 스킬을 썼을 때 대상에게 떨어지는 하트 투사체 이동에 사용함.
[DisallowMultipleComponent]
public sealed class CombatDrawingHeartProjectile : MonoBehaviour
{
    private const int HeartPointCount = 40;

    [SerializeField, Min(0.1f)] private float fallHeight = 4f;
    [SerializeField, Min(0.05f)] private float fallDuration = 0.3f;
    [SerializeField, Min(0.05f)] private float visualSize = 0.9f;

    private CombatDrawingSkillDefinition skill;
    private GameObject source;
    private Health target;
    private LineRenderer heart;
    private Material heartMaterial;
    private float destroyAt;
    private float fallStartsAt;
    private float stunDuration;

    public void Initialize(
        CombatDrawingSkillDefinition definition,
        GameObject owner,
        Health targetHealth,
        float castDelay,
        float appliedStunDuration)
    {
        skill = definition;
        source = owner;
        target = targetHealth;
        fallStartsAt = Time.unscaledTime + Mathf.Max(0f, castDelay);
        stunDuration = Mathf.Max(0f, appliedStunDuration);
        destroyAt = Time.unscaledTime + Mathf.Max(0.1f, skill.SpawnedEffectLifetime);
        transform.position = GetImpactPosition() + Vector3.up * fallHeight;
        EnsureHeartVisual();
    }

    private void Update()
    {
        if (skill == null || target == null || !target.IsAlive || Time.unscaledTime >= destroyAt)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 destination = GetImpactPosition();
        float fallProgress = Mathf.Clamp01(
            (Time.unscaledTime - fallStartsAt) / Mathf.Max(0.05f, fallDuration));
        transform.position = destination
            + Vector3.up * Mathf.Lerp(fallHeight, 0f, fallProgress * fallProgress);
        float slamScale = Mathf.Lerp(0.75f, 1.35f, fallProgress);
        transform.localScale = new Vector3(slamScale, slamScale, 1f);

        if (fallProgress >= 1f)
        {
            bool damaged = target.TakeDamage(new DamageInfo(
                skill.Damage,
                source,
                destination,
                Vector3.down));
            if (damaged)
            {
                StatusEffectController effects = target.GetComponent<StatusEffectController>();
                effects = effects != null
                    ? effects
                    : target.gameObject.AddComponent<StatusEffectController>();
                effects.ApplyStun(stunDuration);
            }
            Destroy(gameObject);
            return;
        }

        if (Camera.main != null)
        {
            transform.rotation = Camera.main.transform.rotation;
        }
    }

    private Vector3 GetImpactPosition()
    {
        return target != null
            ? target.transform.position + Vector3.up * 0.65f
            : transform.position;
    }

    private void EnsureHeartVisual()
    {
        heart = GetComponent<LineRenderer>();
        heart = heart != null ? heart : gameObject.AddComponent<LineRenderer>();
        Shader shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            heartMaterial = new Material(shader)
            {
                name = "CombatDrawingHeartProjectileMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
            heart.sharedMaterial = heartMaterial;
        }

        heart.useWorldSpace = false;
        heart.loop = true;
        heart.positionCount = HeartPointCount;
        heart.widthMultiplier = 0.08f;
        heart.numCornerVertices = 3;
        heart.numCapVertices = 3;
        heart.sortingOrder = 80;
        Color color = skill != null ? skill.EffectColor : new Color(1f, 0.2f, 0.45f, 1f);
        heart.startColor = color;
        heart.endColor = color;

        for (int i = 0; i < HeartPointCount; i++)
        {
            float t = Mathf.PI * 2f * i / HeartPointCount;
            float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
            float y = 13f * Mathf.Cos(t)
                - 5f * Mathf.Cos(2f * t)
                - 2f * Mathf.Cos(3f * t)
                - Mathf.Cos(4f * t);
            heart.SetPosition(i, new Vector3(x, y, 0f) * (visualSize / 32f));
        }
    }

    private void OnDestroy()
    {
        if (heartMaterial != null)
        {
            Destroy(heartMaterial);
        }
    }
}
