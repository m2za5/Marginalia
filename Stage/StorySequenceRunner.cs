using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

[DisallowMultipleComponent]
public sealed class StorySequenceRunner : MonoBehaviour
{
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private StorySequenceStep[] steps = Array.Empty<StorySequenceStep>();
    [Tooltip("For example, NPC idle movement that would compete with the Timeline. Restored on every exit.")]
    [SerializeField] private Behaviour[] suspendDuringSequence = Array.Empty<Behaviour>();
    [SerializeField] private UnityEvent onCompleted = new();
    [SerializeField] private UnityEvent onCancelled = new();

    // Covers every step (dialogue, walk, SetActive), not only the Timeline steps CutsceneManager reports.
    private static readonly HashSet<StorySequenceRunner> RunningSequences = new();
    public static bool IsAnySequenceRunning => RunningSequences.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRunningSequences() => RunningSequences.Clear();

    public bool IsRunning { get; private set; }
    public bool HasCompleted { get; private set; }
    public int CurrentStepIndex { get; private set; } = -1;
    public string LastFailure { get; private set; }

    private PlayerController player;
    private PlayerMovement movement;
    private Rigidbody body;
    private Health health;
    private bool previousControllerEnabled;
    private bool previousMovementEnabled;
    private bool[] suspendedStates;
    private StorySequenceStep currentStep;
    private float elapsed;
    private float walkStartY;
    private bool walking;
    private bool restoringState;
    private GameStateManager gameState;
    private GameState previousGameState;

    private CutsceneManager cutscenes;
    private PlayableDirector activeDirector;
    private string activeCutsceneId;
    private bool timelineFinished;
    private bool timelineCancelled;
    private bool ownsTimeline;
    private DialogueManager dialogues;
    private Dialogue awaitedDialogue;
    private bool dialogueFinished;

    private Camera activeGameplayCamera;
    private Camera activeCutsceneCamera;
    private bool previousGameplayCameraEnabled;
    private bool previousCutsceneCameraEnabled;

    public bool TryBegin(PlayerController candidate)
    {
        if (!isActiveAndEnabled || IsRunning || restoringState || candidate == null
            || !candidate.isActiveAndEnabled || !candidate.IsInputAllowed)
            return false;

        PlayerMovement candidateMovement = candidate.GetComponent<PlayerMovement>();
        Rigidbody candidateBody = candidate.GetComponent<Rigidbody>();
        if (candidateMovement == null || !candidateMovement.isActiveAndEnabled
            || candidateBody == null || candidateBody.isKinematic)
            return Reject("An active PlayerMovement and a dynamic Rigidbody are required.");
        if (!ValidateConfiguration(out string error)) return Reject(error);

        player = candidate;
        movement = candidateMovement;
        body = candidateBody;
        health = candidate.GetComponent<Health>();
        gameState = GameStateManager.Instance;
        if (gameState != null) previousGameState = gameState.CurrentState;
        previousControllerEnabled = player.enabled;
        previousMovementEnabled = movement.enabled;
        suspendDuringSequence ??= Array.Empty<Behaviour>();
        suspendedStates = new bool[suspendDuringSequence.Length];
        for (int i = 0; i < suspendDuringSequence.Length; i++)
            suspendedStates[i] = suspendDuringSequence[i] != null && suspendDuringSequence[i].enabled;

        LastFailure = null;
        HasCompleted = false;
        CurrentStepIndex = -1;
        currentStep = null;
        IsRunning = true;
        RunningSequences.Add(this);
        try
        {
            // OnDisable clears gameplay input. Do not overwrite the shared lifecycle lock.
            player.enabled = false;
            movement.StopHorizontalMovement();
            // Camera orbit and other input readers also observe the existing game state.
            if (gameState != null) gameState.SetCutscene();
            for (int i = 0; i < suspendDuringSequence.Length; i++)
                if (suspendDuringSequence[i] != null) suspendDuringSequence[i].enabled = false;
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            return false;
        }
        return true;
    }

    private bool Reject(string error)
    {
        LastFailure = error;
        Debug.LogWarning($"[StorySequenceRunner] {name}: {error}", this);
        return false;
    }

