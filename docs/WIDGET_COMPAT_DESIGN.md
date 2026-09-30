# AI Usage Monitor — Host 호환 위젯 대응 설계

기준 문서: `C:\Work\ModuleDock\COMPATIBLE_WIDGET_PROCESS_SPEC.md` (Contract v1.0.0), `MAIN_DOCKER_HOST.md`
대상: `UsageMonitorWpf` (v0.9.0, 단일 WPF 프로젝트)

## 1. 목표

하나의 기능 구현(AI 사용량)을 **독립 실행 앱**과 **ModuleDock Host의 자식 위젯** 양쪽으로 재사용한다.
Host가 도킹·드래그·호버/포커스·플로팅·상세창·레이아웃 저장을 소유하고, 위젯은 기능·표시·상태만 소유한다.

비목표: 기존 사용자 동작 변경. 독립 실행 앱은 0.9.x와 동일하게 동작해야 하며, 자동 업데이트(SelfUpdater)와 GitHub Release 파이프라인도 유지한다.

## 2. 현재 구조와 스펙 간 갭

| 스펙 요구 | 현재 상태 | 갭 |
|---|---|---|
| Core/Presentation/App/Widget 분리 (§2) | 단일 프로젝트. 폴더(`Core`, `Providers`, `Refresh`, `Storage`, `ViewModels`)만 논리 분리 | 프로젝트 분리 필요 |
| Core는 Host·UI 비의존 (§2) | `Providers/*`, `Core/Models·Formatters·UsageAnalytics`가 `Loc`(UI 로컬라이저)와 `AppLog`에 의존. `Refresh/RefreshScheduler`가 `System.Windows` 사용 | 추상화 주입 필요 |
| Summary View가 Natural/Compact/Collapsed 지원 (§6, §18) | `WidgetWindow.xaml`이 Compact/Normal/**Detailed**를 **사용자 설정**(`WidgetMode`)으로 선택. Collapsed 없음 | Host가 모드를 선택하는 구조로 전환, Collapsed 템플릿 신설 |
| 위젯은 도킹 엔진을 갖지 않음 (§8, §20) | `WidgetWindow.xaml.cs`(474줄)가 자석 스냅·가장자리 폴딩·슬라이드·`HandleWindow`·드래그를 자체 구현 | 위젯에서 제거, 독립 앱 셸에만 잔류 |
| Detail View가 MainWindow를 가정하지 않음 (§7) | 대시보드는 `MainWindow.xaml`(1104줄) 자체가 창 | 콘텐츠를 UserControl로 추출 |
| 공유 ViewModel (§9) | `MainViewModel`(1415줄) 하나를 위젯·칩·대시보드가 공유 | 방향은 맞음. 단 God-object이고 UI 창을 직접 생성(`RefreshScheduleWindow`, `Application.Current`) |
| Manifest / IComposableWidget (§3, §5) | 없음 | 신규 |
| 상태 저장/복원 + 버전 (§11, §12) | `StateStore` → `state.json` 하나에 기능 상태와 Host 소유 상태(`WidgetLeft/Top/DockEdge/Folded`, `ChipsLeft/Top`)가 혼재. `SchemaVersion`은 있음 | 분리 필요 |
| 인스턴스 ID (§10) | 싱글 인스턴스 뮤텍스, `AllowMultipleInstances=false`가 자연스러움 | Manifest에 명시 |
| Capabilities / Permissions (§15–16) | 없음. 실제로는 FileSystem(자격 증명·세션 로그), Network, ProcessExecution(`LoginHelper`), Notifications 사용 | 선언 + Host API 경유 |
| Host Context 사용 (§13) | 트레이 `NotifyIcon`, `AppLog`, `StartupService` 등을 직접 사용 | 위젯 모드에서는 Context로 대체 |

## 3. 목표 구조

```
Dora.Widget.Abstractions   (Host 저장소, net8.0)   ← 계약 타입
        ▲
AIUsage.Core               (net8.0)                ← 비즈니스 로직, WPF/Host 비의존
        ▲
AIUsage.Presentation       (net8.0-windows, WPF)   ← ViewModel + 표시 모드 인식 View(UserControl)
   ▲            ▲
AIUsage.App     AIUsage.Widget  ← Abstractions + Presentation 참조
(독립 셸)        (Host 어댑터)
```

- `Dora.Widget.Abstractions`는 ModuleDock 저장소의 프로젝트를 **ProjectReference**로 참조한다(패키지 발행 전까지). 참조는 `AIUsage.Widget`만 가진다. Core/Presentation/App은 Abstractions에 의존하지 않는다. → 독립 앱이 Host 없이 빌드된다.
- 저장소는 현재 `C:\Work\App` 하나. 솔루션 `AIUsage.sln`을 추가하고 프로젝트는 `src/`가 아니라 **기존 폴더명을 유지한 채 분리**해 git 이력 추적을 보존한다(§7 참조).

### 3.1 파일 배치

| 새 프로젝트 | 이동 대상 (현재 경로) |
|---|---|
| **AIUsage.Core** | `Core/Models.cs`, `Defaults.cs`, `CollectorPolicy.cs`, `Formatters.cs`, `UsageAnalytics.cs`, `SessionWindow.cs`, `Providers/*`(Claude/Codex/Copilot/CredentialLocator/UsageAggregator/UsageCollector), `Refresh/RefreshRunner.cs`, `Refresh/RefreshScheduler.cs`, `Storage/StateStore.cs`(리팩터 후) |
| **AIUsage.Presentation** | `ViewModels/*`, `Controls/HistoryChart.cs`, `Controls/ColorPickerButton`, `Converters/*`, 테마 리소스(`App.xaml`의 ResourceDictionary 분리), `Core/Loc.cs`, `Core/ThemeService.cs`, 신규 `Views/UsageSummaryView`, `Views/UsageDetailView` |
| **AIUsage.App** | `App.xaml(.cs)`, `MainWindow`(→ 얇은 셸), `WidgetWindow`+`HandleWindow`(독립 모드 자체 도킹 셸), `ChipsWindow`, `RefreshScheduleWindow`, `LegalTextWindow`, 트레이, `SelfUpdater`, `UpdateChecker`, `StartupService`, 폰트/아이콘 에셋. **AssemblyName은 `AIUsageMonitor` 유지** |
| **AIUsage.Widget** | 신규: `AIUsageWidget : IComposableWidget`, `AIUsageWidgetContextAdapter`, 상태 매핑 |

> 참고: `Loc`이 Core의 Provider에서 쓰이는 문제는 §4.1에서 해결.

## 4. 핵심 설계 결정

### 4.1 Core 비의존화 — 문자열/로그/스레드 추상화

Core가 UI를 알지 못하도록 다음 3개 seam만 만든다(최소 침습).

```csharp
namespace AIUsage.Core;

public interface IMessageCatalog { string Msg(string key, params object[] args); }   // Loc.Msg 대체
public interface IAppLogger      { void Write(string message); }                     // AppLog 대체
public interface IUiDispatcher   { void Post(Action a); }                            // RefreshScheduler의 System.Windows 대체
```

- Provider는 생성자/정적 레지스트리를 통해 `IMessageCatalog`를 받는다. 기존 `Loc.Msg(...)` 호출부는 기계적으로 치환 가능(약 60곳).
- 독립 앱: 기존 `Loc`/`AppLog` 구현을 주입. Widget: Host의 `IWidgetContext.Logger`로 브리지하고, 문자열 카탈로그는 Presentation의 `Loc`을 그대로 사용(위젯도 한국어/영어 지원 유지).
- `RefreshScheduler`의 `System.Windows` 사용처를 조사해 `IUiDispatcher`로 치환한다(구현 시 정확한 사용처 확인 필요).

### 4.2 ViewModel 분할 (God-object 해소, 스펙 §9 충족)

`MainViewModel`(1415줄)을 다음처럼 나눈다. **하나의 `UsageFeatureState`를 모든 표면이 공유**한다.

```
UsageFeatureViewModel        ← 데이터/새로고침/Provider·계정 목록/표시 옵션(남은량↔사용량)/차트 데이터
 ├─ SummaryViewModel 없음: SummaryView는 동일 VM을 DisplayMode 속성만 바꿔 바인딩
 ├─ Detail 전용 상태: 탭 인덱스, 히스토리 범위/모드, 계정 편집, 테마 프리셋 → DetailViewModel (Feature VM 소유)
 └─ AppShellViewModel (App 전용): WindowVersion(Mini/Expanded), ShowTaskbarChips, RunAtStartup,
                                   업데이트 확인/다운로드, AlwaysOnTop, 위젯 투명도, 종료 요청
```

분리 기준: **Host가 아닌 앱 셸이 소유해야 할 것은 Shell VM으로**(창 배치·칩·시작프로그램·자동 업데이트·창 투명도). 스펙 §11은 Host 소유 상태(위치, 도킹, 크기)를 위젯이 저장하는 것을 금지하므로 `WidgetOpacity`, `HoverOpaque`, `AlwaysOnTop`은 위젯 모드에서 **무시/미노출**이다.

`MainViewModel` 안의 UI 직접 호출(`new RefreshScheduleWindow`, `Application.Current.Dispatcher`, `ShowLegalDocument`)은 `IDialogService`/`IUiDispatcher` 인터페이스 뒤로 옮기고, 독립 앱과 Host 어댑터가 각자 구현한다(위젯에서 스케줄 편집기는 Detail View 안 인라인 패널 또는 Host 다이얼로그 요청 커맨드로 처리).

### 4.3 표시 모드 (Natural / Compact / Collapsed)

기존 `WidgetMode`(Compact/Normal/Detailed) 사용자 설정과 Host 모드는 **개념이 다르다**. 정리:

- **Host 모드(스펙)**: Host가 공간 기준으로 결정. 위젯은 `DisplayMode` 의존 속성을 받아 렌더링만 한다.
- **기존 `WidgetMode`**: 독립 앱(Mini 창)에서 사용자가 고르는 밀도 설정으로 남긴다. 위젯 모드에서는 무시한다.
- 매핑(독립 앱 셸이 동일 View를 재사용할 때): Compact→Compact, Normal→Natural, Detailed→Natural(+`ShowDetails=true`).

`UsageSummaryView`(UserControl, `IDisplayModeAware` 구현, `DisplayMode` DependencyProperty)의 3개 템플릿:

| 모드 | 내용 (현재 `WidgetWindow.xaml` 요소 매핑) |
|---|---|
| **Collapsed** | 아이콘 + 짧은 이름 + 대표 1개 값 (예: `[AI] 72%`). 대표 계정 = `FavoriteProvider/FavoriteAccount`(이미 존재) |
| **Compact** | 계정별 1줄: `PrimaryMark Title` · `RemainingLine/UsedLine` · `CountdownLine`. 프로그레스 바 생략 |
| **Natural** | 현재 WidgetWindow 본문 그대로: 헤더 제외한 계정 카드 + 프로그레스 바 + 주간 라인 + Source/Forecast + 로그인 버튼 |

`WidgetWindow.xaml`에서 다음은 **View 밖(셸)에 남긴다**: 헤더의 Fold/상세/× 버튼, ContextMenu(투명도·숨김·종료), 드롭섀도, 창 크기.
즉 `WidgetWindow.xaml`의 `ItemsControl`+`DataTemplate`(77–141행)이 `UsageSummaryView`로 이동하는 부분이다.

### 4.4 Layout Profile 초안 (DIP)

현재 위젯 폭 330, 계정 카드 1개 ≈ 90–130px, 최대 계정 3+. 제안값(구현 후 실측으로 보정):

| | Natural | Compact | Collapsed |
|---|---|---|---|
| 기본 | 330×260 | 240×96 | 120×36 |
| 최소 | 300×180 | 200×72 | 88×32 |

높이는 계정 수에 따라 가변이므로 Host의 `MinNaturalSize` 검증에는 "카드 1장" 기준 최소치를 선언하고, 그 이상은 View가 스크롤/축약으로 흡수한다(스펙 §20 "고정 Host 크기를 가정하지 않음").

### 4.5 Manifest

```csharp
new WidgetManifest {
  Id = "dev.leedoha.aiusage.summary",         // 스펙 예시와 동일, 릴리스 후 불변
  Name = "AI Usage", Version = <AssemblyVersion>, ContractVersion = new(1,0,0),
  Layout = <§4.4>,
  Capabilities = FileSystem | Network | ProcessExecution | Notifications,
  AllowMultipleInstances = false,              // 계정 여러 개는 위젯 하나 안에서 처리
  SupportsDetailView = true,                   // 그래프/히스토리/설정
  SupportsFloating = true,
  Description, Author = "leedoha", IconKey = "aiusage", Category = "Monitoring" }
```

Capabilities 근거: 자격 증명·Codex 세션 로그 읽기(FileSystem), 사용량 API(Network), `LoginHelper`의 CLI 로그인 실행(ProcessExecution), 리셋/임계치 알림(Notifications). **`ProcessExecution`은 Permissions API로 요청 후 사용**(§4.7).

### 4.6 위젯 어댑터 (`AIUsage.Widget`)

```
AIUsageWidget : IComposableWidget
  InitializeAsync   → 공유 UsageFeatureViewModel 생성(또는 프로세스 내 공유 싱글턴 조회), 스케줄러 시작, 커맨드 등록
  CreateSummaryView → new UsageSummaryView { DataContext = vm }  (Host가 DisplayMode 주입)
  CreateDetailView  → new UsageDetailView { DataContext = vm }   (Window 아님, UserControl)
  Save/RestoreState → §4.8
  ShutdownAsync     → 타이머/스케줄러 정지, 이벤트 구독 해제
```

- 도킹/플로팅/상세 표면이 **같은 VM 인스턴스**를 바인딩 → §9 만족.
- 상세 창 래핑, 플로팅 창 생성, 드래그는 Host 몫. 위젯 코드에 `Window`가 등장하지 않아야 한다.

### 4.7 Host 서비스 연동

| 현재 직접 사용 | 위젯 모드 대체 |
|---|---|
| `NotifyIcon.ShowBalloonTip` (알림) | `context.Notifications.Notify` |
| `AppLog.Write` | `context.Logger` |
| `LoginHelper`(외부 프로세스 실행) | `context.Permissions.RequestAsync(ProcessExecution)` 승인 후에만 실행 |
| 트레이/칩/시작프로그램/자동 업데이트 | 위젯에서 **비활성**(앱 셸 기능) |
| 새로고침 커맨드 | `context.Commands`에 `aiusage.usage.refresh` 등록 |

이벤트/커맨드 네이밍(`<domain>.<resource>.<action>`):
- 발행: `aiusage.provider.changed`, `aiusage.usage.updated`, `aiusage.threshold.reached`
- 처리: `aiusage.usage.refresh`, `aiusage.account.select`

### 4.8 상태 저장 분리

`AppState` 분해:

| 항목 | 소유 | 저장 위치 |
|---|---|---|
| `Providers`, `Refresh` 로그, 계정/자격 경로, `Settings`의 기능 옵션(`DisplayUsageAs`, `CollectionLevel`, `FavoriteProvider`, 임계치, 알림, 히스토리 범위/모드, 테마, 언어) | **위젯**(기능 상태) | `IWidgetStateWriter`에 `{stateVersion, state}` JSON. 독립 앱은 기존 `state.json` 유지 |
| `WidgetLeft/Top/DockEdge/Folded`, `WindowLeft/Top`, `ChipsLeft/Top`, `WidgetOpacity`, `HoverOpaque`, `AlwaysOnTop`, `WindowVersion`, `ShowTaskbarChips`, `RunAtStartup` | **앱 셸**(독립 모드 전용) / Host(위젯 모드) | 위젯은 저장 금지 |

구현: `AppState`를 `UsageFeatureState`(위젯 저장 대상)와 `AppShellState`로 분할하고, `StateStore`는 두 개를 조합해 기존 `state.json` 포맷을 **하위 호환으로 읽는다**(`SchemaVersion` 3→4, 마이그레이션 코드 유지). 위젯 `stateVersion`은 4부터 시작해 동일 마이그레이터를 공유한다.

- `history.jsonl`(사용 기록)은 데이터 파일이라 위젯 상태가 아니다. 위치는 `IWidgetSettings`가 아니라 기존 데이터 디렉터리(`%APPDATA%\UsageMonitorWpf`)를 계속 사용하고, **독립 앱과 위젯이 같은 디렉터리를 공유하면 동시 실행 시 충돌**하므로 §6 리스크로 관리.

## 5. 독립 앱 셸 처리

`AIUsage.App`은 동작을 바꾸지 않고 재배선만 한다.

- `MainWindow`: 대시보드 콘텐츠(탭들)를 `UsageDetailView`로 추출 후 `<views:UsageDetailView/>`를 호스팅하는 얇은 창으로 축소.
- `WidgetWindow`: 헤더/폴드/컨텍스트 메뉴/자석 도킹은 그대로 두고 본문만 `<views:UsageSummaryView DisplayMode="{...}"/>`로 교체. **독립 앱의 자체 도킹은 Host 위젯 모드와 무관한 셸 기능**이며 스펙 §20("위젯이 도킹 엔진을 갖지 않음")은 `AIUsage.Widget`에만 적용된다.
- `ChipsWindow`: 그대로 Shell 소속.

## 6. 리스크와 미결정

1. **로딩 방식**: 위젯이 Host와 **같은 프로세스**의 어셈블리로 로드되는가? 스펙 §20은 "외부 프로세스 HWND 부모 삽입 금지"만 명시. 현재 Host 코드는 `Func<IComposableWidget>` 팩토리 등록이라 in-proc 전제. 그렇다면 `AIUsage.Widget.dll`을 Host가 참조/플러그인 로드해야 함 → **Host 쪽 로더가 아직 없음**(확인 필요).
2. **동시 실행**: 독립 앱과 Host 안 위젯이 동시에 떠 있으면 `state.json`/`history.jsonl` 쓰기 경합, 이중 폴링, 중복 알림. 완화: 파일 락 + 위젯 모드에서는 저장 경로를 Host 제공 스토리지로 분리하거나, 실행 시 독립 앱 뮤텍스를 감지해 폴링 비활성화.
3. **자격 증명 접근 정책**: Host 권한 모델에서 FileSystem/Network 승인 UX가 정의되기 전까지는 선언만 하고 `IsGranted` 확인은 no-op 기본 허용으로 두되 체크 지점은 심어둔다.
4. **`Loc`/테마의 전역 정적 상태**: `Loc.Apply`, `ThemeService.Apply`가 `Application.Resources`를 직접 바꾼다. Host 앱에 리소스를 주입하면 다른 위젯과 충돌 가능 → 위젯 View는 자체 `ResourceDictionary`를 `UserControl.Resources`에 병합하고 전역 테마 변경은 독립 앱에서만 수행.
5. **릴리스 파이프라인**: `tools/publish-release.ps1`, `.github/workflows/release.yml`, `SelfUpdater`가 단일 csproj/`AIUsageMonitor.exe` 경로를 가정. 프로젝트 분리 후에도 `AIUsage.App`을 publish 대상으로 바꾸고 산출물 이름을 유지해야 자동 업데이트가 깨지지 않는다. **분리 커밋과 릴리스는 반드시 별도로**.
6. **Mac 프로젝트(`UsageMonitorMac`)**: `Core/*`가 복제 파일 형태. `AIUsage.Core`(net8.0, UI 비의존)가 되면 Mac 쪽이 링크 참조로 통합될 여지가 있으나 이번 범위 밖.

## 7. 단계별 실행 계획

각 단계는 **빌드 통과 + 독립 앱 동작 동일**을 완료 조건으로 하며 단독 커밋 가능.

| 단계 | 내용 | 산출물/검증 |
|---|---|---|
| **0** | 현행 동작 기준선: 수동 체크리스트(3개 Provider 갱신, 로그인, 위젯 스냅/폴드, 차트, 자동 업데이트 dry-run) | 체크리스트 기록 |
| **1** | Core seam 도입(`IMessageCatalog`, `IAppLogger`, `IUiDispatcher`)만 하고 프로젝트 분리는 아직 안 함. `Providers/Refresh/Storage`에서 `Loc`/`System.Windows`/`AppLog` 직접 참조 제거 | 동일 csproj에서 빌드, 회귀 없음 |
| **2** | `AIUsage.Core` 프로젝트 분리(폴더 이동, 네임스페이스 정리), `AIUsageMonitor`가 참조 | Core에 `System.Windows` 참조 0 |
| **3** | `AppState` 분할 + 마이그레이션(`SchemaVersion` 4), `MainViewModel` 분할(Feature / Shell), 다이얼로그 서비스화 | 기존 `state.json`로 실행해 설정 유지 확인 |
| **4** | `AIUsage.Presentation` 분리 + `UsageSummaryView`(3모드) + `UsageDetailView` 추출, `WidgetWindow`/`MainWindow`를 셸로 축소 | 독립 앱 화면·동작 동일(스크린샷 비교) |
| **5** | `AIUsage.App` 개명/이동, 릴리스 스크립트·워크플로·`SelfUpdater` 경로 보정 후 **릴리스 드라이런** | `AIUsageMonitor.exe` 산출물 동일 |
| **6** | `AIUsage.Widget` + Manifest + `IComposableWidget` 구현, ModuleDock `ManifestValidator`/`WidgetRegistry.Register`로 검증 | ModuleDock 테스트 프로젝트에 등록 테스트 추가 |
| **7** | Host에서 Natural/Compact/Collapsed·플로팅·상세창·상태 복원 수동 검증, §21 체크리스트 전 항목 체크 | 체크리스트 완료 |

## 8. §21 준수 체크리스트 매핑

| 항목 | 충족 단계 |
|---|---|
| 고유 불변 Id / Name / Version / ContractVersion / 크기 / Capabilities / 3개 bool | 6 |
| InitializeAsync / CreateSummaryView / Save·Restore / ShutdownAsync | 6 |
| Natural·Compact·Collapsed 정상 렌더 | 4, 7 |
| Host Context 사용 | 1, 6 |
| 도킹 미소유, 상태 공유 | 3, 4, 6 |
| CreateDetailView (`SupportsDetailView=true`) | 4, 6 |
| 플로팅 컨테이너에서 Summary 동작 | 7 |

## 9. 확정된 결정 (2026-09-29)

1. **위젯 로딩: in-proc 플러그인 방식.** Host가 `plugins/*/` 폴더의 위젯 DLL을 `AssemblyLoadContext`로 로드한다. Host 로더는 ModuleDock 쪽 작업이며, 그 전까지는 프로젝트 참조로 등록해 검증한다. 위젯 호출부는 예외를 격리해 Host가 같이 죽지 않게 한다.
2. **저장소: 분리 (a안).**
   - `C:\Work\App`(현재): `AIUsage.App`(독립 셸)만 남긴다. 릴리스 파이프라인·`SelfUpdater`·`AIUsageMonitor.exe` 산출물 이름은 그대로.
   - 신규 저장소(가칭 `AIUsage`): `AIUsage.Core`, `AIUsage.Presentation`, `AIUsage.Widget`. ModuleDock의 `Dora.Widget.Abstractions`는 `AIUsage.Widget`만 참조.
   - 저장소 간 참조는 **NuGet 패키지**(Core/Presentation을 패키지로 발행, App이 버전 참조)를 기본안으로 한다. 로컬 경로 ProjectReference는 CI(`release.yml`)에서 깨지므로 개발용 오버라이드로만 쓴다. 대안은 git submodule.
   - 영향: §7의 단계 2·4·5에 "새 저장소로 이동 + 패키지 발행"이 추가된다. 새 저장소에서 Core/Presentation이 독립 빌드되는 것을 먼저 확인한 뒤 App 저장소의 참조를 전환한다.
3. **동시 실행: A안(한 번에 하나만).** 위젯 시작 시 독립 앱의 싱글 인스턴스 뮤텍스(`Local\UsageMonitorWpf-SingleInstance-...`)를 확인해, 이미 떠 있으면 위젯은 폴링·알림·저장을 끄고 읽기 전용으로 동작한다. 위젯도 자체 뮤텍스를 잡아 독립 앱이 나중에 켜질 때 서로 감지한다.

## 10. 구현 결과 (2026-09-30)

### 10.1 실제 구조

```
Dora.Widget.Abstractions  (ModuleDock 저장소)
        ▲
