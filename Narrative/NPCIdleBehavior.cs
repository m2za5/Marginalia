using UnityEngine;

public class NPCIdleBehavior : MonoBehaviour
{
    private const float ArrivalThreshold = 0.05f;
    private const float ArrivalSqrThreshold = ArrivalThreshold * ArrivalThreshold;
    private const float FlipDeadzone = 0.01f;

    [SerializeField] private NPCIdleBehaviorData data;
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Transform _tf;
    private Vector3 _origin;
    private Vector3 _target;
    private float _timer;
    private bool _walking;
    private bool _waiting;
    private bool _stopped;
    private int _walkHash;
    private int _defaultHash;
    private int[] _actionHashes;
    private int _totalWeight;

    private void Awake()
    {
        _tf = transform;
        _origin = _tf.position;

        if (!ValidateSetup())
        {
            enabled = false;
            return;
        }

        CacheHashes();
        CacheTotalWeight();
    }

    private bool ValidateSetup()
    {
        if (data == null)
        {
            Debug.LogError($"[NPCIdleBehavior] {name}: data 미할당.", this);
            return false;
        }

        if (data.actions == null || data.actions.Length == 0)
        {
            Debug.LogError($"[NPCIdleBehavior] {name}: '{data.name}'에 actions가 없다.", this);
            return false;
        }

        if (animator == null)
        {
            Debug.LogError($"[NPCIdleBehavior] {name}: animator 미할당.", this);
            return false;
        }

        return true;
    }

    private void CacheHashes()
    {
        _walkHash = Animator.StringToHash(data.walkStateName);
        _defaultHash = Animator.StringToHash(data.defaultStateName);

        _actionHashes = new int[data.actions.Length];
        for (int i = 0; i < data.actions.Length; i++)
        {
            IdleAction action = data.actions[i];

            if (action == null || string.IsNullOrEmpty(action.stateName))
            {
                _actionHashes[i] = action != null && action.type == IdleAction.ActionType.Stroll
                    ? _walkHash
                    : _defaultHash;
                continue;
            }

            _actionHashes[i] = Animator.StringToHash(action.stateName);
        }
    }

    private void CacheTotalWeight()
    {
        _totalWeight = 0;
        foreach (IdleAction action in data.actions)
            if (action != null) _totalWeight += Mathf.Max(1, action.weight);
    }

    private void Start() => PickNextAction();

    private void Update()
    {
        if (_waiting)
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f) PickNextAction();
            return;
        }

        if (!_walking)
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f) EnterWait();
            return;
        }

        Vector3 position = _tf.position;
        Vector3 delta = _target - position;
        float sqrDist = delta.sqrMagnitude;

        if (sqrDist < ArrivalSqrThreshold)
        {
            EnterWait();
            return;
        }

        float dist = Mathf.Sqrt(sqrDist);
        float stepLength = data.moveSpeed * Time.deltaTime;
        if (stepLength > dist) stepLength = dist;

        _tf.position = position + delta * (stepLength / dist);

        if (spriteRenderer != null && Mathf.Abs(delta.x) > FlipDeadzone)
        {
            bool faceRight = delta.x > 0f;
            bool flip = !faceRight;
            if (spriteRenderer.flipX != flip) spriteRenderer.flipX = flip;
        }
    }

    private void PickNextAction()
    {
        int actionIndex = WeightedRandomIndex();
        IdleAction action = data.actions[actionIndex];
        _waiting = false;

        if (action.type == IdleAction.ActionType.Stroll)
        {
            float minDist = ArrivalThreshold * 4f;
            float dx = Random.Range(minDist, Mathf.Max(minDist * 2f, data.strollRadius));
            if (Random.value < 0.5f) dx = -dx;

            _target = _origin + new Vector3(dx, 0f, 0f);
            _walking = true;
            animator.Play(_actionHashes[actionIndex]);
        }
        else
        {
            _walking = false;
            _timer = action.duration;
            animator.Play(_actionHashes[actionIndex]);
        }
    }

    private int WeightedRandomIndex()
    {
        if (_totalWeight <= 0) return 0;

        int roll = Random.Range(0, _totalWeight);
        int cumulative = 0;

        for (int i = 0; i < data.actions.Length; i++)
        {
            IdleAction action = data.actions[i];
            if (action == null) continue;

            cumulative += Mathf.Max(1, action.weight);
            if (roll < cumulative) return i;
        }

        return data.actions.Length - 1;
    }
    private void EnterWait()
    {
        _walking = false;
        _waiting = true;
        _timer = Random.Range(data.pauseMin, data.pauseMax);
        animator.Play(_defaultHash);
    }
    public void Pause()
    {
        _walking = false;
        _waiting = false;
        enabled = false;
    }

    public void Resume()
    {
        if (_stopped) return;
        if (data == null || animator == null) return;

        enabled = true;
        EnterWait();
    }
    public void StopPermanently()
    {
        _stopped = true;
        _walking = false;
        _waiting = false;
        enabled = false;
    }
    public bool IsPaused => !enabled;
    public bool IsStopped => _stopped;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (data == null) return;

        Vector3 center = Application.isPlaying ? _origin : transform.position;
        Vector3 a = center + Vector3.left * data.strollRadius;
        Vector3 b = center + Vector3.right * data.strollRadius;

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
        Gizmos.DrawLine(a, b);
        Gizmos.DrawWireCube(a, Vector3.one * 0.1f);
        Gizmos.DrawWireCube(b, Vector3.one * 0.1f);
    }
#endif
}