    public bool ValidateConfiguration(out string error)
    {
        error = null;
        if (steps == null || steps.Length == 0) error = "Add at least one step.";
        else for (int i = 0; i < steps.Length; i++)
        {
            StorySequenceStep step = steps[i];
            if (step == null) error = "Step is missing.";
            else switch (step.type)
            {
                case StoryStepType.WaitForLanding:
                    if (!Positive(step.timeout)) error = "Landing timeout must be positive.";
                    break;
                case StoryStepType.AutoWalk:
                    if (step.destination == null || !Positive(step.moveSpeed)
                        || !Positive(step.stopDistance) || !Positive(step.timeout))
                        error = "Assign a destination and positive walk speed, stop distance and timeout.";
                    break;
                case StoryStepType.PlayTimeline:
                    if (string.IsNullOrWhiteSpace(step.cutsceneId) || step.director == null
                        || step.director.playableAsset == null)
                        error = "Assign a cutscene ID and a scene Director with a Timeline asset.";
                    else if (step.director.playOnAwake || step.director.extrapolationMode != DirectorWrapMode.None)
                        error = "Director requires Play On Awake OFF and Wrap Mode None.";
                    else if (double.IsNaN(step.director.duration) || double.IsInfinity(step.director.duration)
                        || step.director.duration <= 0.001)
                        error = "Timeline has no playable duration. Author its tracks first.";
                    else if (step.cutsceneCamera != null && (step.cutsceneCamera == gameplayCamera
                        || !step.cutsceneCamera.gameObject.activeInHierarchy))
                        error = "Use a separate cutscene Camera on an active GameObject.";
                    break;
                case StoryStepType.Dialogue:
                    if (step.dialogue == null || step.dialogue.lines == null || step.dialogue.lines.Count == 0)
                        error = "Assign a non-empty Dialogue asset.";
                    break;
                case StoryStepType.SetActive:
                    if (step.target == null || (!step.active && transform.IsChildOf(step.target.transform)))
                        error = "SetActive requires a target other than this Runner or its parents.";
                    break;
                default:
                    error = "Unknown step type.";
                    break;
            }
            if (error != null)
            {
                error = $"Step {i + 1}: {error}";
                break;
            }
        }
        if (error == null && suspendDuringSequence != null)
            foreach (Behaviour component in suspendDuringSequence)
                if (component == this || component is PlayerController || component is PlayerMovement
                    || component is PlayableDirector || component is DialogueManager || component is CutsceneManager)
                {
                    error = "Suspend only supporting NPC behaviours, not sequence/player/manager components.";
                    break;
                }
        return error == null;
    }

