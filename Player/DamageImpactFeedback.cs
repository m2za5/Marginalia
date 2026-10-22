using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerTimeScaleController))]
public class DamageImpactFeedback : MonoBehaviour
{
    private static readonly Color DealtDamageColor = new Color(1f, 0.82f, 0.2f, 1f);
    private static readonly Color ReceivedDamageColor = new Color(1f, 0.28f, 0.25f, 1f);
    private static readonly Color StatusTextColor = new Color(0.7f, 0.8f, 1f, 1f);
    private static readonly Color MissTextColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    private const float FloatingTextHeight = 0.25f;
    private const float StatusTextHeight = 0.55f;

    [Header("References")]
    [SerializeField] private PlayerAttack attack;
    [SerializeField] private FixedAngleFollowCamera followCamera;
    [SerializeField] private PlayerTimeScaleController timeScaleController;
    [SerializeField] private Health health;

    [Header("Hitstop")]
    [SerializeField] private bool useHitstop = true;
    [SerializeField] private float hitstopTimeScale = 0.04f;
    [SerializeField] private float feedbackCooldown = 0.035f;

    [Header("Camera Shake")]
    [SerializeField] private bool useCameraShake = true;
    [SerializeField] private float shakeAmplitude = 0.075f;
    [SerializeField] private float shakeDuration = 0.1f;

    [Header("Impact Puff")]
    [SerializeField] private bool spawnImpactPuff = true;
    [SerializeField] private Color impactPuffColor = new Color(1f, 0.92f, 0.46f, 0.72f);
    [SerializeField] private float impactPuffStartScale = 0.22f;
    [SerializeField] private float impactPuffEndScale = 0.68f;
    [SerializeField] private float impactPuffLifetime = 0.18f;
    [SerializeField] private int impactSortingOrder = 18;

    private float nextFeedbackTime;
    private TMP_FontAsset _combatTextFont;
    private readonly Dictionary<Health, CombatDamageFloatingText> _temporalDamageTexts =
        new Dictionary<Health, CombatDamageFloatingText>();

    public int PresentationCount { get; private set; }
    public float LastPresentedAt { get; private set; } = float.NegativeInfinity;
    public int LastPresentedFrame { get; private set; } = -1;
    public bool LastPresentationSpawnedPuff { get; private set; }
    public bool LastPresentationStartedShake { get; private set; }
    public bool LastPresentationStartedHitstop { get; private set; }
    public event System.Action Presented;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Health.AnyDamaged += HandleAnyDamaged;
        Health.AnyDamageRejected += HandleAnyDamageRejected;
        StatusEffectController.AnyEffectApplied += HandleAnyEffectApplied;

