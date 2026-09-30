# Host ↔ 원본 프로세스 소유권 이양 설계

`WIDGET_COMPAT_DESIGN.md`(인프로세스 위젯)를 확장한다. 인프로세스 위젯은 그대로 쓰고, **Host 밖으로 꺼낼 때 원본 프로세스로 소유권을 넘긴다.**

## 1. 동작 정의

- **Host 안(도킹)**: 소유자는 Host 하나. 위젯은 Host 프로세스 안의 인프로세스 위젯이며 수집·상태·생명 주기가 Host에 종속된다(Host와 함께 켜지고 꺼진다).
- **밖으로 드롭(분리)**: 소유권이 **원본 프로세스**로 넘어간다. Host는 자기 인스턴스를 없애고, 원본 프로세스가 그 상태를 이어받아 드롭한 자리에 미니 위젯 창을 띄운다. 이후 생명 주기는 완전히 별도(Host를 꺼도 살아 있다).
- **다시 Host로 드롭**: 소유권이 Host로 돌아온다. 원본이 현재 상태를 넘기고 자기 창을 닫으면 Host가 인프로세스 위젯을 다시 만들어 도킹한다.
- **어느 순간에도 소유자는 하나.** 드롭하는 순간에만 넘어가며, 동시에 둘이 수집·저장하지 않는다.

## 2. 상태 모델

```
Docked    (Host 소유)   Host 안 인프로세스 위젯 · 원본 창 없음
Detached  (프로세스 소유) 원본 프로세스 창 · Host에는 인스턴스 없음(레이아웃 기록만)
```

Host의 기존 `Floating`(Host 소유 플로팅 창)은 그대로 두고, `Detached`는 위젯이 이양 핸들러를 제공할 때만 쓴다. 핸들러가 없거나 이양이 실패하면 기존 Floating으로 폴백한다.

| 전이 | 순서 |
|---|---|
| Docked → Detached | ① Host가 위젯 상태를 저장(`SaveStateAsync`) → ② 핸들러 `DetachAsync(state, 드롭 좌표)` → ③ 원본이 상태를 **받아 적용하고 창을 띄운 뒤 `ack`** → ④ 그때서야 Host가 인스턴스를 제거(`DockState=Detached`로 레이아웃 기록). ack 전에 실패하면 Host 인스턴스는 그대로 두고 Floating 폴백 |
| Detached → Docked | ① 원본 창을 Host 위로 드롭 → ② 원본이 `dockRequest{state, cursor}` 전송 → ③ Host가 상태를 Host 상태 저장소에 쓰고 인스턴스 생성·삽입 위치에 도킹 → ④ Host가 `ack` → ⑤ 원본이 창을 닫고 수집을 멈춘다 |
| Detached, 원본이 크래시 | 파이프가 끊기면 Host 레이아웃의 Detached 기록만 남는다. 다음 도킹 요청 때까지 표시 없음(사용자가 + 메뉴로 다시 추가 가능) |

**ack를 받은 쪽만 소유자가 된다.** 그 사이 잠깐은 원본이 아직 받지 않았으므로 Host가 소유자로 남고, 반대로 원본이 넘긴 뒤 ack 전에 끊기면 원본이 소유자로 남는다. 이렇게 "이중 소유"와 "소유자 없음"을 모두 피한다.

## 3. 무엇이 재사용되는가

- 인프로세스 위젯 `AIUsage.Widget`과 테스트 42개: 그대로.
- 상태 전달 단위: `FeatureStateSnapshot`(버전 포함). 원본 프로세스는 표준 `state.json`을 쓰므로, 받은 스냅샷의 기능 필드를 자기 `AppState`에 적용한다(창 위치·테마 같은 셸 값은 건드리지 않음).
- 수집 충돌 방지: 원본이 뜨면 Host 위젯은 수집을 멈추는 `CollectionOwnership`. 이양 중 잠깐 겹쳐도 안전망이 된다.
- 히스토리(`history.jsonl`)는 두 소유자가 같은 파일을 쓰지만, 한 번에 한 쪽만 쓰므로 그대로 이어진다.

## 4. 컨트랙트 v1.1 (Abstractions, 하위 호환)

기존 위젯은 영향 없다. 이양을 지원하는 위젯만 아래를 구현한다.

```csharp
// Host 수명 동안 하나 존재. 위젯 DLL이 제공하고 Host가 로더로 발견한다(parameterless 생성자).
public interface IWidgetDetachHandler
{
    string WidgetId { get; }

    // 소유권을 외부 표면으로 넘긴다. 상대가 받아 적용하고 표시까지 마친 뒤에만 true.
    Task<bool> DetachAsync(DetachRequest request, CancellationToken ct);

    // 외부 표면이 Host로 돌아오겠다고 요청. Host가 처리하고 결과(수락 여부)를 돌려준다.
    event Func<DockRequest, Task<bool>>? DockRequested;

    // 외부 표면이 지금 살아 있는가(Detached 유지 여부 판단).
    bool IsDetached { get; }

    Task ShutdownAsync();
}

public sealed record DetachRequest(string InstanceId, int StateVersion, string StateJson, WidgetRect ScreenBounds, double Dpi);
public sealed record DockRequest(int StateVersion, string StateJson, WidgetPoint ScreenCursor);
```