    private static bool Positive(float value) => value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);

    private void Update()
    {
        if (!IsRunning) return;
        try
        {
            if (player == null || movement == null || body == null
                || !player.gameObject.activeInHierarchy || (health != null && !health.IsAlive))
            {
                Fail("Player became unavailable.");
                return;
            }
            if (currentStep == null)
            {
                BeginNextStep();
                return;
            }
            if (currentStep.type == StoryStepType.PlayTimeline)
            {
                if (cutscenes == null || activeDirector == null || timelineCancelled)
                    Fail("Timeline was cancelled or its Director/manager was removed.");
                else if (timelineFinished) FinishStep();
            }
            else if (currentStep.type == StoryStepType.Dialogue)
            {
                if (dialogues == null) Fail("DialogueManager was removed.");
                else if (dialogueFinished) FinishStep();
                else if (!dialogues.IsDialogueActive || dialogues.CurrentDialogue != awaitedDialogue)
                    Fail("The requested dialogue is no longer active.");
            }
        }
        catch (Exception exception) { Fail(exception.Message); }
    }

    private void BeginNextStep()
    {
        CurrentStepIndex++;
        if (CurrentStepIndex >= steps.Length)
        {
            EndSequence(true);
            return;
        }
        currentStep = steps[CurrentStepIndex];
        elapsed = 0f;
        switch (currentStep.type)
        {
            case StoryStepType.PlayTimeline: BeginTimeline(); break;
            case StoryStepType.AutoWalk:
                if (currentStep.destination == null || !movement.IsGrounded || movement.IsMovementLocked)
                {
                    Fail("AutoWalk requires a destination and a grounded, unrestricted player.");
                    return;
                }
                movement.StopHorizontalMovement();
                movement.enabled = false;
                walkStartY = body.position.y;
                walking = true;
                break;
            case StoryStepType.Dialogue: BeginDialogue(); break;
            case StoryStepType.SetActive:
                if (currentStep.target == null || (!currentStep.active
                    && player.transform.IsChildOf(currentStep.target.transform)))
                {
                    Fail("SetActive target was removed or would disable the player.");
                    return;
                }
                currentStep.target.SetActive(currentStep.active);
                FinishStep();
                break;
        }
    }

    private void FixedUpdate()
    {
        if (!IsRunning || currentStep == null || body == null || movement == null) return;
        try
        {
            if (currentStep.type == StoryStepType.WaitForLanding)
            {
                if (movement.IsGrounded) FinishStep();
                else TickTimeout();
            }
            else if (walking)
            {
                if (currentStep.destination == null || movement.IsMovementLocked
                    || Mathf.Abs(body.position.y - walkStartY) > 0.75f)
                {
                    Fail("AutoWalk lost its target or left the supported level path.");
                    return;
                }
                Vector3 delta = currentStep.destination.position - body.position;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance <= currentStep.stopDistance)
                {
                    FinishStep();
                    return;
                }
                Vector3 direction = delta / distance;
                FaceAlongMovement(direction);
                // Limit the last physics step; never teleport through a collider to the target.
                float speed = Mathf.Min(currentStep.moveSpeed, distance / Time.fixedDeltaTime);
                Vector3 velocity = body.linearVelocity;
                velocity.x = direction.x * speed;
                velocity.z = direction.z * speed;
                body.linearVelocity = velocity;
                TickTimeout();
            }
        }
        catch (Exception exception) { Fail(exception.Message); }
    }

    private void TickTimeout()
    {
        elapsed += Time.fixedDeltaTime;
        if (elapsed >= currentStep.timeout) Fail("Landing/walking timed out. Check the floor and path.");
    }

    private void FaceAlongMovement(Vector3 direction)
    {
        // Query the existing public input mapping, including custom camera references.
        movement.SetMoveInput(Vector2.right);
        Vector3 right = movement.MoveDirection;
        movement.SetMoveInput(Vector2.up);
        Vector3 forward = movement.MoveDirection;
        float determinant = right.x * forward.z - forward.x * right.z;
        Vector2 input = Mathf.Abs(determinant) > 0.0001f
            ? new Vector2((direction.x * forward.z - forward.x * direction.z) / determinant,
                (right.x * direction.z - direction.x * right.z) / determinant)
            : new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward));
        movement.SetMoveInput(input);
        // Keep facing but clear synthetic input so Idle resumes when velocity reaches zero.
        movement.StopHorizontalMovement();
    }

    private void BeginTimeline()
    {
        cutscenes = CutsceneManager.Instance;
        activeDirector = currentStep.director;
        activeCutsceneId = currentStep.cutsceneId;
        timelineFinished = false;
        timelineCancelled = false;
        ownsTimeline = false;
        if (cutscenes == null || activeDirector == null)
        {
            Fail("CutsceneManager or Director is missing.");
            return;
        }
        SwitchCamera(currentStep.cutsceneCamera);
        cutscenes.OnCutsceneFinished += HandleTimelineFinished;
        ownsTimeline = cutscenes.PlayCutscene(activeCutsceneId, activeDirector);
        if (!ownsTimeline)
            Fail("Director could not start. Check its bindings and whether it is already playing.");
    }

    private void HandleTimelineFinished(string id)
    {
        if (id != activeCutsceneId) return;
        timelineCancelled = cutscenes.WasCancelled(id);
        timelineFinished = true;
    }

    private void BeginDialogue()
    {
        dialogues = DialogueManager.Instance;
        awaitedDialogue = currentStep.dialogue;
        dialogueFinished = false;
        if (dialogues == null || dialogues.IsDialogueActive || awaitedDialogue == null)
        {
            Fail("DialogueManager is unavailable/busy or the dialogue is missing.");
            return;
        }
        dialogues.OnDialogueEnded += HandleDialogueFinished;
        // Let DialogueManager capture the real prior state, so cancellation during dialogue
        // cannot leave a stale Cutscene state after its UI eventually closes.
        RestoreGameState();
        dialogues.StartDialogue(awaitedDialogue);
        if (!dialogueFinished && dialogues.CurrentDialogue != awaitedDialogue)
            Fail("Dialogue did not start.");
    }

    private void HandleDialogueFinished()
    {
        if (dialogues != null && dialogues.LastFinishedDialogue == awaitedDialogue)
        {
            dialogueFinished = true;
            if (IsRunning && gameState != null && gameState.CurrentState == previousGameState)
                gameState.SetCutscene();
        }
    }

    private void SwitchCamera(Camera cutsceneCamera)
    {
        if (cutsceneCamera == null) return; // A Timeline may own camera switching itself.
        Camera main = gameplayCamera != null ? gameplayCamera : Camera.main;
        if (main == null || main == cutsceneCamera)
            throw new InvalidOperationException("Assign separate gameplay and cutscene Cameras.");
        activeGameplayCamera = main;
        activeCutsceneCamera = cutsceneCamera;
        previousGameplayCameraEnabled = main.enabled;
        previousCutsceneCameraEnabled = cutsceneCamera.enabled;
        main.enabled = false;
        cutsceneCamera.enabled = true;
    }

    private void RestoreCamera()
    {
        if (activeCutsceneCamera != null) activeCutsceneCamera.enabled = previousCutsceneCameraEnabled;
        if (activeGameplayCamera != null) activeGameplayCamera.enabled = previousGameplayCameraEnabled;
        activeGameplayCamera = null;
        activeCutsceneCamera = null;
    }

    private void RestoreGameState()
    {
        if (gameState != null && gameState.IsState(GameState.Cutscene))
            gameState.ChangeState(previousGameState);
    }

    private void FinishStep()
    {
        ReleaseStep();
        currentStep = null;
    }

    private void ReleaseStep()
    {
        try
        {
            if (cutscenes != null)
            {
                cutscenes.OnCutsceneFinished -= HandleTimelineFinished;
                if (ownsTimeline && !timelineFinished
                    && cutscenes.IsPlaying(activeCutsceneId, activeDirector))
                    cutscenes.CancelCutscene(activeCutsceneId, activeDirector);
            }
        }
        finally
        {
            cutscenes = null;
            ownsTimeline = false;
            activeDirector = null;
            activeCutsceneId = null;
            if (dialogues != null) dialogues.OnDialogueEnded -= HandleDialogueFinished;
            dialogues = null;
            awaitedDialogue = null;
            RestoreCamera();
            if (walking)
            {
                walking = false;
                if (movement != null)
                {
                    movement.StopHorizontalMovement();
                    movement.enabled = previousMovementEnabled;
                }
            }
        }
    }

    public void Cancel()
    {
        if (!IsRunning) return;
        LastFailure = "Sequence cancelled.";
        EndSequence(false);
    }

    private void Fail(string reason)
    {
        LastFailure = $"Step {CurrentStepIndex + 1}: {reason}";
        Debug.LogWarning($"[StorySequenceRunner] {name}: {LastFailure}", this);
        if (IsRunning) EndSequence(false);
    }

    private void EndSequence(bool completed)
    {
        // Set first: callbacks fired during cleanup cannot advance/re-enter this sequence.
        IsRunning = false;
        RunningSequences.Remove(this);
        restoringState = true;
        try { ReleaseStep(); }
        catch (Exception exception) { Debug.LogException(exception, this); }
        if (movement != null)
        {
            movement.StopHorizontalMovement();
            movement.enabled = previousMovementEnabled;
        }
        for (int i = 0; i < suspendedStates.Length; i++)
            if (suspendDuringSequence[i] != null) suspendDuringSequence[i].enabled = suspendedStates[i];
        RestoreGameState();
        if (player != null) player.enabled = previousControllerEnabled;
        player = null;
        movement = null;
        body = null;
        health = null;
        gameState = null;
        currentStep = null;
        restoringState = false;
        HasCompleted = completed;
        if (completed) onCompleted.Invoke();
        else onCancelled.Invoke();
    }

    private void OnDisable() => Cancel();
}
