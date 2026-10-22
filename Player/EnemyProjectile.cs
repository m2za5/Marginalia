using UnityEngine;

// 원거리 몬스터와 보스가 같이 쓰는 공용 투사체로 사용함.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class EnemyProjectile : MonoBehaviour
{
    private Rigidbody body;
    private GameObject owner;
    private int damage;
    private float destroyAt;
    private bool initialized;
    private float stunDuration;

    public GameObject Owner => owner;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    public void Launch(GameObject source, Vector3 direction, float speed, int amount, float lifetime)
    {
        Launch(source, direction, speed, amount, lifetime, 0f);
    }

    public void Launch(GameObject source, Vector3 direction, float speed, int amount, float lifetime, float appliedStunDuration)
    {
        owner = source;
        damage = Mathf.Max(0, amount);
        destroyAt = Time.time + Mathf.Max(0.1f, lifetime);
        initialized = true;
        stunDuration = Mathf.Max(0f, appliedStunDuration);
        body.useGravity = false;
        body.linearVelocity = direction.normalized * Mathf.Max(0f, speed);
    }
    public bool Cancel()
    {
        if (!initialized || !gameObject.activeInHierarchy)
        {
            return false;
        }

        initialized = false;
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }

    private void Update()
    {
        if (initialized && Time.time >= destroyAt)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!initialized || (owner != null && other.transform.IsChildOf(owner.transform)))
        {
            return;
        }

        Transform player = FindPlayerRoot(other.transform);
        if (player == null)
        {
            // 스포너 영역처럼 판정만을 위한 Trigger는 투사체의 실제 장애물이 아니다.
            // 플레이어의 피격 Trigger는 위에서 먼저 식별하므로 정상적으로 피해를 적용한다.
            if (other.isTrigger)
            {
                return;
            }

            Destroy(gameObject);
            return;
        }

        IDamageable damageable = player.GetComponentInChildren<IDamageable>();

        if (damageable != null && damageable.CanTakeDamage)
        {
            Vector3 direction = body.linearVelocity.sqrMagnitude > 0.001f
                ? body.linearVelocity.normalized
                : transform.forward;
            if (damageable.TakeDamage(new DamageInfo(damage, owner, transform.position, direction, 0f))
                && stunDuration > 0f)
            {
                StatusEffectController effects = player.GetComponentInChildren<StatusEffectController>();
                if (effects == null) effects = player.gameObject.AddComponent<StatusEffectController>();
                effects.ApplyStun(stunDuration);
            }
        }

        Destroy(gameObject);
    }

    private static Transform FindPlayerRoot(Transform candidate)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (current.CompareTag("Player"))
            {
                return current;
            }
        }

        return null;
    }
}