        if (attack != null)
        {
            attack.AttackHit.RemoveListener(HandleAttackHit);
            attack.AttackHit.AddListener(HandleAttackHit);
        }
        if (health != null)
        {
            health.Damaged -= HandlePlayerDamaged;
            health.Damaged += HandlePlayerDamaged;
        }
    }

    private void OnDisable()
    {
        Health.AnyDamaged -= HandleAnyDamaged;
        Health.AnyDamageRejected -= HandleAnyDamageRejected;
        StatusEffectController.AnyEffectApplied -= HandleAnyEffectApplied;
        _temporalDamageTexts.Clear();
        if (attack != null)
        {
            attack.AttackHit.RemoveListener(HandleAttackHit);
        }
        if (health != null)
        {
            health.Damaged -= HandlePlayerDamaged;
        }

        timeScaleController?.CancelHitstop();
    }

    private void ResolveReferences()
    {
        if (attack == null)
        {
            attack = GetComponent<PlayerAttack>();
        }

        if (followCamera == null && Camera.main != null)
        {
            followCamera = Camera.main.GetComponent<FixedAngleFollowCamera>();
        }

        if (timeScaleController == null)
        {
            timeScaleController = GetComponent<PlayerTimeScaleController>();
        }
        if (health == null)
        {
            health = GetComponent<Health>();
        }
        if (_combatTextFont == null)
            _combatTextFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Hakgyoansim_NalgaeR SDF");
    }

    private void LateUpdate()
    {
        foreach (KeyValuePair<Health, CombatDamageFloatingText> entry in _temporalDamageTexts)
        {
            if (entry.Key != null && entry.Value != null) continue;
            _temporalDamageTexts.Remove(entry.Key);
            break;
        }
    }

    private void HandleAnyDamaged(Health target, DamageInfo info, int appliedDamage, int remainingHealth)
    {
        if (target == null || appliedDamage <= 0) return;
        bool received = target == health;
        bool dealt = info.Source != null && info.Source.transform.IsChildOf(transform);
        if (!received && !dealt) return;

        if (!received && timeScaleController != null && timeScaleController.IsTemporalSkillActive)
        {
            if (_temporalDamageTexts.TryGetValue(target, out CombatDamageFloatingText existing)
                && existing != null && existing.IsHoldingDamage)
            {
                existing.HoldAndAccumulate(appliedDamage, timeScaleController);
                return;
            }

            CombatDamageFloatingText accumulated = CreateFloatingText(target.gameObject, "0", DealtDamageColor);
            accumulated.HoldAndAccumulate(appliedDamage, timeScaleController);
            _temporalDamageTexts[target] = accumulated;
            return;
        }

        CreateFloatingText(target.gameObject, appliedDamage.ToString(),
            received ? ReceivedDamageColor : DealtDamageColor);
    }

    private void HandleAnyEffectApplied(StatusEffectController effects, StatusEffectType effect, float duration)
    {
        if (effects == null) return;
        Health target = effects.GetComponentInParent<Health>();
        if (target == null || !target.IsAlive) return;
        PlayerController player = effects.GetComponentInParent<PlayerController>();
        if (player != null && !player.transform.IsChildOf(transform)) return;

        string message = effect == StatusEffectType.Stun ? "기절함" : "속박됨";
        CreateFloatingText(target.gameObject, message, StatusTextColor, StatusTextHeight);
    }

    private void HandleAnyDamageRejected(Health target, DamageInfo info)
    {
        if (target == null || !target.IsAlive || info.Kind == DamageKind.PlayerMelee
            || info.Source == null || !info.Source.transform.IsChildOf(transform)) return;
        BossMonsterAI boss = target.GetComponent<BossMonsterAI>();
        if (boss != null && boss.ShieldRemaining > 0)
            CreateFloatingText(target.gameObject, "miss", MissTextColor);
    }

    private CombatDamageFloatingText CreateFloatingText(
        GameObject target, string message, Color color, float extraHeight = 0f)
    {
        Collider targetCollider = target.GetComponent<Collider>();
        Vector3 position = target.transform.position;
        position.y = targetCollider != null
            ? targetCollider.bounds.max.y
            : position.y + 1.4f;
        GameObject textObject = new GameObject("CombatFeedbackText");
        textObject.transform.position = position + Vector3.up * (FloatingTextHeight + extraHeight);
        CombatDamageFloatingText text = textObject.AddComponent<CombatDamageFloatingText>();
        text.InitializeMessage(message, color, _combatTextFont);
        return text;
    }

    private void HandlePlayerDamaged(DamageInfo damageInfo, int remainingHealth)
    {
        ResolveReferences();
        if (useCameraShake && followCamera != null)
        {
            followCamera.AddImpulseShake(0.045f, 0.09f, -damageInfo.HitDirection);
        }
    }

    private void HandleAttackHit(GameObject target, DamageInfo damageInfo)
    {
        PresentImpact(
            target,
            damageInfo,
            attack != null ? attack.CurrentImpactFeedbackScale : 1f,
            attack != null ? attack.CurrentHitstopDuration : 0f);
    }

    public static DamageImpactFeedback Resolve(GameObject owner)
    {
        return owner == null ? null : owner.GetComponentInParent<DamageImpactFeedback>();
    }

    public bool PresentImpact(
        GameObject target,
        DamageInfo damageInfo,
        float impactScale,
        float hitstopDuration,
        float cameraShakeScale = 1f,
        bool allowImpactPuff = true)
    {
        if (!isActiveAndEnabled || Time.unscaledTime < nextFeedbackTime)
        {
            return false;
        }

        nextFeedbackTime = Time.unscaledTime + feedbackCooldown;
        ResolveReferences();
        impactScale = Mathf.Max(0f, impactScale);
        hitstopDuration = Mathf.Max(0f, hitstopDuration);
        bool shakeStarted = false;
        bool puffSpawned = false;
        bool hitstopStarted = false;

        if (useCameraShake && followCamera != null)
        {
            followCamera.AddImpulseShake(
                shakeAmplitude * impactScale * Mathf.Max(0f, cameraShakeScale),
                shakeDuration * Mathf.Lerp(1f, impactScale, 0.35f),
                damageInfo.HitDirection);
            shakeStarted = followCamera.LastImpulseShakeStartedFrame == Time.frameCount;
        }

        if (spawnImpactPuff && allowImpactPuff)
        {
            puffSpawned = SpawnImpactPuff(target, damageInfo, impactScale) != null;
        }

        if (useHitstop && hitstopDuration > 0f && Application.isPlaying)
        {
            timeScaleController?.TriggerHitstop(
                hitstopTimeScale,
                hitstopDuration);
            hitstopStarted = timeScaleController != null
                && timeScaleController.IsHitstopActive;
        }

        PresentationCount++;
        LastPresentedAt = Time.unscaledTime;
        LastPresentedFrame = Time.frameCount;
        LastPresentationSpawnedPuff = puffSpawned;
        LastPresentationStartedShake = shakeStarted;
        LastPresentationStartedHitstop = hitstopStarted;
        Presented?.Invoke();
        return true;
    }

    private PaperSpritePuff SpawnImpactPuff(
        GameObject target,
        DamageInfo damageInfo,
        float impactScale)
    {
        Vector3 position = damageInfo.HitPoint;

        if (position.sqrMagnitude <= 0.0001f && target != null)
        {
            position = target.transform.position + Vector3.up * 0.85f;
        }

        Vector3 drift = damageInfo.HitDirection.sqrMagnitude > 0.0001f
            ? damageInfo.HitDirection.normalized * 0.18f + Vector3.up * 0.12f
            : Vector3.up * 0.12f;

        return PaperSpritePuff.Spawn(
            position,
            impactPuffColor,
            impactPuffStartScale * impactScale,
            impactPuffEndScale * impactScale,
            impactPuffLifetime,
            impactSortingOrder,
            drift,
            true);
    }

    private void OnValidate()
    {
        hitstopTimeScale = Mathf.Clamp(hitstopTimeScale, 0.001f, 1f);
        feedbackCooldown = Mathf.Max(0f, feedbackCooldown);
        shakeAmplitude = Mathf.Max(0f, shakeAmplitude);
        shakeDuration = Mathf.Max(0f, shakeDuration);
        impactPuffStartScale = Mathf.Max(0.01f, impactPuffStartScale);
        impactPuffEndScale = Mathf.Max(impactPuffStartScale, impactPuffEndScale);
        impactPuffLifetime = Mathf.Max(0.01f, impactPuffLifetime);
    }

    private void Reset()
    {
        attack = GetComponent<PlayerAttack>();
        timeScaleController = GetComponent<PlayerTimeScaleController>();
    }
}
