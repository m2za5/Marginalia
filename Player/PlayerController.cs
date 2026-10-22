using UnityEngine;
using UnityEngine.InputSystem;

public enum DrawingPenInputMode
{
    Hold,
    Toggle
}

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement), typeof(PlayerAttack), typeof(PlayerInteractor))]
[RequireComponent(typeof(PlayerDrawingSkill), typeof(PlayerDrawingOverlay), typeof(PlayerInput))]
[RequireComponent(typeof(PlayerCombatDrawingLoadout))]
public class PlayerController : MonoBehaviour
{
    private const string PlayerActionMapName = "Player";
    private const string KeyboardMouseControlSchemeName = "Keyboard&Mouse";

    [Header("References")]
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerAttack attack;
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private PlayerDrawingSkill drawingSkill;
    [SerializeField] private PlayerDrawingOverlay drawingOverlay;
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private Health health;
    [SerializeField] private PlayerCombatStateController combatState;

    [Header("Input Gate")]
    [SerializeField] private bool allowInputWhenGameStateMissing = true;

    [Header("Drawing Cursor")]
    [SerializeField] private bool useDrawingPenCursor = true;
    [SerializeField] private Texture2D drawingPenCursorTexture;
    [SerializeField] private Vector2 drawingPenCursorHotspot = new Vector2(12f, 58f);

    [Header("Drawing Accessibility")]
    [SerializeField] private DrawingPenInputMode drawingPenInputMode = DrawingPenInputMode.Hold;

    private InputActionMap cachedActionMap;
    private InputAction moveAction;
    private InputAction attackAction;
    private InputAction interactAction;
    private InputAction jumpAction;
    private InputAction _dashAction;
    private InputAction drawingSkillAction;
    private InputAction drawingSubmitAction;
    private InputAction drawingCancelAction;
    private InputAction pauseAction;
    private InputAction pointerPositionAction;
    private bool wasDrawingPenHeld;
    private bool toggledDrawingPenActive;
    private bool wasInputAllowed;
    private bool ownsDrawingCursorState;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;
    private bool isUsingDrawingPenCursor;
    private DialogueManager subscribedDialogueManager;
    private CutsceneManager subscribedCutsceneManager;
    private bool isDialogueInputBlocked;
    private bool isCutsceneInputBlocked;
    private bool lifecycleInputBlocked;
    private bool allowDrawingInputWhileDebugUIOpen;
    private bool allowCameraInputWhileDebugUIOpen;
    private bool applicationHasFocus;
    private bool applicationPaused;
    private bool externalInterruptionHandled;

    public Vector2 MoveInput => movement != null ? movement.MoveInput : Vector2.zero;
    public int FacingDirection => movement != null ? movement.FacingDirection : 1;
    public bool IsFacingRight => movement == null || movement.IsFacingRight;
    public Vector3 FacingVector => movement != null ? movement.FacingVector : Vector3.right;
    public PlayerDrawingSkill DrawingSkill => drawingSkill;
    public bool IsDrawingSkillActive => drawingSkill != null && drawingSkill.IsDrawing;
    public DrawingPenInputMode DrawingPenMode => drawingPenInputMode;
    public bool IsInCombat => combatState != null && combatState.IsInCombat;
    public bool IsCameraInputAllowed =>
        !BaseDebugUI.IsGameplayPausedByDebugUI
        && (allowCameraInputWhileDebugUIOpen || !BaseDebugUI.IsAnyDebugUIOpen)
        && IsPlayerStateInputAllowed;
    public bool IsInputAllowed =>
        !BaseDebugUI.IsAnyDebugUIOpen
        && IsPlayerStateInputAllowed;
    public bool HandlesContextualCancelInput =>
        isActiveAndEnabled
        && pauseAction != null
        && !BaseDebugUI.IsAnyDebugUIOpen;
    private bool IsMovementInputAllowed =>
        !BaseDebugUI.IsGameplayPausedByDebugUI
        && IsPlayerStateInputAllowed;
    private bool IsDrawingInputAllowed =>
        !BaseDebugUI.IsGameplayPausedByDebugUI
        && (allowDrawingInputWhileDebugUIOpen || !BaseDebugUI.IsAnyDebugUIOpen)
        && IsPlayerStateInputAllowed;
    private bool IsPlayerStateInputAllowed =>
        !PaperInspectable.IsAnyOpen
        && !ClockDrawerRevealSequence.IsPlaying
        && !lifecycleInputBlocked
        && (health == null || health.IsAlive)
        && !isDialogueInputBlocked
        && !isCutsceneInputBlocked
        && (GameStateManager.Instance == null
            ? allowInputWhenGameStateMissing
            : GameStateManager.Instance.IsState(GameState.Playing));