- 좌표는 가상 화면 **물리 픽셀 + dpi**로 주고받아 모니터 DPI 차이를 피한다.
- `ProcessExecution` 능력을 선언하고 Host 권한 프롬프트를 통과해야 핸들러가 원본 프로세스를 실행할 수 있다.
- 핸들러 검증: `WidgetId`가 등록된 위젯과 일치해야 하며, 아니면 무시하고 로그.

## 5. IPC (named pipe, JSON lines)

파이프: `\\.\pipe\AIUsage.Handoff.<사용자SID해시>` — 현재 사용자만 접근하도록 ACL 제한. **서버는 원본 프로세스**(항상 떠서 대기 가능)이고 Host 쪽 핸들러가 클라이언트로 붙는다. 원본이 Host로 돌려보낼 때는 같은 연결의 역방향 메시지를 쓴다.

| 방향 | 메시지 | 의미 |
|---|---|---|
| Host → 원본 | `hello{protocol:1, hostPid}` | 연결 |
| 원본 → Host | `welcome{protocol:1, appVersion, canAdopt:true}` | 이양을 받을 수 있음 |
| Host → 원본 | `adopt{state:{version,json}, x,y,w,h,dpi}` | 소유권을 넘긴다: 상태 적용 + 창 표시 |
| 원본 → Host | `adopted` (ack) / `refused{reason}` | 받아서 창이 떴다 / 거부 |
| 원본 → Host | `dockHover{cx,cy}` / `dockHoverEnd` | 원본 창을 Host 위로 끌고 지나는 중(Host가 삽입선 표시) |
| 원본 → Host | `dockRequest{state, cx,cy}` | 드롭됨: 소유권을 돌려준다 |
| Host → 원본 | `docked` (ack) / `dockRefused{reason}` | Host가 받았다 → 원본은 창을 닫고 수집 중지 |
| 양방향 | `bye` | 정상 종료 |

- 프로토콜 버전이 다르면 `hello`에서 협상 실패 → Host는 Floating 폴백.
- 파이프가 없으면(원본이 안 떠 있으면) 핸들러가 원본을 `--adopt` 모드로 실행한 뒤 재연결한다(제한 시간 5초).
- 원본 실행 파일 위치: 원본이 설치 시 레지스트리 `HKCU\Software\AIUsageMonitor\InstallPath`에 자기 경로를 기록(시작 시 갱신). 핸들러는 그 값만 신뢰하고 위젯 DLL 옆 경로는 신뢰하지 않는다.

## 6. 드래그 처리

- **Host → 밖**: 기존 드래그 로직 그대로. 드롭 순간 `DetachAsync`가 호출된다. 드래그 시작 시점에 핸들러가 미리 연결해 두어(`hello/welcome`) 드롭 후 지연을 줄인다. 이어잡기(창을 붙잡은 채로 드래그 계속)는 하지 않는다.
- **원본 창 → Host**: 원본의 자체 드래그(자석 스냅)는 유지한다. Host가 연결돼 있으면(`hello`를 보낸 경우) 커서가 Host 창 사각형 위에 있을 때 `dockHover`를 보내 Host가 삽입선을 표시하고, 놓으면 `dockRequest`. Host 창 사각형은 `hello`에 실린 Host 프로세스 id로 그 프로세스의 메인 창을 읽기 전용으로 조회한다(`HostWindowLocator`, 재부모화·창 조작 없음).

## 7. 예외 처리

| 상황 | 처리 |
|---|---|
| 원본 미설치/경로 없음/실행 실패 | Host Floating 폴백(위젯은 Host가 계속 소유), 로그 경고 |
| ack 시간 초과(5초) | 이양 취소, Host가 계속 소유. 원본이 뒤늦게 받았다면 `revoke`를 보내 창을 닫게 함 |
| 원본이 Detached 중 종료 | 사용자가 종료한 것으로 간주. Host에 인스턴스가 없으므로 아무 일도 없음(레이아웃의 Detached 기록만 정리) |
| Host가 Detached 중 종료 | 원본은 계속 산다. Detached는 저장하지 않으므로 다음 Host 시작 때 그 위젯은 나타나지 않는다 |
| 파이프 위조 | 현재 사용자 ACL, `hello` 검증, 원본은 Host PID가 실제 Host 프로세스인지 확인 |
| 상태 버전 불일치 | 받는 쪽이 `Deserialize`에서 거부(null) → `refused` → 이양 취소 |

