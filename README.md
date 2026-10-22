# Marginalia

![Marginalia](https://cdn.jsdelivr.net/gh/m2za5/Marginalia@main/Image.png)

만년필로 길을 만들고 적을 물리치며 뒤틀린 동화 세계를 정화하는 **2.5D 액션 어드벤처**

Unity · C# · 개발 4인 / 아티스트 2인 · 2026.03 ~ 진행 중

[플레이 영상](https://youtu.be/GDm_sxGuMDo)

## 프로젝트 개요

| 항목 | 내용 |
|---|---|
| 엔진 | Unity (URP) |
| 언어 | C# |
| 플랫폼 | Windows PC |
| 팀 | 개발 4인 / 아티스트 2인 |
| 협업 | Azure DevOps |
| 주요 담당 | 드로잉 전투 스킬 · 상점 · 대화 · NPC 행동 및 상호작용 |

만년필 드로잉으로 발판을 만들거나 공격 스킬을 사용하고, NPC 대화와 퀘스트를 통해 동화 세계를 탐험하는 게임입니다.

이 저장소는 담당 기능의 주요 소스를 공개하는 포트폴리오용 저장소입니다. 전체 Unity 프로젝트와 실행에 필요한 에셋을 포함하지 않습니다.

## 주요 담당 업무

아래는 팀 프로젝트에서 본인이 구현·수정한 기능입니다. 각 기능은 플레이어·피해 처리·게임 상태·저장 등 프로젝트 공통 시스템과 연동합니다.

| 영역 | 주요 구현 |
|---|---|
| [드로잉 전투 스킬](#1-드로잉-전투-스킬) | 패턴 인식 결과와 시전 연동 · 스크린–월드 좌표 변환 · 경로형·범위형 공격 판정 · 드로잉 선을 이용한 적 투사체 제거 |
| [상점 시스템](#2-상점-시스템) | 구매 조건 검증 공통화 · 구매 성공 후 재고 처리 · 상점·상품 ID 기반 재고 저장 및 복원 |
| [대화 시스템](#3-대화-시스템) | 상태별 입력 처리 · 대사 ID 기반 분기 · 게임 상태 복원 후 종료 이벤트 전달 |
| [NPC 행동 및 상호작용](#4-npc-행동-및-상호작용) | ScriptableObject 기반 가중치 행동 선택 · 컷신 중 행동 제어 · 근접·게임 상태 검사 및 퀘스트 수락 연동 |

## 1. 드로잉 전투 스킬

### 시전 조건 검증과 공격 실행

**설계 목표**

드로잉 패턴 인식 결과를 공격 스킬 실행으로 연결하고, 잉크 부족·쿨타임·타겟팅 실패 시 시전을 제한합니다.

**구현 방식**

- 드로잉 완료 시 패턴 인식 결과에 따라 방향 지정 공격인 **Ink Slash** 또는 지점 지정 범위 공격인 **Ink Eruption**의 시전 함수로 분기합니다.
- 잉크·쿨타임과 스킬별 타겟팅 조건을 검증한 뒤 잉크를 차감하고 스킬을 실행합니다.
- 스킬별 쿨타임 종료 시각을 기록하고, 시전 결과에 따른 피드백을 표시합니다.

관련 코드: `PlayerAreaDrawingSkillCaster.cs`, `CombatDrawingSkillDefinition.cs`

### 스크린–월드 좌표 변환과 타겟팅

**설계 목표**

플레이어가 화면에서 그린 위치를 월드 공간의 공격 방향 또는 시전 지점으로 변환합니다.

**구현 방식**

- 드로잉 바운딩 박스의 중심을 조준 스크린 좌표로 사용합니다.
- **Slash**는 카메라 Raycast 충돌점을 우선 사용합니다. 충돌점이 없으면 시전 원점 높이의 수평면에 투영하고, 목표 지점을 향하는 수평 공격 방향을 계산합니다.
- **Eruption**은 지면 충돌점의 경사·장애물 가림·수평 사거리를 검증해 시전 위치를 확정합니다.

관련 코드: `PlayerAreaDrawingSkillCaster.cs`, `DrawingWorldProjector.cs`

### 경로형·범위형 공격 판정

**설계 목표**

공격 형태에 맞게 피해 대상을 검출하고, 하나의 대상에 여러 Collider가 있어도 중복으로 피해를 요청하지 않도록 처리합니다.

**구현 방식**

| 공격 | 충돌 검사 |
|---|---|
| Ink Slash | `SphereCastNonAlloc`으로 공격 경로를 검사하고, 시작 지점의 겹침은 `OverlapSphereNonAlloc`으로 보완 |
| Ink Eruption | 지정 지점에서 `OverlapSphereNonAlloc`으로 범위 내 대상 검출 |

검출된 Collider의 상위 객체에서 `IDamageable`을 조회하고, `HashSet<IDamageable>`로 대상별 피해 요청을 **시전당 1회**로 제한합니다. 피해 적용 결과에 따라 타격 피드백을 실행합니다.

관련 코드: `CombatDrawingInkSlash.cs`, `CombatDrawingInkEruption.cs`, `DamageImpactFeedback.cs`

### 드로잉 선을 이용한 적 투사체 제거

**문제점**

기존 방어 스킬은 선을 그은 위치와 관계없이 모든 방향의 피해를 차단해, 드로잉 위치가 방어 결과에 반영되지 않았습니다.

**개선 방식**

- 드로잉 좌표를 월드 고정점으로 보관하고, 카메라 이동에 맞춰 화면 좌표로 다시 투영합니다.
- 0.5초 동안 적 투사체의 이전·현재 위치 사이 이동 구간을 검사해, 드로잉 선에 닿거나 통과한 투사체만 제거합니다.
- `EnemyProjectile.Cancel()`로 투사체를 비활성화한 뒤 제거합니다.
- 첫 투사체 제거 성공 시 콜백을 한 번만 실행해 해당 스킬의 쿨타임을 초기화합니다.

관련 코드: `CombatDrawingCancellationLine.cs`, `PlayerAreaDrawingSkillCaster.cs`, `EnemyProjectile.cs`

## 2. 상점 시스템

### 구매 조건 검증과 구매 처리

**설계 목표**

구매 버튼의 활성화 조건과 실제 구매 요청의 검증 조건을 일치시키고, 구매 실패 시 재고가 차감되지 않도록 처리합니다.

**구현 방식**

- 재고·재화·아이템 최대 보유 수량 등을 검사하는 `Evaluate()`를 구매 버튼 갱신과 실제 구매 요청에서 함께 사용합니다.
- 상품 유형에 따라 아이템 구매·최대 체력 강화·최대 스킬 포인트 강화 처리를 구매 서비스에 요청합니다.
- 구매 서비스가 성공을 반환한 경우에만 재고를 차감하고 상품 목록·상세 UI를 갱신합니다.
- 실패 결과를 `ShopPurchaseResult`로 구분해 원인에 맞는 안내를 표시합니다.

관련 코드: `ShopController.cs`, `PlayerPurchaseService.cs`, `ShopInventory.cs`

### 상점·상품 ID 기반 재고 저장 및 복원

**설계 목표**

상품 목록의 순서가 바뀌어도 동일한 상품을 식별하고, 게임 저장·불러오기 이후에도 한정 상품의 잔여 재고를 유지합니다.

**구현 방식**

- **상점 ID와 상품 ID의 조합**으로 재고 데이터를 식별합니다.
- 구매 성공 시 잔여 재고를 차감하고 `SaveManager`의 저장 데이터를 갱신합니다.
- 재고 조회 시 저장된 값을 우선 사용하고, 저장 이력이 없으면 상품의 초기 재고를 적용합니다.
- 무제한 재고는 `-1`로 구분해 한정 재고 차감 대상에서 제외합니다.

재고 데이터 갱신과 파일 저장은 구분됩니다. 구매 시에는 저장 데이터를 갱신하고, 게임 저장·불러오기 흐름을 통해 파일에 기록하거나 복원합니다.

관련 코드: `ShopController.cs`, `ShopInventory.cs`, `SaveManager.cs`, `IngameSaveData.cs`

### 대화 선택지와 상점 진입 연결

**설계 목표**

상점 진입 선택지를 고른 뒤 대화 종료와 게임 상태 복원을 거쳐 상점을 엽니다.

**구현 방식**

- 대사 ID와 선택지 인덱스가 지정된 진입 조건과 일치하면 상점 열기를 예약합니다.
- 대화 종료 이벤트에서 `LastFinishedDialogue`가 해당 NPC의 대화인지 확인합니다.
- 예약된 진입 요청을 확인하고 예약 상태를 초기화한 뒤 상점을 엽니다.
- 상점은 상품 데이터의 아이콘·이름·설명·가격을 UI에 반영하고, 페이지 단위로 상품 목록을 표시합니다.

관련 코드: `ShopNPC.cs`, `ShopController.cs`, `ShopUIView.cs`

## 3. 대화 시스템

### 대화 상태에 따른 입력 처리

**설계 목표**

같은 진행 입력을 대사 출력·진행·선택지 대기 상태에 따라 구분해 처리합니다.

**구현 방식**

| 상태 | 입력 처리 |
|---|---|
| `Inactive` | 대화 진행 입력 처리 중단 |
| `Typing` | 진행 입력 시 현재 문장 전체 표시 |
| `WaitingForContinue` | 진행 입력 시 다음 대사로 이동 |
| `WaitingForChoice` | 일반 진행 입력을 차단하고 선택 결과로 분기 |

타이핑은 `Time.unscaledDeltaTime`의 누적 시간을 표시할 글자 수로 환산합니다. 일시정지 중에도 출력이 진행되며, 프레임이 길어진 경우 한 번에 여러 글자를 표시해 누적 시간에 맞춥니다.

관련 코드: `DialogueManager.cs`, `DialogueView.cs`

### 대사 ID 기반 분기

**문제점**

선택지·점프 대상을 배열 인덱스로만 지정하면 대사를 삽입하거나 순서를 바꿀 때 분기 연결도 함께 수정해야 합니다.

**개선 방식**

- 대화 시작 시 대사 ID와 인덱스를 `Dictionary`로 매핑합니다.
- 선택지·점프 대상은 대사 ID를 우선 조회하고, ID가 지정되지 않은 경우 기존 인덱스를 사용합니다.
- 지정된 ID를 찾지 못하면 오류를 기록하고 대화를 종료합니다.
- 에디터 검증으로 대사 ID 중복과 존재하지 않는 분기 대상 등을 확인합니다.

관련 코드: `DialogueManager.cs`, `Dialogue.cs`

### 게임 상태 복원과 종료 이벤트 연동

**설계 목표**

대화가 끝나면 이전 게임 상태를 복원하고, 종료된 대화에 맞는 후속 처리를 실행합니다.

**구현 방식**

- 대화 시작 시 이전 게임 상태를 보관한 뒤 대화 상태로 전환합니다.
- 대화 종료 시 UI와 내부 상태를 정리하고 `LastFinishedDialogue`에 종료된 대화를 기록합니다.
- 이전 게임 상태를 복원한 뒤 `OnDialogueEnded` 이벤트를 호출합니다.
- 구독한 시스템은 종료된 대화를 확인해 NPC 행동 재개·상점 진입·씬 전환 등 필요한 후속 처리를 실행합니다.

대화 중 다른 시스템이 게임 상태를 변경했다면, 종료 시 그 상태를 덮어쓰지 않도록 현재 상태가 여전히 대화 상태인 경우에만 복원합니다.

관련 코드: `DialogueManager.cs`, `DialogueStarter.cs`, `DialogueEndHandler.cs`, `DialogueSceneTransition.cs`, `ShopNPC.cs`

## 4. NPC 행동 및 상호작용

### 데이터 기반 가중치 행동 선택

**설계 목표**

공통 행동 로직을 사용하면서 NPC별로 행동 종류·지속시간·선택 확률·배회 범위를 설정합니다.

**구현 방식**

- `ScriptableObject`에 NPC 행동 데이터를 분리합니다.
- 각 행동에 설정된 가중치를 누적해 다음 행동을 선택합니다.
- 선택된 행동에 따라 애니메이션을 재생하거나 배회 목표 지점으로 이동합니다.

관련 코드: `NPCIdleBehavior.cs`, `NPCIdleBehaviorData.cs`

### 컷신 중 행동 중단 및 종료 후 재개

**설계 목표**

컷신 중 NPC의 배회와 빌보딩이 연출에 간섭하지 않도록 제어합니다.

**구현 방식**

- 컷신 실행 상태를 확인해 NPC의 배회와 빌보딩을 일시 중단합니다.
- 배회 로직은 타이머와 목표 지점을 보존하고, 컷신 종료 후 진행 중이던 행동을 이어갑니다.
- 대화에서 사용하는 `Pause()`·`Resume()`과 영구 정지 처리는 별도로 구분합니다.

관련 코드: `NPCIdleBehavior.cs`, `NPCFacePlayer.cs`, `StorySequenceRunner.cs`, `CutsceneManager.cs`

### 상호작용 및 퀘스트 수락 연동

**설계 목표**

플레이어의 근접 여부와 게임 상태에 따라 상호작용 안내와 입력을 제어합니다.

**구현 방식**

- 상호작용 안내 표시와 입력 처리에서 같은 게임 상태 검사 함수를 사용합니다.
- 퀘스트 제안이 있으면 퀘스트 시작을 요청하고, 수락 성공 시 설정에 따라 재수락을 제한하거나 저장을 요청합니다.
- 수락 후 이벤트로 후속 처리를 연결하고, 퀘스트 수락이 처리되지 않으면 일반 상호작용 이벤트를 실행합니다.

관련 코드: `NPCInteractable.cs`

## 주요 코드 맵

| 영역 | 파일 | 역할 |
|---|---|---|
| 스킬 시전 | `PlayerAreaDrawingSkillCaster.cs` | 인식 결과 분기·시전 조건 검증·쿨타임 관리 |
| 좌표 변환 | `DrawingWorldProjector.cs` | 지면 투영과 시전 지점 검증 |
| 공격 판정 | `CombatDrawingInkSlash.cs`, `CombatDrawingInkEruption.cs` | 경로형·범위형 대상 검출과 중복 피해 요청 방지 |
| 투사체 제거 | `CombatDrawingCancellationLine.cs`, `EnemyProjectile.cs` | 이동 구간 검사와 투사체 제거 |
| 타격 피드백 | `DamageImpactFeedback.cs` | 피해 적용 결과에 따른 타격 피드백 |
| 상점 구매 | `ShopController.cs`, `PlayerPurchaseService.cs` | 구매 조건 검증과 상품 유형별 구매 처리 |
| 상점 데이터·UI | `ShopInventory.cs`, `ShopUIView.cs` | 상품 정의와 목록·상세 UI |
| 재고 저장 | `SaveManager.cs`, `IngameSaveData.cs` | 상점별 상품 재고 데이터 저장 및 복원 |
| 대화·상점 연결 | `ShopNPC.cs` | 선택지에 따른 진입 예약과 대화 종료 후 상점 열기 |
| 대화 진행·분기 | `DialogueManager.cs`, `Dialogue.cs`, `DialogueView.cs` | 상태별 입력·대사 ID 조회·선택지 UI |
| 대화 종료 연동 | `DialogueStarter.cs`, `DialogueEndHandler.cs`, `DialogueSceneTransition.cs` | NPC 상태 전환과 종료 후 이벤트·씬 전환 |
| 대화 연출 | `DialogueBlurController.cs`, `PortraitCalibration.cs` | 블러 전환과 초상화별 배치 보정 |
| NPC 행동 | `NPCIdleBehavior.cs`, `NPCIdleBehaviorData.cs`, `NPCFacePlayer.cs` | 가중치 행동 선택과 컷신 중 행동·빌보딩 제어 |
| NPC 상호작용 | `NPCInteractable.cs` | 근접·게임 상태 검사와 퀘스트 수락 연동 |
