# Turn Motion Composer

`Tools > CrossBlade > Turn Motion Composer`는 원본 애니메이션의 몇 개 시점을 골라 각 구간을 동일한 길이로 재생하고, 전체를 한 턴 안에 배치하는 편집 도구다.

## 사용 순서

1. `Source Animation`에 원본 `AnimationClip`을 지정한다.
2. 해당 클립과 동일한 리그를 가진 프리팹을 `Character Prefab`에 지정한다.
3. 타임라인을 재생하거나 scrub하고, 구간 경계로 쓸 순간마다 `Add Point`를 누른다.
4. `Turn Duration`을 지정한다. 기본값은 0.5초다.
5. 필요하면 `Default Part VFX`에 검격 VFX 프리팹을 지정한다.
6. `Generate Clip + Definition + Action/Part Prefabs`를 누른다.
7. `Preview generated one-turn clip`으로 결과 애니메이션과 Part VFX를 함께 재생하거나 scrub한다.

선택한 점이 N개면 N-1개 구간이 만들어진다. 각 구간의 목표 길이는 `Turn Duration / (N-1)`이고, 원본 구간 길이에 따라 배속이 자동 결정된다. 점은 원본 시간 순으로 정렬되며 현재 버전은 정방향 재생을 사용한다.

## 생성 결과

기본 출력 폴더는 `Assets/Generated/TurnMotions`다.

- `<Action>.anim`: 구간별로 time-warp된 한 턴 길이의 애니메이션
- `<Action>.asset`: 원본 시점, 구간, 배속, 생성 결과를 보관하는 정의
- `<Action>.prefab`: 기존 전투 시스템에서 바로 `Move`로 사용할 액션 프리팹
- `<Action>_Parts/<Action>_Part_XX.prefab`: 구간별 VFX 편집 프리팹

액션 루트에는 `TurnMotionMove : Move`와 `TurnMotionAction`이 붙는다. 기존 `ActorVisualController`가 전투의 `moveProgress`로 이 액션을 샘플링하므로, 애니메이션과 VFX가 턴 진행도에 맞춰 결정적으로 재생된다.

## VFX 편집

생성 후 창의 `Open Part Prefab Folder`를 누른다. 원하는 Part 프리팹을 열고 `VFXAnchor` 아래에 검격 VFX를 nested prefab으로 추가하거나 위치·회전·크기를 조정한다. 같은 이름으로 다시 생성해도 기존 Part 프리팹의 `VFXAnchor` 내용은 유지된다.

`Default Part VFX`를 지정하면 비어 있는 각 `VFXAnchor`에 최초 생성 시 자동으로 넣는다. 인접 Part는 반개구간으로 평가되어 경계 시점에 두 VFX가 동시에 활성화되지 않는다.

## 기존 전투에 연결

생성된 `<Action>.prefab` 자체가 `Move` 하위 타입이다. 기존 Move 프리팹을 등록하던 곳에 동일하게 넣으면 된다. 프리팹의 `TurnMotionMove`에서 ID와 액션 연결을 확인하고, 필요하면 각 Part 프리팹에서 VFX만 독립적으로 다듬는다.