## 8. 스펙과의 관계

- §20(HWND 재부모 금지): 준수. 창을 붙이지 않고 소유권만 넘긴다.
- §17(상호작용은 Host 소유): Host 드래그가 분리의 트리거. 원본은 도킹을 **요청**만 한다.
- 스펙 문서 개정: `MAIN_DOCKER_HOST.md`에 `Detached` 상태와 이양 시퀀스, `COMPATIBLE_WIDGET_PROCESS_SPEC.md`에 `IWidgetDetachHandler`(선택) 추가.

## 9. 구현 현황 (2026-09-30)

| 단계 | 상태 | 위치 / 검증 |
|---|---|---|
| H1 프로토콜·파이프 | 완료 | `AIUsage.Core/Handoff/*` — 루프백 테스트 8개(요청·응답, 대용량 상태, 양방향 요청, 시간 초과, 끊김, 교체 연결, 잘못된 줄). 수신 요청은 도착 순서대로 처리(호버 시작/종료가 뒤바뀌지 않음) |
| H2 원본 앱 | 완료 | `AppHandoffService`(Presentation), `WidgetHandoffSurface`, `WidgetWindow`(표시·호버·드롭), `App`(`--adopt`, 설치 경로 등록), `HostWindowLocator`, `UsageFeatureViewModel.AdoptFeatureState` — 서비스 테스트 12개 |
| H3 Host측 핸들러 | 완료 | `AIUsageDetachHandler`(위젯 DLL, 매개변수 없는 생성자) — 실제 파이프+실제 서비스 통합 테스트 13개 |
| H4 ModuleDock | 완료 | `Abstractions/Detach.cs`(`IWidgetDetachHandler` 등), 로더의 핸들러 발견, `HostController`(분리·도킹·호버·폴백·권한), `Program` — Host 테스트 12개 추가(96개 통과) |
| H5 통합 | 부분 | 실제 프로세스 두 개(격리된 Host 복사본 + 실제 `AIUsageMonitor.exe --adopt`)로 **분리 → 앱 기동·상태 이어받기 → 원본 창 드롭 → Host 재도킹**이 로그로 확인됨(`handed over to its application`, `docked from its application`). 이 과정에서 드래그 중 위젯이 제거되면 `KeyNotFoundException`이 반복되는 Host 버그를 발견해 수정(`AbandonGestureOf`, 회귀 테스트 포함) |

배포: `tools/deploy-to-host.ps1`이 위젯과 Core/Presentation DLL을 Host의 `widgets\AIUsage`에 복사한다. 실행 중인 Host는 그 폴더의 DLL을 잠그므로 Host를 종료한 뒤 배포한다.

## 10. 작업 분해 (계획 당시)

| 단계 | 저장소 | 내용 | 검증 |
|---|---|---|---|
| H1 | App(Core) | 프로토콜 메시지/직렬화, 파이프 서버·클라이언트(`Handoff`) | 루프백 단위 테스트(정상, 거부, 시간 초과, 끊김) |
| H2 | App(UsageMonitorWpf) | 원본의 이양 서버: `adopt` 처리(상태 적용·창 표시·ack), `dockHover/dockRequest` 송신, `--adopt` 실행 인자, 설치 경로 레지스트리 기록 | 하네스 + 단위 테스트 |
| H3 | App(Widget) | `AIUsageDetachHandler : IWidgetDetachHandler` | 루프백 + 가짜 원본 테스트 |
| H4 | ModuleDock | Abstractions v1.1, 로더가 핸들러 발견, `DockState.Detached`, `HostController`의 Detach/Dock 분기, 레이아웃 저장 | 단위 테스트 |
| H5 | 전체 | 통합 시나리오(두 프로세스): 분리→독립 생존, 재도킹, 실패 폴백 | 체크리스트 + 스크린샷 |

## 11. 결정 사항

1. 소유권: 한 번에 하나, 드롭 순간에 이양(ack 방식). ✔ 확정
2. Host 안 표면은 인프로세스 위젯 유지. ✔ 확정
3. 재도킹 후 원본 프로세스는 **종료한다**(확정). 남겨 두면 단일 인스턴스 규칙(독립 앱이 항상 수집 우선) 때문에 Host 안 위젯이 수집을 양보해 멈춘다. 다시 분리하면 Host가 `--adopt`로 새로 띄운다.
4. Host의 "상세 보기"는 Host 안 상세 창을 유지(원본 대시보드 연결은 이후 옵션).
5. `Detached` 상태는 **레이아웃에 저장하지 않는다.** 이양이 끝나면 Host는 인스턴스와 상태를 지우고(소유권이 완전히 넘어감), Host를 다시 켜도 그 위젯은 나타나지 않는다(원본이 살아 있으면 그쪽에 있음, 다시 넣으려면 + 메뉴 또는 원본 창을 Host로 드롭).
