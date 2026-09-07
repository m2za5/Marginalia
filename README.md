# Marginalia
![](https://cdn.jsdelivr.net/gh/m2za5/Marginalia@main/Image.png)

만년필로 길을 만들고 적을 물리치며 뒤틀린 동화 세계를 정화하는 **2.5D 액션 어드벤처**
Unity · C# · 개발 4인 / 아티스트 2인 · 2026.03 ~ 진행 중

---

## 게임 소개

- 만년필로 그린 선이 발판·공격이 되는 드로잉 액션
- 뒤틀린 동화 세계를 구역 단위로 정화
- NPC 대화 · 퀘스트 · 상점으로 진행 분기

| | |
|---|---|
| **엔진** | Unity (URP) |
| **언어** | C# |
| **플랫폼** | Windows PC |
| **팀** | 개발 4인 / 아티스트 2인 |
| **협업** | Azure DevOps |
| **담당** | 클라이언트 — 대화 · NPC · 상점 |

---

## 담당 범위

개발 4인 팀이므로 **아래 항목만 본인 작업**. 전투 · 드로잉 · 스테이지는 팀원 담당.

| 영역 | 핵심 |
|---|---|
| [대화 재생](#1-대화-재생-시스템) | 상태 기계 기반 입력 분기 · 프레임 보정 타이핑 |
| [선택지 · 점프 분기](#2-lineid-기반-분기) | `lineId` 우선 참조로 대사 순서 변경에 안전 |
| [에디터 검증](#3-데이터-오류를-런타임-전에-차단) | `OnValidate`로 잘못된 참조를 임포트 시점에 차단 |
| [대화 연출 · 종료 연동](#4-대화-종료-이벤트-연동) | 종료된 대화를 식별해 후속 처리 분기 |
| [NPC 행동 · 상호작용](#5-npc-행동상호작용) | 가중치 랜덤 행동 · 상태 기반 상호작용 게이트 |
| [상점](#6-상점) | 구매 판정 일원화 · 대화 선택지 연동 진입 |

---

## 1. 대화 재생 시스템

`DialogueManager` · `DialogueView`

### 상태 기계로 입력을 분기

**문제** — 같은 입력키가 상황마다 다른 동작이어야 함. 출력 중이면 문장 완성, 출력 후면 다음 대사, 선택지 대기 중이면 무시.

**조치** — 4개 상태로 분리하고 `Update`에서 상태별 처리

```csharp
private enum State { Inactive, Typing, WaitingForContinue, WaitingForChoice }
```

- `Typing` 중 입력 → `CompleteLine()` — 전체 문장 즉시 표시
- `WaitingForContinue` 중 입력 → `Advance()` — 다음 대사
- `WaitingForChoice` → 입력 무시, 버튼 클릭만 수용

### 같은 입력이 두 번 먹는 문제

**문제** — 문장 완성 입력이 같은 프레임에 "다음 대사"로도 처리되어 한 줄이 건너뛰어짐

**조치** — 상태 전환 시 입력 잠금 프레임 기록

```csharp
_inputUnlockFrame = Time.frameCount + 1;
...
if (Time.frameCount < _inputUnlockFrame) return;
```

선택지 선택 직후 · 타이핑 완료 직후 · 대화 시작 직후 3곳에 적용.

### 코루틴 대신 누적 시간 방식

**문제** — 코루틴 `WaitForSeconds` 방식은 프레임 드랍 시 타이핑이 밀림

**조치** — `Update`에서 누적 시간을 글자 수로 환산

```csharp
_typeAccumulator += Time.unscaledDeltaTime;
int step = (int)(_typeAccumulator / typeSpeed);
if (step > 0)
{
    _typeAccumulator -= step * typeSpeed;
    _visibleChars = Mathf.Min(_visibleChars + step, _totalChars);
    dialogueText.maxVisibleCharacters = _visibleChars;
}
```

- 한 프레임에 여러 글자를 넘겨 **프레임 드랍을 보정**
- `unscaledDeltaTime` 사용 → `timeScale = 0` 상태에서도 타이핑 진행
- `maxVisibleCharacters` 조작 방식 → 문자열 재할당 없음

### UI 리빌드 비용 회피

`SetActive` 대신 `Canvas.enabled` 를 우선 사용. 계층 활성/비활성은 레이아웃 리빌드를 유발하지만 Canvas 비활성화는 그리기만 멈춤.

```csharp
private void SetRootVisible(bool visible)
{
    if (dialogueCanvas != null)
    {
        if (dialogueCanvas.enabled != visible) dialogueCanvas.enabled = visible;
        return;
    }
    if (dialoguePanel != null && dialoguePanel.activeSelf != visible)
        dialoguePanel.SetActive(visible);
}
```

Canvas 미할당 시 GameObject 방식으로 폴백. 대화 루트 · 선택지 패널 양쪽에 동일 적용.

### 선택지 버튼 풀링

`DialogueView`에서 버튼을 파괴하지 않고 재사용. 선택지 수가 대사마다 달라도 최대치만큼만 생성됨.

```csharp
private Button GetOrCreateChoiceButton(int index)
{
    while (_spawnedChoices.Count <= index)
    {
        Button btn = Instantiate(choiceButtonPrefab, choicesContainer);
        btn.gameObject.SetActive(false);
        _spawnedChoices.Add(btn);
    }
    return _spawnedChoices[index];
}
```

### 대화 전 게임 상태 저장 · 복원

- 대화 시작 시 `GameStateManager`의 현재 상태 저장 후 `Dialogue`로 전환
- **이미 `Dialogue` 상태였으면 복원 대상에서 제외** — 중첩 대화에서 상태가 꼬이는 것 방지
- 복원은 **여전히 `Dialogue` 상태일 때만** 수행 — 대화 도중 다른 시스템이 상태를 바꿨으면 그 결정을 존중

```csharp
_shouldRestorePreviousGameState = _previousGameState != GameState.Dialogue;
...
if (GameStateManager.Instance.IsState(GameState.Dialogue))
    GameStateManager.Instance.ChangeState(_previousGameState);
```

### 초상화 보정

`PortraitCalibration` (ScriptableObject)

**문제** — 초상화마다 원본 크기와 여백이 달라 같은 UI 슬롯에서 제각각으로 보임

**조치** — 스프라이트별 `scale` · `offset` 을 데이터로 분리. 아트 리소스를 수정하지 않고 배치 보정

- 첫 조회 시 `Dictionary<Sprite, Entry>` 캐시 구성
- 스프라이트가 바뀔 때만 보정값 적용 (`portraitImage.sprite != sprite`)
- `sqrMagnitude` 비교로 동일 스케일 재대입 회피

---

## 2. lineId 기반 분기

**문제** — 선택지와 점프를 배열 인덱스로 가리키면, 대사를 중간에 하나 추가하는 순간 이후 연결이 전부 어긋남

**조치** — 각 대사에 고유 `lineId` 를 부여하고 **ID 우선 · 인덱스 폴백**

```csharp
private int ResolveIndex(string id, int fallbackIndex, string context)
{
    if (string.IsNullOrEmpty(id)) return fallbackIndex;
    if (_hasIdMap && _idToIndex.TryGetValue(id, out int resolved)) return resolved;

    Debug.LogError($"[DialogueManager] {context}: targetLineId '{id}' 해석 실패. 대화를 종료한다.");
    return -1;
}
```

- 대화 시작 시 `BuildIdMap()` 으로 `lineId → index` 사전 구성 — 매 분기마다 순회하지 않음
- ID 미지정이면 기존 인덱스 사용 → **기존 데이터와 호환**
- 해석 실패 시 `-1` 반환 → 대화 종료. **잘못된 참조로 무한 루프에 빠지지 않음**
- 중복 `lineId` 발견 시 에러 로그 후 앞의 것 유지

선택지 · 점프 양쪽이 같은 `ResolveIndex` 를 사용.

---

## 3. 데이터 오류를 런타임 전에 차단

`Dialogue.OnValidate` · `ShopNPC.OnValidate` · `NPCInteractable.OnValidate`

**문제** — 대화 데이터는 기획 단계에서 자주 수정됨. 잘못된 참조는 **해당 대사에 도달해야만** 드러나서 발견이 늦음

**조치** — 에셋 편집 시점에 검증

| 대상 | 검사 |
|---|---|
| `Dialogue` | `lineId` 중복 · `jumpTargetIndex` 범위 · `targetLineId`가 실제 존재하는지 |
| `ShopNPC` | 트리거 `lineId` 가 해당 대화에 있는지 · `choiceIndex` 가 선택지 개수 내인지 |
| `NPCInteractable` | Collider가 `isTrigger` 인지 · 2D Collider가 붙어 있지 않은지 |
| `NPCIdleBehaviorData` | `pauseMax < pauseMin` 자동 보정 · `Animate` 인데 `stateName` 비어 있는지 |

에디터에서 즉시 빨간 로그가 뜨므로 **플레이 없이 데이터 오류를 잡음.**

---

## 4. 대화 종료 이벤트 연동

`DialogueEndHandler` · `DialogueStarter` · `DialogueSceneTransition` · `DialogueBlurController`

**문제** — `OnDialogueEnded` 는 파라미터가 없음. 여러 NPC가 같은 이벤트를 구독하면 **어느 대화가 끝났는지 구분 불가**

**조치** — `DialogueManager` 가 `LastFinishedDialogue` 를 보관. 구독자가 자기 대화인지 판별

```csharp
private void HandleEnd()
{
    DialogueManager manager = DialogueManager.Instance;
    if (manager == null || manager.LastFinishedDialogue != dialogue) return;
    ...
}
```

### 실행 순서 의존 제거

`OnEnable` 시점에 `DialogueManager.Instance` 가 아직 없을 수 있음 → `Start` 에서 재시도하고, 그래도 실패하면 경고

```csharp
private void OnEnable() => TrySubscribe();

private void Start()
{
    if (!_subscribed && !TrySubscribe())
        Debug.LogWarning($"[DialogueEndHandler] {name}: DialogueManager.Instance 없음.", this);
}
```

`OnDisable` · `OnDestroy` 양쪽에서 구독 해제.

### 종료 후 처리

| 컴포넌트 | 동작 |
|---|---|
| `DialogueStarter` | 종료 트리거가 있으면 애니메이터 트리거 발동 + NPC 행동 영구 정지, 없으면 배회 재개 |
| `DialogueSceneTransition` | 현재 스테이지 위치 저장 후 지정 씬 로드 · `transitionRequested` 로 중복 호출 차단 |
| `DialogueBlurController` | 시작/종료에 맞춰 Volume weight 페이드 · 진행 중 코루틴은 중단 후 재시작 |
| `DialogueEndHandler` | `fireOnce` 로 1회만 실행 · `ResetFired()` 로 수동 재활성화 |

블러는 `Time.unscaledDeltaTime` 사용 → 일시정지 중에도 전환 진행.

---

## 5. NPC 행동 · 상호작용

### 가중치 기반 랜덤 행동

`NPCIdleBehavior` · `NPCIdleBehaviorData`

**문제** — 같은 스크립트를 쓰면서 NPC마다 성향이 달라야 함. 어떤 NPC는 자주 걷고, 어떤 NPC는 대부분 서 있음

**조치** — 행동 종류 · 지속시간 · 가중치 · 배회 범위를 `ScriptableObject` 로 분리. 누적 가중치로 추첨

```csharp
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
```

**최적화 처리**

- `Animator.StringToHash` 결과를 `Awake` 에서 캐시 — 매 전환마다 문자열 해싱하지 않음
- 총 가중치도 `Awake` 에서 1회 계산
- 이동 판정은 `sqrMagnitude` 비교로 `Sqrt` 회피
- `stepLength > dist` 클램프로 목표 지점 오버슈트 방지
- `FlipDeadzone` 으로 미세 이동 시 스프라이트 좌우 뒤집힘 깜빡임 차단

**설정 검증 실패 시 `enabled = false`** — 잘못된 데이터로 매 프레임 에러를 뿜지 않고 즉시 정지

### 3단계 정지 제어

| 메서드 | 용도 |
|---|---|
| `Pause()` | 대화 시작 시 정지 |
| `Resume()` | 대화 종료 후 재개 · `_stopped` 면 무시 |
| `StopPermanently()` | 퀘스트 수락 등으로 영구 정지 |

`Resume()` 이 `_stopped` 를 확인하므로 **영구 정지 후 재개 요청이 와도 되살아나지 않음.**

### 상호작용 게이트

`NPCInteractable`

**문제** — 대화 중이거나 일시정지 상태에서 상호작용 프롬프트가 뜨고 입력도 먹힘

**조치** — 프롬프트 표시 조건과 입력 허용 조건에 **같은 함수**를 사용

```csharp
private bool CanInteractNow()
{
    GameStateManager manager = GameStateManager.Instance;
    if (manager != null) return manager.IsState(GameState.Playing);

    DialogueManager dialogue = DialogueManager.Instance;
    return dialogue == null || !dialogue.IsDialogueActive;
}
```

- `GameStateManager` 없는 씬에서는 `DialogueManager` 로 폴백
- 표시와 입력이 같은 판정을 쓰므로 **"프롬프트는 떠 있는데 눌리지 않는" 상태가 발생하지 않음**
- 퀘스트 제안이 있으면 수락 시도 → 실패 시 일반 상호작용으로 폴백

```csharp
if (!TryAcceptQuest()) onInteract.Invoke();
```

---

## 6. 상점

`ShopController` · `ShopUIView` · `ShopNPC` · `ShopInventory`

### 구매 판정 일원화

**문제** — 구매 버튼 활성화 조건과 실제 구매 처리 조건이 따로 구현되면 어긋남. 버튼은 눌리는데 구매가 실패하거나 그 반대

**조치** — 판정을 `Evaluate` 하나로 모으고 **버튼 상태와 실제 구매가 같은 함수를 호출**

```csharp
public ShopPurchaseResult Evaluate(ShopItemEntry entry)
{
    if (entry == null || !entry.IsConfigured)          return ShopPurchaseResult.InvalidEntry;
    if (purchaseService == null)                       return ShopPurchaseResult.ServiceUnavailable;
    if (GetRemainingStock(entry) == 0)                 return ShopPurchaseResult.OutOfStock;
    if (!purchaseService.CanAfford(entry.Price))       return ShopPurchaseResult.NotEnoughCurrency;

    if (entry.Kind == ShopItemKind.InventoryItem)
    {
        ...
        if (inventory.GetQuantity(definition) + entry.Quantity > definition.MaxStack)
            return ShopPurchaseResult.InventoryFull;
    }
    return ShopPurchaseResult.Success;
}
```

```csharp
// 구매 버튼 활성화
view.BuyButton.interactable =
    selectedEntry != null && Evaluate(selectedEntry) == ShopPurchaseResult.Success;

// 실제 구매 요청
ShopPurchaseResult result = Evaluate(entry);
if (result != ShopPurchaseResult.Success) { ShowResultMessage(result); return result; }
```

- 결과가 `bool` 이 아니라 **enum** 이라 실패 원인별 안내 메시지를 그대로 매핑
- 재고 · 재화 · 인벤토리 여유를 **순서대로** 판정 → 사용자에게 가장 먼저 걸린 원인을 알림

### 한정 재고

- `remainingStock` 은 판매 발생 시점에만 기록 — 미판매 상품은 항목 없음
- 조회 시 없으면 `InitialStock` 반환
- 무제한 상품은 `-1` 반환으로 구분
- `resetStockOnClose` 로 상점 닫을 때 초기화 여부 선택

### 열기 · 닫기 상태 관리

**Open**

- `GameState.Playing` 일 때만 허용
- 인벤토리 UI가 열려 있으면 먼저 닫음
- 플레이어 드로잉 스킬 취소 + 입력 차단
- `GameStateManager` 있으면 `Paused` 로, 없으면 `timeScale = 0` 폴백

**Close**

- 상태 복원은 **`Paused` 상태이고 이전이 `Playing` 이었을 때만** — 상점을 여는 사이 다른 시스템이 상태를 바꿨으면 덮어쓰지 않음

**외부 상태 변경 대응**

```csharp
private void HandleGameStateChanged(GameState previousState, GameState currentState)
{
    if (IsOpen && currentState != GameState.Paused) Close(false);
}
```

상점이 열린 채로 게임 상태가 바뀌면 자동으로 닫힘. **UI와 게임 상태의 불일치를 구조적으로 차단.**

`EnsureEventSystem()` — 씬에 `EventSystem` 이 없으면 생성. 상점 UI 입력이 씬 구성에 의존하지 않음.

### 대화 선택지 → 상점 진입

`ShopNPC`

**문제** — 대화 UI가 열린 상태에서 상점을 열면 두 UI가 겹침

**조치** — 선택 시점에 **예약**만 하고 대화 종료 후 실행

```csharp
private void HandleChoiceSelected(Dialogue dialogue, int fromLineIndex, int choiceIndex)
{
    if (greetingDialogue == null || dialogue != greetingDialogue) return;

    string lineId = ResolveLineId(dialogue, fromLineIndex);
    foreach (ShopOpenTrigger trigger in openTriggers)
        if (trigger != null && trigger.Matches(lineId, choiceIndex))
        {
            pendingOpen = true;
            return;
        }
}

private void HandleDialogueEnded()
{
    if (manager.LastFinishedDialogue != greetingDialogue) return;
    bool shouldOpen = pendingOpen || (openWhenDialogueEnds && 트리거 없음);
    pendingOpen = false;
    if (shouldOpen) OpenShop();
}
```

- 진입 조건을 `lineId + choiceIndex` 로 지정 → **대사 순서가 바뀌어도 유지**
- 트리거가 비어 있으면 `openWhenDialogueEnds` 로 무조건 진입도 가능
- `OnValidate` 로 트리거가 가리키는 대사·선택지가 실제 존재하는지 에디터에서 검증

---

## 코드 맵

| 파일 | 역할 | 설계 결정 |
|---|---|---|
| `DialogueManager` | 대화 재생 · 타이핑 · 선택지 · 상태 전환 | 4상태 기계 · 입력 잠금 프레임 · `Canvas.enabled` |
| `Dialogue` | 대사 · 선택지 · 점프 데이터 (`ScriptableObject`) | `OnValidate` 로 ID 중복 · 참조 오류 사전 검증 |
| `DialogueView` | 선택지 버튼 생성 · 정리 | 파괴 대신 비활성화 재사용 |
| `PortraitCalibration` | 스프라이트별 초상화 보정 (`ScriptableObject`) | 아트 수정 없이 데이터로 배치 보정 |
| `DialogueStarter` | 대화 시작 · 종료 후 NPC 상태 전환 | `LastFinishedDialogue` 로 자기 대화 식별 |
| `DialogueEndHandler` | 지정 대화 종료 시 이벤트 발행 | `fireOnce` · `OnEnable` 실패 시 `Start` 재시도 |
| `DialogueSceneTransition` | 대화 종료 후 씬 전환 | 진행 위치 저장 · 중복 요청 차단 |
| `DialogueBlurController` | 대화 시작 · 종료 블러 페이드 | 중복 코루틴 중단 · `unscaledDeltaTime` |
| `NPCIdleBehavior` | 가중치 랜덤 배회 · 대기 | 해시 캐시 · `sqrMagnitude` · 3단계 정지 제어 |
| `NPCIdleBehaviorData` | NPC별 행동 성향 (`ScriptableObject`) | `OnValidate` 로 잘못된 범위 자동 보정 |
| `NPCInteractable` | 근접 상호작용 · 퀘스트 수락 | 표시 조건과 입력 조건에 동일 함수 사용 |
| `ShopController` | 상점 열기 · 닫기 · 구매 판정 · 재고 | `Evaluate` 단일 판정 · 상태 불일치 자동 해소 |
| `ShopUIView` | 상점 UI 참조 · 목록/상세 전환 | 로직과 뷰 분리 |
| `ShopNPC` | 대화 선택지 기반 상점 진입 | 선택 시 예약 · 대화 종료 후 실행 |
| `ShopDebugOpener` | 개발용 단축키 상점 열기 | 디버그 전용 |
