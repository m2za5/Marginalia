using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CombatDrawingInkSlash : MonoBehaviour
{
    private const int HitBufferSize = 64;
    [Header("Motion")]
    [SerializeField, Min(0.1f)] private float speed = 14f;

    [Header("Hit")]
    [SerializeField, Min(0.05f)] private float hitRadius = 0.6f;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Visual")]
    [SerializeField, Min(0f)] private float dissipateDuration;
    [SerializeField] private bool useLegacyImpactEffect = true;
    [SerializeField] private GameObject impactEffectPrefab;
    [SerializeField, Min(0.01f)] private float impactEmitDuration = 0.12f;
    [SerializeField, Min(0.1f)] private float impactEffectLifetime = 1.2f;
    [SerializeField] private bool useInkCastVfx;
    [SerializeField] private bool useInkImpactBurst;
    [SerializeField] private GameObject castVfxPrefab;
    [SerializeField, Min(0.05f)] private float castVfxLifetime = 0.35f;
    [SerializeField, Range(0f, 1f)] private float castVfxCameraFacing = 0.5f;
    [Tooltip("시전 참격 VFX의 시각 전용 배율. 피해 판정 범위에는 영향이 없다.")]
    [SerializeField, Min(0.05f)] private float castVfxScale = 0.7875f;
    [Tooltip("VFX 루트 기준으로 참격이 실제로 베는 지점(배율 1 기준). 이 지점이 드로잉 목표 위치에 오도록 루트를 배치한다.")]
    [SerializeField] private Vector3 castVfxStrikeOffset = new Vector3(0f, 0f, 3.6f);
    [SerializeField] private GameObject inkImpactBurstPrefab;
    [SerializeField, Min(0.05f)] private float inkImpactBurstLifetime = 0.3f;
    [SerializeField, Min(0f)] private float inkImpactBurstCameraOffset = 0.35f;

    [Header("Camera Shake")]
    [SerializeField, Min(0f)] private float cameraShakeMultiplier = 2.5f;

    private readonly HashSet<IDamageable> damagedTargets = new HashSet<IDamageable>();
    private readonly RaycastHit[] hitBuffer = new RaycastHit[HitBufferSize];
    private readonly Collider[] overlapBuffer = new Collider[HitBufferSize];

    private CombatDrawingSkillDefinition skill;
    private GameObject owner;
    private Vector3 direction = Vector3.forward;
    private Vector3 targetPoint;
    private bool initialized;
    private bool arrived;

    private DamageImpactFeedback impactFeedback;
    private ParticleSystem[] ownedParticles;
    private float[] ownedSimulationSpeeds;
    private bool particlesFrozen;
    private bool impactFeedbackPresented;
    private bool impactSoundPlayed;
    private bool inkImpactBurstSpawned;

    public int HitCount => damagedTargets.Count;
    public bool HasArrived => arrived;

    public void Initialize(
        CombatDrawingSkillDefinition definition,
        GameObject source,
        Vector3 startPosition,
        Vector3 worldTargetPoint)
    {
        skill = definition;
        owner = source;
        impactFeedback = DamageImpactFeedback.Resolve(source);

        Vector3 planarDelta = worldTargetPoint - startPosition;
        planarDelta.y = 0f;
        direction = planarDelta.sqrMagnitude > 0.0001f ? planarDelta.normalized : transform.forward;
        targetPoint = new Vector3(worldTargetPoint.x, startPosition.y, worldTargetPoint.z);
        transform.SetPositionAndRotation(startPosition, Quaternion.LookRotation(direction, Vector3.up));
        initialized = true;

        ConfigureParticles();
        SpawnCastVfx(targetPoint);
        float sweepDistance = Mathf.Min(planarDelta.magnitude, skill != null ? skill.Range : planarDelta.magnitude);
        InkSlashDebugTrace.Log("3.SLASH",
            $"start={startPosition:F2} worldTarget={worldTargetPoint:F2} direction={direction:F2} "
            + $"rotationForward={transform.forward:F2} sweepDistance={sweepDistance:F2} hitRadius={hitRadius:F2} "
            + $"hitLayers={hitLayers.value}",
            this);
        InkSlashDebugTrace.Line(startPosition, startPosition + direction * sweepDistance, Color.red);
        GunHoSfxBus.Play(GunHoSfxCue.InkSlashCast);
        DamageOverlappingTargets(startPosition);
        DamageTargetsAlongStep(startPosition, sweepDistance);
        Arrive();
    }

    private void SpawnCastVfx(Vector3 strikePoint)
    {
        if (!useInkCastVfx || castVfxPrefab == null)
        {
            return;
        }

        Vector3 strokeUp = Vector3.up;
        Camera viewCamera = Camera.main;

        if (viewCamera != null)
        {
            Vector3 towardCamera = Vector3.ProjectOnPlane(-viewCamera.transform.forward, direction);

            if (towardCamera.sqrMagnitude > 0.0001f)
            {
                strokeUp = Vector3.Slerp(Vector3.up, towardCamera.normalized, castVfxCameraFacing);
            }
        }

        Quaternion rotation = Quaternion.LookRotation(direction, strokeUp);
        GameObject cast = Instantiate(
            castVfxPrefab,
            strikePoint - rotation * (castVfxStrikeOffset * castVfxScale),
            rotation);
        cast.transform.localScale *= castVfxScale;
        cast.name = castVfxPrefab.name + " (Ink Slash Cast)";
        InkSlashDebugTrace.Log("4.VFX",
            $"prefab={castVfxPrefab.name} strikePoint={strikePoint:F2} rootPos={cast.transform.position:F2} "
            + $"scale={castVfxScale:F2} rotEuler={cast.transform.eulerAngles:F1} "
            + $"vfxForward={cast.transform.forward:F2} vfxUp={cast.transform.up:F2} strokeUp={strokeUp:F2} "
            + $"cameraFacing={castVfxCameraFacing:F2} slashDirection={direction:F2}",
            cast);

        foreach (ParticleSystem system in cast.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = system.main;
            main.useUnscaledTime = true;
        }

        Destroy(cast, castVfxLifetime);
    }

    private void DamageOverlappingTargets(Vector3 position)
    {
        int count = Physics.OverlapSphereNonAlloc(
            position,
            hitRadius,
            overlapBuffer,
            hitLayers,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            bool behind = IsBehindStart(overlapBuffer[i], position);
            InkSlashDebugTrace.Log("5.HIT-OVERLAP",
                $"collider={overlapBuffer[i].name} at={overlapBuffer[i].transform.position:F2} skippedBehind={behind}");
            if (behind)
            {
                continue;
            }

            TryDamage(overlapBuffer[i], position);
        }
    }
    private bool IsBehindStart(Collider hit, Vector3 start)
    {
        if (hit == null)
        {
            return false;
        }

        Vector3 toTarget = ColliderPointUtility.ClosestPointSafe(hit, start) - start;
        toTarget.y = 0f;
        return toTarget.sqrMagnitude > 0.0001f && Vector3.Dot(toTarget, direction) < 0f;
    }

    private void DamageTargetsAlongStep(Vector3 from, float distance)
    {
        int count = Physics.SphereCastNonAlloc(
            from,
            hitRadius,
            direction,
            hitBuffer,
            distance,
            hitLayers,
            QueryTriggerInteraction.Collide);
        System.Array.Sort(hitBuffer, 0, count, HitDistanceComparer);

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];
            // [InkSlashTrace] 임시 진단: 경로(SphereCast) 판정 후보
            InkSlashDebugTrace.Log("5.HIT-SWEEP",
                $"collider={hit.collider.name} at={hit.collider.transform.position:F2} distance={hit.distance:F2} "
                + $"point={hit.point:F2} from={from:F2} direction={direction:F2}");
            if (hit.distance <= 0f && IsBehindStart(hit.collider, from))
            {
                continue;
            }

            Vector3 hitPoint = hit.distance > 0f ? hit.point : from;
            TryDamage(hit.collider, hitPoint);
        }
    }

    private static readonly IComparer<RaycastHit> HitDistanceComparer =
        Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

    private bool TryDamage(Collider hit, Vector3 hitPoint)
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

        DamageInfo damageInfo = new DamageInfo(
            skill.Damage,
            owner,
            hitPoint,
            direction);

        if (!damageable.TakeDamage(damageInfo))
        {
            BossMonsterAI boss = damageableComponent.GetComponentInParent<BossMonsterAI>();
            if (boss != null && boss.ShieldRemaining > 0)
            {
                SpawnImpactEffect(hitPoint);
                SpawnInkImpactBurst(hitPoint);
            }
            return false;
        }

        SpawnImpactEffect(hitPoint);
        SpawnInkImpactBurst(hitPoint);
        PlayImpactSound();
        PresentImpactFeedback(hit.gameObject, damageInfo);
        return true;
    }

    private void SpawnInkImpactBurst(Vector3 position)
    {
        if (!useInkImpactBurst || inkImpactBurstSpawned || inkImpactBurstPrefab == null)
        {
            return;
        }

        inkImpactBurstSpawned = true;
        Camera viewCamera = Camera.main;

        if (viewCamera != null)
        {
            position -= viewCamera.transform.forward * inkImpactBurstCameraOffset;
        }

        GameObject burst = Instantiate(inkImpactBurstPrefab, position, Quaternion.identity);
        burst.name = inkImpactBurstPrefab.name + " (Ink Slash First Hit)";

        foreach (ParticleSystem system in burst.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = system.main;
            main.useUnscaledTime = true;
        }

        Destroy(burst, inkImpactBurstLifetime);
    }

    private void PlayImpactSound()
    {
        if (impactSoundPlayed)
        {
            return;
        }

        impactSoundPlayed = true;
        GunHoSfxBus.Play(GunHoSfxCue.InkSlashHit);
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
            cameraShakeMultiplier,
            !inkImpactBurstSpawned);
    }

    private void SetParticlesFrozen(bool frozen)
    {
        if (particlesFrozen == frozen || ownedParticles == null)
        {
            return;
        }

        particlesFrozen = frozen;

        for (int i = 0; i < ownedParticles.Length; i++)
        {
            ParticleSystem system = ownedParticles[i];

            if (system == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = system.main;
            main.simulationSpeed = frozen ? 0f : ownedSimulationSpeeds[i];
        }
    }

    private void Arrive()
    {
        arrived = true;
        transform.position = targetPoint;
        SetParticlesFrozen(false);
        StopParticles();

        float lifetime = dissipateDuration > 0f
            ? dissipateDuration
            : (skill != null ? skill.SpawnedEffectLifetime : 0.5f);
        Destroy(gameObject, Mathf.Max(0.05f, lifetime));
    }

    private void ConfigureParticles()
    {
        ownedParticles = GetComponentsInChildren<ParticleSystem>(true);
        ownedSimulationSpeeds = new float[ownedParticles.Length];

        for (int i = 0; i < ownedParticles.Length; i++)
        {
            ParticleSystem.MainModule main = ownedParticles[i].main;
            main.useUnscaledTime = true;
            ownedSimulationSpeeds[i] = main.simulationSpeed;
        }
    }

    private void StopParticles()
    {
        foreach (ParticleSystem system in GetComponentsInChildren<ParticleSystem>(true))
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void SpawnImpactEffect(Vector3 position)
    {
        if (!useLegacyImpactEffect || impactEffectPrefab == null)
        {
            return;
        }

        GameObject impact = Instantiate(
            impactEffectPrefab,
            position,
            Quaternion.LookRotation(-direction, Vector3.up));
        impact.name = impactEffectPrefab.name + " (Ink Slash Hit)";

        ParticleSystem[] systems = impact.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem system in systems)
        {
            ParticleSystem.MainModule main = system.main;
            main.useUnscaledTime = true;
        }

        Destroy(impact, impactEffectLifetime);
        StartCoroutine(StopImpactEmission(systems));
    }

    private IEnumerator StopImpactEmission(ParticleSystem[] systems)
    {
        yield return new WaitForSecondsRealtime(impactEmitDuration);

        foreach (ParticleSystem system in systems)
        {
            if (system != null)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    private void OnValidate()
    {
        speed = Mathf.Max(0.1f, speed);
        hitRadius = Mathf.Max(0.05f, hitRadius);
        dissipateDuration = Mathf.Max(0f, dissipateDuration);
        impactEmitDuration = Mathf.Max(0.01f, impactEmitDuration);
        impactEffectLifetime = Mathf.Max(impactEmitDuration, impactEffectLifetime);
        castVfxLifetime = Mathf.Max(0.05f, castVfxLifetime);
        castVfxScale = Mathf.Max(0.05f, castVfxScale);
        inkImpactBurstLifetime = Mathf.Max(0.05f, inkImpactBurstLifetime);
        inkImpactBurstCameraOffset = Mathf.Max(0f, inkImpactBurstCameraOffset);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, hitRadius);
        if (initialized)
        {
            Gizmos.DrawLine(transform.position, targetPoint);
        }
    }
}