    private void Awake()
    {
        ResolveReferences();
        ResolveInputActions();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToDialogueManager();
        SubscribeToCutsceneManager();
        SubscribeToCombatState();
        ResolveInputActions();
        SubscribeToPlayerInputEvents();
    }

    private void Start()
    {
        SubscribeToDialogueManager();
        SubscribeToCutsceneManager();
    }

    private void Update()
    {
        SubscribeToDialogueManager();
        SubscribeToCutsceneManager();
        ResolveInputActions();

        if (!BaseDebugUI.IsAnyDebugUIOpen && HandleContextualPauseInput())
        {
            return;
        }

        bool inputAllowed = IsInputAllowed;

        if (!inputAllowed)
        {
            if (IsDrawingInputAllowed && UpdateDrawingSkillInput())
            {
                wasInputAllowed = false;
                return;
            }

            if (IsMovementInputAllowed)
            {
                if (wasInputAllowed || IsDrawingSkillActive)
                {
                    ClearInput();
                }

                UpdateMoveInput();
                wasInputAllowed = false;
                return;
            }

            if (wasInputAllowed || IsDrawingSkillActive || MoveInput.sqrMagnitude > 0f)
            {
                ClearInput();
            }

            wasInputAllowed = false;
            return;
        }

        wasInputAllowed = true;

        if (UpdateDrawingSkillInput())
        {
            return;
        }

        UpdateMoveInput();
        UpdateDashInput();
        UpdateJumpInput();

        if (WasPressedThisFrame(attackAction))
        {
            RequestAttack();
        }

        if (WasPressedThisFrame(interactAction))
        {
            RequestInteract();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromDialogueManager();
        UnsubscribeFromCutsceneManager();
        UnsubscribeFromCombatState();
        UnsubscribeFromPlayerInputEvents();
        isDialogueInputBlocked = false;
        isCutsceneInputBlocked = false;
        wasInputAllowed = false;
        ClearInput(DrawingCancelReason.Disabled);
    }

    private void OnDestroy()
    {
        UnsubscribeFromDialogueManager();
        UnsubscribeFromCutsceneManager();
        UnsubscribeFromCombatState();
    }

    public void SetMoveInput(Vector2 input)
    {
        if (movement == null)
        {
            return;
        }

        movement.SetMoveInput(IsMovementInputAllowed && !IsDrawingSkillActive
            ? Vector2.ClampMagnitude(input, 1f)
            : Vector2.zero);
    }

    public void RequestJump()
    {
        if (!IsInputAllowed
            || movement == null
            || IsDrawingSkillActive
            || (attack != null && attack.IsAttacking))
        {
            return;
        }

        movement.RequestJump();
    }

    public void ReleaseJump()
    {
        if (movement != null)
        {
            movement.ReleaseJump();
        }
    }

    public bool RequestAttack()
    {
        return IsInputAllowed && attack != null && !IsDrawingSkillActive && attack.TryAttack();
    }

    public bool RequestDrawingSkillStart()
    {
        return RequestDrawingSkillStart(ReadPointerScreenPosition());
    }

    public bool RequestDrawingSkillStart(Vector2 screenPosition)
    {
        return !IsInCombat && BeginDrawingSkill(screenPosition);
    }

    public bool RequestCombatDrawingSkillStart(Vector2 screenPosition)
    {
        return IsInCombat && BeginDrawingSkill(screenPosition);
    }

    private bool BeginDrawingSkill(Vector2 screenPosition)
    {
        if (!IsDrawingInputAllowed || drawingSkill == null)
        {
            return false;
        }

        bool started = drawingSkill.BeginDrawing(screenPosition, IsInCombat);

        if (started)
        {
            wasDrawingPenHeld = false;
            BeginDrawingCursorOverride();
            UpdateDrawingPenCursor(IsPointerOverDrawingPaper(screenPosition));
        }

        return started;
    }

    private bool TryBeginAreaDrawing(Vector2 screenPosition)
    {
        PlayerAreaDrawingSkillCaster caster = GetComponent<PlayerAreaDrawingSkillCaster>();
        return caster != null && caster.TryBeginDrawing(this, screenPosition);
    }

    public bool RequestDrawingSkillToggle()
    {
        return RequestDrawingSkillToggle(ReadPointerScreenPosition());
    }

    public bool RequestDrawingSkillToggle(Vector2 screenPosition)
    {
        return IsDrawingSkillActive
            ? RequestDrawingSkillSubmit(screenPosition)
            : RequestDrawingSkillStart(screenPosition);
    }

    public bool RequestDrawingSkillContinue()
    {
        return RequestDrawingSkillContinue(ReadPointerScreenPosition());
    }

    public bool RequestDrawingSkillContinue(Vector2 screenPosition)
    {
        return IsDrawingInputAllowed
            && drawingSkill != null
            && drawingSkill.ContinueDrawing(screenPosition);
    }

    public bool RequestDrawingSkillEnd()
    {
        return RequestDrawingSkillEnd(ReadPointerScreenPosition());
    }

    public bool RequestDrawingSkillEnd(Vector2 screenPosition)
    {
        if (drawingSkill == null)
        {
            return false;
        }

        bool ended = drawingSkill.EndDrawing(screenPosition);

        if (ended)
        {
            FinishDrawingInput();
        }

        return ended;
    }

    public bool RequestDrawingSkillSubmit()
    {
        return RequestDrawingSkillSubmit(ReadPointerScreenPosition());
    }

    public bool RequestDrawingSkillSubmit(Vector2 screenPosition)
    {
        if (drawingSkill == null)
        {
            return false;
        }

        bool submitted = drawingSkill.SubmitDrawing(screenPosition);

        if (submitted)
        {
            FinishDrawingInput();
        }

        return submitted;
    }

    public bool CancelDrawingSkill(DrawingCancelReason reason = DrawingCancelReason.Manual)
    {
        bool canceled = drawingSkill != null && drawingSkill.CancelDrawing(reason);

        if (canceled || ownsDrawingCursorState)
        {
            FinishDrawingInput();
        }

        return canceled;
    }

    public bool RequestInteract()
    {
        return IsInputAllowed && interactor != null && !IsDrawingSkillActive && interactor.TryInteract();
    }

    public void SetDrawingPenInputMode(DrawingPenInputMode mode)
    {
        drawingPenInputMode = mode;
        toggledDrawingPenActive = false;
        wasDrawingPenHeld = false;
    }

    public bool TryApplyBindingOverride(
        string actionName,
        int bindingIndex,
        string controlPath)
    {
        ResolveInputActions();

        if (cachedActionMap == null
            || string.IsNullOrWhiteSpace(actionName)
            || string.IsNullOrWhiteSpace(controlPath))
        {
            return false;
        }

        InputAction action = cachedActionMap.FindAction(actionName, false);

        if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
        {
            return false;
        }

        InputBinding targetBinding = action.bindings[bindingIndex];

        if (targetBinding.isComposite
            || !BindingBelongsToControlScheme(
                targetBinding,
                KeyboardMouseControlSchemeName))
        {
            return false;
        }

        string trimmedPath = controlPath.Trim();

        if (!IsConcreteKeyboardMouseControlPath(trimmedPath))
        {
            return false;
        }

        foreach (InputAction candidateAction in cachedActionMap.actions)
        {
            for (int i = 0; i < candidateAction.bindings.Count; i++)
            {
                if (candidateAction == action && i == bindingIndex)
                {
                    continue;
                }

                InputBinding candidate = candidateAction.bindings[i];

                if (!candidate.isComposite
                    && string.Equals(
                        candidate.effectivePath,
                        trimmedPath,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        action.ApplyBindingOverride(bindingIndex, trimmedPath);
        return true;
    }

    private static bool BindingBelongsToControlScheme(
        InputBinding binding,
        string controlSchemeName)
    {
        if (string.IsNullOrWhiteSpace(binding.groups)
            || string.IsNullOrWhiteSpace(controlSchemeName))
        {
            return false;
        }

        string[] groups = binding.groups.Split(';');

        foreach (string group in groups)
        {
            if (string.Equals(
                group.Trim(),
                controlSchemeName,
                System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConcreteKeyboardMouseControlPath(string controlPath)
    {
        if (controlPath.IndexOf('*') >= 0)
        {
            return false;
        }

        string deviceLayout = InputControlPath.TryGetDeviceLayout(controlPath);

        if (!string.Equals(
                deviceLayout,
                nameof(Keyboard),
                System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                deviceLayout,
                nameof(Mouse),
                System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(
            InputControlPath.TryGetControlLayout(controlPath));
    }

    public string GetEffectiveBindingPath(string actionName, int bindingIndex)
    {
        ResolveInputActions();
        InputAction action = cachedActionMap != null
            ? cachedActionMap.FindAction(actionName, false)
            : null;
        return action != null && bindingIndex >= 0 && bindingIndex < action.bindings.Count
            ? action.bindings[bindingIndex].effectivePath
            : string.Empty;
    }

    public void ResetBindingOverrides()
    {
        ResolveInputActions();
        cachedActionMap?.RemoveAllBindingOverrides();
    }

    public void SetLifecycleInputBlocked(bool blocked)
    {
        lifecycleInputBlocked = blocked;

        if (blocked)
        {
            ClearInput();
        }
    }

    public void SetDrawingInputAllowedWhileDebugUIOpen(bool allowed)
    {
        allowDrawingInputWhileDebugUIOpen = allowed;
        if (!allowed && BaseDebugUI.IsAnyDebugUIOpen)
        {
            CancelDrawingSkill(DrawingCancelReason.StateChanged);
        }
    }

    public void SetCameraInputAllowedWhileDebugUIOpen(bool allowed)
    {
        allowCameraInputWhileDebugUIOpen = allowed;
    }

    private bool HandleContextualPauseInput()
    {
        if (!WasPressedThisFrame(pauseAction))
        {
            return false;
        }

        return RequestContextualCancelOrPause();
    }

    public bool RequestContextualCancelOrPause()
    {
        if (PlayerInventoryUI.TryCloseAny())
        {
            return true;
        }

        if (ShopController.TryHandleCancelInput())
        {
            return true;
        }

        if (IsDrawingSkillActive)
        {
            CancelDrawingSkill(DrawingCancelReason.Manual);
            return true;
        }

        GameStateManager manager = GameStateManager.Instance;

        if (manager == null
            || (!manager.IsState(GameState.Playing) && !manager.IsState(GameState.Paused)))
        {
            return false;
        }

        manager.TogglePause();
        return true;
    }

    private void UpdateMoveInput()
    {
        Vector2 move = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        SetMoveInput(move);
    }

    private void UpdateJumpInput()
    {
        if (movement == null)
        {
            return;
        }

        bool jumpHeld = jumpAction != null && jumpAction.IsPressed();
        movement.SetJumpHeld(jumpHeld);

        if (WasPressedThisFrame(jumpAction))
        {
            RequestJump();
        }

        if (WasReleasedThisFrame(jumpAction))
        {
            ReleaseJump();
        }
    }

    private void UpdateDashInput()
    {
        if (WasPressedThisFrame(_dashAction)
            && movement != null
            && !IsDrawingSkillActive
            && (attack == null || !attack.IsAttacking))
        {
            movement.RequestDash();
        }
    }

    private bool UpdateDrawingSkillInput()
    {
        bool togglePressed = WasPressedThisFrame(drawingSkillAction);

        if (!IsDrawingSkillActive)
        {
            EndDrawingCursorOverride();

            if (!togglePressed)
            {
                return false;
            }

            Vector2 startScreenPosition = ReadPointerScreenPosition();
            if (IsInCombat)
            {
                PlayerAreaDrawingSkillCaster caster = GetComponent<PlayerAreaDrawingSkillCaster>();
                if (caster != null && caster.HasActiveEnvironmentDrawing)
                {
                    caster.TryBeginEnvironmentDrawing(this, startScreenPosition);
                }
                else
                {
                    TryBeginAreaDrawing(startScreenPosition);
                }
            }
            else
            {
                RequestDrawingSkillStart(startScreenPosition);
            }

            if (IsDrawingSkillActive)
            {
                attack?.CancelPendingAttack();
                StopMovementForDrawing();
            }

            return true;
        }

        EnsureDrawingCursorOverride();
        Vector2 screenPosition = ReadPointerScreenPosition();
        bool pointerOverPaper = IsPointerOverDrawingPaper(screenPosition);
        bool penPressed = WasPressedThisFrame(attackAction);
        bool penHeld = attackAction != null && attackAction.IsPressed();
        UpdateDrawingPenCursor(pointerOverPaper);

        if (WasPressedThisFrame(drawingCancelAction))
        {
            CancelDrawingSkill();
        }
        else if (togglePressed || WasPressedThisFrame(drawingSubmitAction))
        {
            RequestDrawingSkillSubmit(screenPosition);
        }
        else
        {
            if (drawingPenInputMode == DrawingPenInputMode.Toggle)
            {
                UpdateToggleDrawingPen(penPressed, pointerOverPaper, screenPosition);
            }
            else
            {
                if (penPressed && pointerOverPaper && !wasDrawingPenHeld)
                {
                    drawingSkill.BeginNewStroke();
                }

                if (penHeld && pointerOverPaper)
                {
                    RequestDrawingSkillContinue(screenPosition);
                }
                else if (penHeld && wasDrawingPenHeld)
                {
                    drawingSkill.BeginNewStroke();
                }

                wasDrawingPenHeld = penHeld && pointerOverPaper;
            }
        }

        StopMovementForDrawing();
        return true;
    }

    private void UpdateToggleDrawingPen(
        bool penPressed,
        bool pointerOverPaper,
        Vector2 screenPosition)
    {
        if (penPressed && pointerOverPaper)
        {
            toggledDrawingPenActive = !toggledDrawingPenActive;

            if (toggledDrawingPenActive)
            {
                drawingSkill.BeginNewStroke();
            }
        }

        if (toggledDrawingPenActive && pointerOverPaper)
        {
            if (!wasDrawingPenHeld)
            {
                drawingSkill.BeginNewStroke();
            }

            RequestDrawingSkillContinue(screenPosition);
        }
        else if (toggledDrawingPenActive && wasDrawingPenHeld)
        {
            drawingSkill.BeginNewStroke();
        }

        wasDrawingPenHeld = toggledDrawingPenActive && pointerOverPaper;
    }

    private void StopMovementForDrawing()
    {
        if (movement == null)
        {
            return;
        }

        movement.SetMoveInput(Vector2.zero);
        movement.SetJumpHeld(false);
        movement.ReleaseJump();
        movement.ClearJumpRequest();
        movement.StopHorizontalMovement();
    }

    private void ClearInput(DrawingCancelReason reason = DrawingCancelReason.StateChanged)
    {
        wasDrawingPenHeld = false;
        toggledDrawingPenActive = false;
        CancelDrawingSkill(reason);
        EndDrawingCursorOverride();
        attack?.CancelPendingAttack();

        if (movement == null)
        {
            return;
        }

        movement.SetMoveInput(Vector2.zero);
        movement.SetJumpHeld(false);
        movement.ClearJumpRequest();
        movement.ReleaseJump();
        movement.StopHorizontalMovement();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        bool wasApplicationFocused = applicationHasFocus;
        applicationHasFocus = hasFocus;

        if (!hasFocus && wasApplicationFocused)
        {
            HandleExternalInterruption(DrawingCancelReason.FocusLost);
        }
        else if (!applicationPaused)
        {
            externalInterruptionHandled = false;
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        applicationPaused = pauseStatus;

        if (pauseStatus)
        {
            HandleExternalInterruption(DrawingCancelReason.FocusLost);
        }
        else if (applicationHasFocus)
        {
            externalInterruptionHandled = false;
        }
    }

    private void HandleExternalInterruption(DrawingCancelReason reason)
    {
        GameStateManager manager = GameStateManager.Instance;
        bool canInterruptActivePlay = IsDrawingSkillActive
            || (manager != null && manager.IsState(GameState.Playing));

        if (!Application.isPlaying || externalInterruptionHandled || !canInterruptActivePlay)
        {
            return;
        }

        externalInterruptionHandled = true;
        ClearInput(reason);

        if (manager != null && manager.IsState(GameState.Playing))
        {
            manager.TogglePause();
        }
    }

    private void SubscribeToPlayerInputEvents()
    {
        ResolveReferences();

        if (playerInput == null)
        {
            return;
        }

        playerInput.onDeviceLost -= HandleInputDeviceLost;
        playerInput.onDeviceLost += HandleInputDeviceLost;
    }

    private void UnsubscribeFromPlayerInputEvents()
    {
        if (playerInput != null)
        {
            playerInput.onDeviceLost -= HandleInputDeviceLost;
        }
    }

    private void HandleInputDeviceLost(PlayerInput input)
    {
        externalInterruptionHandled = false;
        HandleExternalInterruption(DrawingCancelReason.DeviceLost);
    }

    private void SubscribeToCombatState()
    {
        if (combatState == null)
        {
            return;
        }

        combatState.StateChanged -= HandleCombatStateChanged;
        combatState.StateChanged += HandleCombatStateChanged;
    }

    private void UnsubscribeFromCombatState()
    {
        if (combatState != null)
        {
            combatState.StateChanged -= HandleCombatStateChanged;
        }
    }

    private void HandleCombatStateChanged(
        PlayerCombatState previousState,
        PlayerCombatState currentState)
    {
        if (previousState == currentState)
        {
            return;
        }

        ClearInput(DrawingCancelReason.StateChanged);
        interactor?.ForceRefreshTarget();
    }

    private void ResolveReferences()
    {
        movement = ResolveComponent(movement);
        attack = ResolveComponent(attack);
        interactor = ResolveComponent(interactor);
        drawingSkill = ResolveComponent(drawingSkill);
        drawingOverlay = ResolveComponent(drawingOverlay);
        playerInput = ResolveComponent(playerInput);
        health = ResolveComponent(health);
        combatState = ResolveComponent(combatState);

        if (combatState == null)
        {
            combatState = gameObject.AddComponent<PlayerCombatStateController>();
        }
    }

    private T ResolveComponent<T>(T component) where T : Component
    {
        return component != null ? component : GetComponent<T>();
    }

    private void ResolveInputActions()
    {
        if (playerInput == null)
        {
            playerInput = GetComponent<PlayerInput>();
        }

        InputActionMap nextMap = playerInput != null ? playerInput.currentActionMap : null;

        if (nextMap == null && playerInput != null && playerInput.actions != null)
        {
            nextMap = playerInput.actions.FindActionMap(PlayerActionMapName, false);
        }

        if (cachedActionMap == nextMap)
        {
            return;
        }

        cachedActionMap = nextMap;
        moveAction = FindAction("Move");
        attackAction = FindAction("Attack");
        interactAction = FindAction("Interact");
        jumpAction = FindAction("Jump");
        _dashAction = FindAction("Sprint");
        drawingSkillAction = FindAction("DrawingSkill");
        drawingSubmitAction = FindAction("DrawingSubmit");
        drawingCancelAction = FindAction("DrawingCancel");
        pauseAction = FindAction("Pause");
        pointerPositionAction = FindAction("PointerPosition");
    }

    private InputAction FindAction(string actionName)
    {
        return cachedActionMap != null ? cachedActionMap.FindAction(actionName, false) : null;
    }

    private static bool WasPressedThisFrame(InputAction action)
    {
        return action != null && action.enabled && action.WasPressedThisFrame();
    }

    private static bool WasReleasedThisFrame(InputAction action)
    {
        return action != null && action.enabled && action.WasReleasedThisFrame();
    }

    private Vector2 ReadPointerScreenPosition()
    {
        if (pointerPositionAction != null && pointerPositionAction.enabled)
        {
            return pointerPositionAction.ReadValue<Vector2>();
        }

        if (drawingSkill != null && drawingSkill.IsDrawing)
        {
            return drawingSkill.CurrentScreenPosition;
        }

        return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

    private void SubscribeToDialogueManager()
    {
        DialogueManager manager = DialogueManager.Instance;

        if (subscribedDialogueManager == manager)
        {
            return;
        }

        UnsubscribeFromDialogueManager();
        subscribedDialogueManager = manager;

        if (subscribedDialogueManager == null)
        {
            isDialogueInputBlocked = false;
            return;
        }

        subscribedDialogueManager.OnDialogueStarted += HandleDialogueStarted;
        subscribedDialogueManager.OnDialogueEnded += HandleDialogueEnded;
        isDialogueInputBlocked = subscribedDialogueManager.IsDialogueActive;

        if (isDialogueInputBlocked)
        {
            ClearInput();
        }
    }

    private void UnsubscribeFromDialogueManager()
    {
        if (subscribedDialogueManager == null)
        {
            return;
        }

        subscribedDialogueManager.OnDialogueStarted -= HandleDialogueStarted;
        subscribedDialogueManager.OnDialogueEnded -= HandleDialogueEnded;
        subscribedDialogueManager = null;
    }

    private void HandleDialogueStarted()
    {
        isDialogueInputBlocked = true;
        ClearInput();
    }

    private void HandleDialogueEnded()
    {
        isDialogueInputBlocked = false;
        ClearInput();
    }

    private void SubscribeToCutsceneManager()
    {
        CutsceneManager manager = CutsceneManager.Instance;

        if (subscribedCutsceneManager == manager)
        {
            return;
        }

        UnsubscribeFromCutsceneManager();
        subscribedCutsceneManager = manager;

        if (subscribedCutsceneManager == null)
        {
            isCutsceneInputBlocked = false;
            return;
        }

        subscribedCutsceneManager.OnCutsceneStarted += HandleCutsceneStarted;
        subscribedCutsceneManager.OnCutsceneFinished += HandleCutsceneFinished;
        isCutsceneInputBlocked = subscribedCutsceneManager.IsCutsceneActive;

        if (isCutsceneInputBlocked)
        {
            ClearInput();
        }
    }

    private void UnsubscribeFromCutsceneManager()
    {
        if (subscribedCutsceneManager == null)
        {
            return;
        }

        subscribedCutsceneManager.OnCutsceneStarted -= HandleCutsceneStarted;
        subscribedCutsceneManager.OnCutsceneFinished -= HandleCutsceneFinished;
        subscribedCutsceneManager = null;
    }

    private void HandleCutsceneStarted(string cutsceneId)
    {
        isCutsceneInputBlocked = true;
        ClearInput();
    }

    private void HandleCutsceneFinished(string cutsceneId)
    {
        isCutsceneInputBlocked = subscribedCutsceneManager != null && subscribedCutsceneManager.IsCutsceneActive;
        ClearInput();
    }

    private void BeginDrawingCursorOverride()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!ownsDrawingCursorState)
        {
            previousCursorLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            ownsDrawingCursorState = true;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void EnsureDrawingCursorOverride()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!ownsDrawingCursorState)
        {
            BeginDrawingCursorOverride();
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void FinishDrawingInput()
    {
        wasDrawingPenHeld = false;
        toggledDrawingPenActive = false;
        EndDrawingCursorOverride();
    }

    private void EndDrawingCursorOverride()
    {
        ClearDrawingPenCursor();

        if (!Application.isPlaying || !ownsDrawingCursorState)
        {
            return;
        }

        Cursor.lockState = previousCursorLockState;
        Cursor.visible = previousCursorVisible;
        ownsDrawingCursorState = false;
    }

    private bool IsPointerOverDrawingPaper(Vector2 screenPosition)
    {
        return drawingOverlay == null || drawingOverlay.IsPointerOverPaper(screenPosition);
    }

    private void UpdateDrawingPenCursor(bool pointerOverPaper)
    {
        if (!Application.isPlaying || !useDrawingPenCursor || !pointerOverPaper || drawingPenCursorTexture == null)
        {
            ClearDrawingPenCursor();
            return;
        }

        Cursor.SetCursor(drawingPenCursorTexture, GetClampedDrawingPenHotspot(), CursorMode.Auto);
        isUsingDrawingPenCursor = true;
    }

    private void ClearDrawingPenCursor()
    {
        if (!isUsingDrawingPenCursor)
        {
            return;
        }

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        isUsingDrawingPenCursor = false;
    }

    private Vector2 GetClampedDrawingPenHotspot()
    {
        return drawingPenCursorTexture == null
            ? drawingPenCursorHotspot
            : new Vector2(
                Mathf.Clamp(drawingPenCursorHotspot.x, 0f, drawingPenCursorTexture.width - 1f),
                Mathf.Clamp(drawingPenCursorHotspot.y, 0f, drawingPenCursorTexture.height - 1f));
    }

    private void Reset()
    {
        movement = GetComponent<PlayerMovement>();
        attack = GetComponent<PlayerAttack>();
        interactor = GetComponent<PlayerInteractor>();
        drawingSkill = GetComponent<PlayerDrawingSkill>();
        drawingOverlay = GetComponent<PlayerDrawingOverlay>();
        playerInput = GetComponent<PlayerInput>();
    }
}
