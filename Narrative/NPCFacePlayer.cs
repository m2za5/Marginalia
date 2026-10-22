using UnityEngine;

[DisallowMultipleComponent]
public class NPCBillboardFacing : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform player;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Flip")]
    [SerializeField] private bool spriteFacesRightByDefault = true;
    [SerializeField, Min(0f)] private float flipDeadZone = 0.05f;

    private CutsceneManager subscribedCutsceneManager;
    private bool isSuspendedByCutscene;

    // While suspended, a Timeline or cutscene logic owns this NPC's rotation and flip.
    // A story sequence keeps it suspended between its Timeline steps (dialogue, walk).
    public bool IsSuspendedByCutscene => isSuspendedByCutscene
        || StorySequenceRunner.IsAnySequenceRunning;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
    }

    // Cutscene-authored facing, e.g. from a Timeline Signal Receiver.
    // Kept while a cutscene plays; automatic flipping resumes when it ends.
    public void SetFacingLeft() => SetFacing(false);

    public void SetFacingRight() => SetFacing(true);

    private void SetFacing(bool faceRight)
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
            return;

        spriteRenderer.flipX = spriteFacesRightByDefault
            ? !faceRight
            : faceRight;
    }

    private void OnEnable()
    {
        SubscribeToCutsceneManager();
    }

    private void OnDisable()
    {
        UnsubscribeFromCutsceneManager();
        isSuspendedByCutscene = false;
    }

    private void LateUpdate()
    {
        // CutsceneManager may be created after this NPC is enabled.
        SubscribeToCutsceneManager();

        if (IsSuspendedByCutscene)
            return;

        ResolveReferences();

        if (targetCamera == null)
            return;

        transform.rotation = Quaternion.Euler(
            0f,
            targetCamera.transform.eulerAngles.y,
            0f);

        if (player == null || spriteRenderer == null)
            return;

        UpdatePlayerFacing();
    }

    private void SubscribeToCutsceneManager()
    {
        CutsceneManager manager = CutsceneManager.Instance;

        // ReferenceEquals: a destroyed manager must not keep this NPC suspended.
        if (ReferenceEquals(subscribedCutsceneManager, manager))
            return;

        UnsubscribeFromCutsceneManager();
        subscribedCutsceneManager = manager;

        if (subscribedCutsceneManager == null)
        {
            isSuspendedByCutscene = false;
            return;
        }

        subscribedCutsceneManager.OnCutsceneStarted += HandleCutsceneStarted;
        subscribedCutsceneManager.OnCutsceneFinished += HandleCutsceneFinished;
        isSuspendedByCutscene = subscribedCutsceneManager.IsCutsceneActive;
    }

    private void UnsubscribeFromCutsceneManager()
    {
        if (subscribedCutsceneManager == null)
            return;

        subscribedCutsceneManager.OnCutsceneStarted -= HandleCutsceneStarted;
        subscribedCutsceneManager.OnCutsceneFinished -= HandleCutsceneFinished;
        subscribedCutsceneManager = null;
    }

    private void HandleCutsceneStarted(string cutsceneId)
    {
        isSuspendedByCutscene = true;
    }

    private void HandleCutsceneFinished(string cutsceneId)
    {
        // Stay suspended while another cutscene is still playing.
        isSuspendedByCutscene = subscribedCutsceneManager != null
            && subscribedCutsceneManager.IsCutsceneActive;
    }

    private void ResolveReferences()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (player == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");

            if (playerObject != null)
                player = playerObject.transform;
        }
    }

    private void UpdatePlayerFacing()
    {
        Vector3 toPlayer = player.position - transform.position;
        Vector3 screenRight = Vector3.ProjectOnPlane(
            targetCamera.transform.right,
            Vector3.up);

        toPlayer = Vector3.ProjectOnPlane(toPlayer, Vector3.up);

        if (screenRight.sqrMagnitude < 0.0001f ||
            toPlayer.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float side = Vector3.Dot(
            toPlayer.normalized,
            screenRight.normalized);

        if (Mathf.Abs(side) <= flipDeadZone)
            return;

        bool playerIsOnRight = side > 0f;

        spriteRenderer.flipX = spriteFacesRightByDefault
            ? !playerIsOnRight
            : playerIsOnRight;
    }
}