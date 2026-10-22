using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CombatDrawingInkEruption : MonoBehaviour
{
    private const int HitBufferSize = 24;
    private const float MaximumEruptionDelay = 0.08f;

    [Header("Timing")]
    [SerializeField, Range(0f, 0.08f)] private float eruptionDelay = 0.06f;
    [SerializeField, Min(0.01f)] private float eruptionEmitDuration = 0.2f;

    [Header("Hit")]
    [SerializeField, Min(0.05f)] private float hitRadius = 1.25f;
    [SerializeField, Min(0f)] private float hitHeightOffset = 0.6f;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Visual")]
    [SerializeField] private GameObject telegraphRoot;
    [SerializeField] private GameObject eruptionRoot;
    [Tooltip("Visual-only scale for the spawned eruption VFX, applied around the cast point. Hit radius is unaffected.")]
    [SerializeField, Min(0.01f)] private float eruptionVfxScale = 0.5f;

    [Header("Camera Shake")]
    [SerializeField, Min(0f)] private float cameraShakeMultiplier = 2.2f;

    private readonly HashSet<IDamageable> damagedTargets = new HashSet<IDamageable>();
    private readonly Collider[] overlapBuffer = new Collider[HitBufferSize];

    private CombatDrawingSkillDefinition skill;
    private GameObject owner;
    private Vector3 groundPoint;
    private float elapsed;
    private bool initialized;
    private bool erupted;

    private DamageImpactFeedback impactFeedback;
    private PlayerTimeScaleController ownerTimeScale;
    private bool impactFeedbackPresented;

    private bool eruptionRootBaseCaptured;
    private Vector3 eruptionRootBasePosition;
    private Vector3 eruptionRootBaseScale;

    public bool HasErupted => erupted;
    public int HitCount => damagedTargets.Count;

    public void Initialize(
        CombatDrawingSkillDefinition definition,
        GameObject source,
        Vector3 worldGroundPoint)
    {
        skill = definition;
        owner = source;
        impactFeedback = DamageImpactFeedback.Resolve(source);
        ownerTimeScale = source != null
            ? source.GetComponentInParent<PlayerTimeScaleController>()
            : null;
        groundPoint = worldGroundPoint;
        eruptionDelay = Mathf.Clamp(eruptionDelay, 0f, MaximumEruptionDelay);
        elapsed = 0f;
        erupted = false;
        initialized = true;

        transform.position = worldGroundPoint;
        SetUnscaledTime(gameObject);
        ApplyEruptionVfxScale();

        if (telegraphRoot != null && !telegraphRoot.activeSelf)
        {
            telegraphRoot.SetActive(true);
        }

        if (eruptionRoot != null && eruptionRoot.activeSelf)
        {
            eruptionRoot.SetActive(false);
        }

        if (eruptionDelay <= 0f)
        {
            Erupt();
        }
    }

    private void Update()
    {
        if (!initialized || erupted)
        {
            return;
        }

        if (skill == null)
        {
            Destroy(gameObject);
            return;
        }

        if (ownerTimeScale != null && ownerTimeScale.IsHitstopActive)
        {
            return;
        }

        elapsed += Time.unscaledDeltaTime;

        if (elapsed >= eruptionDelay)
        {
            Erupt();
        }
    }

    private void Erupt()
    {
        erupted = true;
        ReplayAsImpactMarker(telegraphRoot);

        if (eruptionRoot != null)
        {
            eruptionRoot.SetActive(true);
            SetUnscaledTime(eruptionRoot);
        }
        GunHoSfxBus.Play(GunHoSfxCue.InkEruption);

        DamageTargetsInRange();

        float lifetime = skill != null ? skill.SpawnedEffectLifetime : 2f;
        StartCoroutine(FinishAfterEruption(Mathf.Max(0.05f, lifetime - eruptionDelay)));
    }

    private IEnumerator FinishAfterEruption(float remainingLifetime)
    {
        yield return new WaitForSecondsRealtime(Mathf.Min(eruptionEmitDuration, remainingLifetime));
        StopEmitting(telegraphRoot);
        StopEmitting(eruptionRoot);
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, remainingLifetime - eruptionEmitDuration));
        Destroy(gameObject);
    }

    // Scales from the prefab's authored transform each time, so re-initializing never compounds the scale.
    // Child particle systems use Hierarchy scaling, so they follow this transform.
    private void ApplyEruptionVfxScale()
    {
        if (eruptionRoot == null)
        {
            return;
        }

        Transform root = eruptionRoot.transform;

        if (!eruptionRootBaseCaptured)
        {
            eruptionRootBasePosition = root.localPosition;
            eruptionRootBaseScale = root.localScale;
            eruptionRootBaseCaptured = true;
        }

        root.localPosition = eruptionRootBasePosition * eruptionVfxScale;
        root.localScale = eruptionRootBaseScale * eruptionVfxScale;
    }

    private void DamageTargetsInRange()
    {
        Vector3 center = groundPoint + Vector3.up * hitHeightOffset;
        int count = Physics.OverlapSphereNonAlloc(
            center,
            hitRadius,
            overlapBuffer,
            hitLayers,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            TryDamage(overlapBuffer[i], center);
        }
    }

    private bool TryDamage(Collider hit, Vector3 center)
    {
        if (hit == null || owner == null || hit.transform.IsChildOf(owner.transform))
        {
            return false;
        }

        IDamageable damageable = hit.GetComponentInParent<IDamageable>();
        Component damageableComponent = damageable as Component;

        if (damageable == null
            || damageableComponent == null
            || damageableComponent.transform.IsChildOf(owner.transform)
            || !damageable.IsAlive
            || damagedTargets.Contains(damageable))
        {
            return false;
        }

        damagedTargets.Add(damageable);

        Vector3 hitPoint = ColliderPointUtility.ClosestPointSafe(hit, center);
        Vector3 hitDirection = hitPoint - center;
        hitDirection.y = 0f;
        if (hitDirection.sqrMagnitude <= 0.0001f)
        {
            hitDirection = Vector3.up;
        }

        DamageInfo damageInfo = new DamageInfo(
            skill.Damage,
            owner,
            hitPoint,
            hitDirection.normalized);

        if (!damageable.TakeDamage(damageInfo))
        {
            return false;
        }

        PresentImpactFeedback(hit.gameObject, damageInfo);
        return true;
    }
    private void PresentImpactFeedback(GameObject target, DamageInfo damageInfo)
    {
        if (impactFeedbackPresented || impactFeedback == null || skill == null)
        {
            return;
        }

        impactFeedbackPresented = impactFeedback.PresentImpact(
            target,
            damageInfo,
            skill.ImpactFeedbackScale,
            skill.ImpactHitstopDuration,
            cameraShakeMultiplier);
    }

    private static void StopEmitting(GameObject root)
    {
        if (root == null)
        {
            return;
        }
        foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (system.main.loop)
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    private static void ReplayAsImpactMarker(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        if (!root.activeSelf)
        {
            root.SetActive(true);
        }

        foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            system.Clear(true);
            system.Play(true);
        }
    }

    private static void SetUnscaledTime(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = system.main;
            main.useUnscaledTime = true;
        }
    }

    private void OnValidate()
    {
        eruptionDelay = Mathf.Clamp(eruptionDelay, 0f, MaximumEruptionDelay);
        eruptionEmitDuration = Mathf.Max(0.01f, eruptionEmitDuration);
        eruptionVfxScale = Mathf.Max(0.01f, eruptionVfxScale);
        hitRadius = Mathf.Max(0.05f, hitRadius);
        hitHeightOffset = Mathf.Max(0f, hitHeightOffset);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.05f, 0.2f, 0.6f);
        Vector3 center = (initialized ? groundPoint : transform.position) + Vector3.up * hitHeightOffset;
        Gizmos.DrawWireSphere(center, hitRadius);
    }
}