AIUsage.Core           net8.0-windows, WPF 비의존: Models/Providers/Refresh/Storage/Loc/AppLog,
                       IUsageStore, FeatureStateSnapshot, StateNormalizer, CollectionOwnership, AppIdentity
        ▲
AIUsage.Presentation   WPF 라이브러리: UsageFeatureViewModel, ProviderViewModel, IUiServices,
                       UsageSummaryView(3모드) / UsageDetailView / FeatureSettingsView / RefreshScheduleWindow,
                       Styles.xaml + Brushes.xaml + Fonts, LocResources, TitleBar
   ▲                    ▲
UsageMonitorWpf (App)   AIUsage.Widget: AIUsageWidget(IComposableWidget), Manifest, HostUiServices,
AIUsageMonitor.exe      HostStateStore, HostSummaryView
```

- 독립 앱 폴더명(`UsageMonitorWpf`)과 `AIUsageMonitor.exe`는 릴리스 스크립트/자동 업데이트 호환을 위해 유지했다(§3의 `AIUsage.App` 개명은 하지 않음).
- `MainViewModel`은 `UsageFeatureViewModel`을 상속한 **셸 전용** VM으로 남았다(업데이트·테마·시작프로그램·창 모드/투명도/칩·라이선스). XAML 바인딩 경로 변화 없음.
- `tests/AIUsage.Tests`: 35개(계약 준수, 상태 스냅샷, 소유권 정책, Host 런타임과의 생명주기).

### 10.2 스펙 §21 체크리스트 대응

| 항목 | 결과 | 근거 |
|---|---|---|
| Id/Name/Version/ContractVersion/크기/Capabilities/3개 bool | 충족 | `ContractTests` (Host `ManifestValidator` 이슈 0건) |
| Initialize / Summary / State / Shutdown | 충족 | `AIUsageWidget`, `WidgetLifecycleTests` |
| Natural/Compact/Collapsed 렌더 | 충족 | `UsageSummaryView`, 렌더 크기 테스트, 하네스 스크린샷 |
| Host Context 사용 | 충족 | 알림·권한·이벤트·커맨드·로그를 `IWidgetContext`로만 사용 |
| 도킹 미소유 / 상태 공유 | 충족 | 위젯 코드에 `Window` 없음(대화상자 제외). Summary/Detail이 동일 VM 인스턴스 |
| CreateDetailView | 충족 | 기능 설정 탭 포함, 앱 전용 설정 제외 |
| 플로팅에서 Summary 동작 | 부분 | Summary는 컨테이너 독립적으로 구현·검증. 실제 Host 플로팅 창 검증은 Host 통합 후 |

### 10.3 설계와 달라진 점

1. **Core seam 인터페이스 3종 폐기** (`IMessageCatalog` 등): `Loc`/`AppLog`가 이미 순수 코드였다. `Loc.Apply`만 `Loc.SetLanguage`(Core) + `LocResources.Apply`(WPF)로 분리.
2. **`AppState` 물리 분할 안 함**: `FeatureStateSnapshot`(Capture/ToAppState, `stateVersion`=1)로 기능 필드만 뽑는다. `state.json` 포맷과 마이그레이션은 그대로. Host가 소유하는 위치·도킹·테마·창 값은 스냅샷에 들어가지 않으며 테스트로 고정했다.
3. **저장소 추상화 `IUsageStore` 추가**: 독립 앱은 `StateStore`(state.json + history.jsonl), 위젯은 `HostStateStore`(기능 상태는 Host writer, 히스토리 파일만 공유). 위젯 첫 실행 시 독립 앱의 계정/설정을 가져온다.
4. **뷰가 자체 리소스를 가진다**: 뷰마다 `Styles.xaml`을 병합해 어떤 Host에서도 로드된다. 테마 색은 `Brushes.xaml`로 분리해 Application 수준에서 정의(독립 앱은 `ThemeService`가 덮어씀, Host는 자체 키로 테마 가능). 위젯은 정의되지 않았을 때만 기본값을 추가한다(`PresentationResources.EnsureThemeDefaults`).
5. **XAML 컨트롤은 다른 어셈블리에서 상속 불가**(BAML 리소스 탐색 실패, 테스트가 발견) → `HostSummaryView`는 `UsageSummaryView`를 감싸는 래퍼.
6. **권한 게이트**: `IUiServices.IsGranted/RequestAsync`. FileSystem+Network가 승인되기 전에는 수집하지 않고 안내 문구를 표시, 로그인과 예약 새로고침(둘 다 CLI 프로세스 실행)은 ProcessExecution 승인 후에만.
7. **동시 실행 A안**: `CollectionOwnership` — 독립 앱이 항상 우선, 위젯 프로세스끼리는 먼저 잡은 쪽. 소유하지 않은 인스턴스는 폴링·예약 실행·알림·히스토리 기록을 끄고 마지막 데이터와 안내 문구만 표시. 5초마다 재평가해 상대가 종료하면 인수.

### 10.4 Host 통합 가이드

- 등록: `registry.Register(() => new AIUsageWidget())` (in-proc). 플러그인 로더가 생기면 `AIUsage.Widget.dll`을 스캔 대상으로.
- Host가 해야 할 것: Summary 뷰를 `IDisplayModeAware`로 모드 통지, 상세 뷰를 Window로 래핑, `IWidgetStateStore`에 `{stateVersion, json}` 영속화, 권한 프롬프트 제공(없으면 위젯은 수집하지 않음).
- 테마: Host가 `InkBrush/MutedBrush/AccentBrush/PanelBrush/CanvasBrush/LineBrush`(+`HeaderBrush`, `Header*Color`) 키를 Application 리소스에 정의하면 위젯이 따른다.
- 알려진 한계: `Loc`(언어)와 `LocResources`가 Application 리소스에 `ui.*` 등 문자열 키를 푸시하는 전역 방식이다(다른 위젯과 키 충돌 없음을 전제). 위젯 언어 변경은 프로세스 전역에 영향.
- 알림 클릭 동작(로그인 안내 → 위젯 열기)은 Host 컨텍스트에 해당 개념이 없어 위젯 모드에서는 전달되지 않는다(알림 텍스트만).

## 11. 남은 작업

1. **저장소 분리 (a안)**: 로컬 저장소 `C:\Work\AIUsage`를 `tools/extract-shared-repo.ps1`로 만들고 커밋까지 마쳤다(빌드·테스트 35개·패키징 검증됨). 남은 것은 원격 생성, NuGet 발행 위치 결정, 이 저장소의 `PackageReference` 전환과 `release.yml` 수정, 전환 후 이 저장소의 `AIUsage.Core/Presentation/Widget/tests` 제거다. 전환 전까지는 두 곳에 같은 소스가 있으므로 이 저장소의 사본이 기준이다.
2. **Host 통합 검증**: 실제 Host에서 Natural/Compact/Collapsed 전환, 플로팅, 상세 창, 상태 복원을 수동 확인(§21 마지막 항목).
3. **플러그인 로더**: ModuleDock 쪽 작업(§9-1).
