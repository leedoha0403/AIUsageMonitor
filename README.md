# Usage Monitor

Claude Code, Codex, GitHub Copilot의 사용량, 리셋 시간, 데이터 출처, 신뢰도를 보는 Windows 데스크톱 앱입니다. 목표는 단순 사용량 표시기가 아니라 **AI Coding Usage Control Center**입니다. 평소에는 작업표시줄 칩 몇 픽셀만 쓰고, 필요할 때 값의 출처, 사용 패턴, Reset Timeline까지 확인합니다.

## 실행

`Start Usage Monitor WPF.cmd`를 더블클릭하면 Release로 빌드한 뒤 앱을 별도 프로세스로 띄우고 cmd 창은 스스로 닫힙니다. cmd 창을 닫아도 앱은 계속 실행되며, 앱 종료는 트레이 메뉴의 "종료"로 합니다. 이미 실행 중이면 새로 띄우지 않습니다.

```powershell
dotnet build .\UsageMonitorWpf\UsageMonitorWpf.csproj -c Release
.\UsageMonitorWpf\bin\Release\net8.0-windows\AIUsageMonitor.exe
```

개발 중에는 `dotnet run --project .\UsageMonitorWpf\UsageMonitorWpf.csproj`로도 실행할 수 있습니다. 다만 이렇게 실행하면 터미널을 닫을 때 앱도 함께 종료됩니다. .NET 8 SDK가 필요하며, 프로젝트 폴더에 `.dotnet`(로컬 SDK)이 있으면 그것을 우선 사용합니다.

## 릴리스 패키지 만들기

배포용 exe는 `.NET 런타임을 포함한 self-contained, single-file` 빌드로 만듭니다. 사용자가 .NET을 따로 설치할 필요가 없습니다.

```powershell
.\tools\publish-release.ps1
# 버전을 지정하려면
.\tools\publish-release.ps1 -Version 1.0.0
```

`UsageMonitorWpf.csproj`의 `Version`을 기준으로 빌드하며, 결과물은 `dist\release\`에 다음처럼 생성됩니다.

```text
dist\release\<version>\win-x64\AIUsageMonitor.exe    빌드 산출물(압축 전)
dist\release\AIUsageMonitor-v<version>-win-x64.zip   배포용 zip
dist\release\SHA256SUMS.txt                          zip의 SHA256 체크섬 목록
```

릴리스 빌드는 `InformationalVersion`에 `-internal` 접미사 없이 순수 버전 문자열을 심습니다. `dist/`는 git에 커밋되지 않으므로, GitHub Release를 만들 때 zip과 `SHA256SUMS.txt`의 해당 줄을 첨부물로 올리면 됩니다.

PowerShell MVP도 남겨두었습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\UsageMonitor.ps1
```

## 프로젝트 구조

| 프로젝트 | 역할 |
|---|---|
| `AIUsage.Core` | 수집·계산·저장 로직(Provider, 새로고침 스케줄러, 상태/히스토리 저장). WPF에 의존하지 않습니다. |
| `AIUsage.Presentation` | ViewModel과 재사용 가능한 뷰(요약 3모드, 상세, 기능 설정), 공용 스타일·폰트. |
| `UsageMonitorWpf` | 독립 실행 앱 셸(`AIUsageMonitor.exe`): 트레이, 칩, 미니 위젯 도킹, 자동 업데이트, 테마·시작프로그램. |
| `AIUsage.Widget` | ModuleDock Host용 어댑터(`IComposableWidget`). 같은 기능을 Host의 자식 위젯으로 제공합니다. |
| `tests/AIUsage.Tests` | 계약 준수·상태 저장·동시 실행 정책·위젯 생명주기 테스트. |

`AIUsage.Widget`과 테스트는 형제 폴더의 ModuleDock 저장소(`..\ModuleDock`)를 참조하므로 그 저장소가 있어야 빌드됩니다. 앱 빌드와 릴리스(`UsageMonitorWpf.csproj`)는 ModuleDock 없이 동작합니다. 설계와 Host 통합 방법은 `docs/WIDGET_COMPAT_DESIGN.md`를 참고하세요.

```powershell
dotnet test .	ests\AIUsage.Tests
```

## mac 호환 별도 프로젝트

`UsageMonitorMac`은 기존 WPF 앱과 분리된 `net8.0` 크로스플랫폼 프리뷰입니다. 아직 mac 네이티브 UI는 붙이지 않았고, Claude/Codex 수집, macOS 로그인 파일 탐색, state/history 저장, 예약 갱신 dry-run처럼 UI 아래에서 재사용할 핵심 레이어를 먼저 검증합니다.

```powershell
.\.dotnet\dotnet.exe build .\UsageMonitorMac\UsageMonitorMac.csproj
.\.dotnet\dotnet.exe run --project .\UsageMonitorMac\UsageMonitorMac.csproj -- --self-test
.\.dotnet\dotnet.exe run --project .\UsageMonitorMac\UsageMonitorMac.csproj -- --once
.\.dotnet\dotnet.exe run --project .\UsageMonitorMac\UsageMonitorMac.csproj -- --refresh-dry-run --provider codex
```

mac에서는 같은 명령을 `dotnet`으로 실행하면 됩니다. 테스트용 데이터 폴더는 `USAGE_MONITOR_DATA_DIR`로 분리할 수 있습니다.

## 정보 밀도 4단계

```text
Taskbar Chips   Claude 72% | Codex 41%        (작업표시줄 바로 위, 드래그 이동/위치 저장)
   ↓ 클릭
Mini Flyout     계정별 5H 사용량, 리셋 카운트다운 (Compact / Normal / Detailed)
   ↓ Details
Dashboard       Overview · History · Accounts · Snapshot · Diagnostics · Settings
```

- Tray 아이콘: 클릭하면 Flyout, 더블클릭하면 Dashboard. 메뉴에서 Widget Mode / Window / Collection Level / Taskbar Chips / Show Usage As 전환
- Used / Remaining 표시 전환, Favorite(Primary) Provider는 ★ 표시와 함께 맨 앞에 배치
- Mini 모드: 모든 위젯 모드에서 5시간 리셋까지 남은 시간을 표시하고, 칩에는 `4:15`처럼 짧게 표시합니다.
- 투명도: 미니 위젯과 칩의 투명도를 20~100% 범위에서 조정합니다(설정 탭, 또는 위젯/칩 우클릭 슬라이더). "마우스를 올리면 불투명" 옵션을 지원합니다.
- 가장자리 도킹과 폴딩: 미니 위젯을 화면 가장자리 근처(24px)로 끌면 자석처럼 붙습니다. 붙은 상태에서 화살표 버튼을 누르면 위젯이 그 가장자리로 슥 들어가고, 가장자리에 **Auto-hide Handle**(깜빡이며 나타나는 탭)이 남습니다. 핸들에 마우스를 올리면 위젯이 나오고, 마우스가 벗어나면 다시 들어갑니다. 도킹/폴딩 상태는 재시작 후에도 유지됩니다.
- 로그인 유도: 로그인이 안 된 계정에는 [로그인] 버튼이 표시되고, 누르면 PowerShell 창에서 `claude auth login` / `codex login`이 실행됩니다. CLI가 없으면 확인을 받은 뒤 설치하고 이어서 로그인 창을 엽니다. 로그인 파일이 생기면 자동으로 감지해 바로 수집합니다. 실행 중 한 번 알림으로 안내하며, 알림을 클릭하면 위젯만 열립니다(외부 프로세스는 버튼을 직접 눌렀을 때만 실행).
- 테스트용 데이터 폴더: 환경변수 `USAGE_MONITOR_DATA_DIR`를 지정하면 실제 데이터와 분리해 실행할 수 있습니다.
- 계정 관리: 왼쪽 목록에서 계정을 고르면 오른쪽에서 바로 편집합니다(이름, 설정 폴더 찾아보기, 모니터링 켜기/끄기, 기본 계정 지정, 로그인, 삭제). 계정마다 **표시 위치**(미니: 위젯·칩 / 대시보드)를 따로 켜고 끌 수 있습니다. 숨겨도 로그인은 유지되고 사용량 수집도 계속되며, 알림은 한 곳이라도 표시 중인 계정에만 보냅니다. 변경 사항은 즉시 저장되며, 개요 카드의 "계정 관리 ›"와 상세의 "개요에서 보기"로 서로 오갈 수 있습니다.
- 폰트: Spoqa Han Sans Neo를 내장했습니다(SIL OFL 1.1, `AIUsage.Presentation/Fonts`). subset Regular 파일의 메타데이터가 Bold로 표시되어 있어 Regular와 Medium/Bold를 별도 패밀리로 나눠 사용합니다.
- 다크 테마: 메뉴, 툴팁, 슬라이더, 체크박스, 스크롤바, 트레이 메뉴, 창 제목 표시줄까지 테마를 따릅니다.
- Windows 시작 시 자동 실행: 설정 탭 맨 위 또는 트레이 메뉴에서 켭니다. 현재 사용자 시작 프로그램(`HKCU\...\Run`, 관리자 권한 불필요)에 `AIUsageMonitor.exe --startup`으로 등록합니다. 로그인 시에는 대시보드를 띄우지 않고 트레이와 칩(미니 모드면 위젯)으로 조용히 시작합니다. 실행 파일 위치가 바뀌면 다음 실행 때 등록 경로를 자동으로 갱신하며, 작업 관리자에서 끈 상태도 그대로 반영됩니다.
- 아이콘: exe, 창 제목 표시줄, 트레이, 알림, 대시보드 및 위젯 헤더에 같은 앱 아이콘을 씁니다. `tools/make-icons.ps1 -Source <원본 이미지>`로 `UsageMonitorWpf/Assets/AppIcon.ico/.png`를 다시 만들 수 있습니다. 40px 이상은 원본 아트워크를 사용하고, 32px 이하(트레이 등)는 선명하도록 같은 디자인을 벡터로 다시 그립니다.
- 한국어/English: UI, 트레이 메뉴, 상태 메시지, 진단, 알림 전체를 번역했으며 언어를 바꾸면 즉시 반영됩니다(`Core/Loc.cs`).
- 업데이트 확인: 실행 10초 뒤와 설정 탭의 "업데이트 확인" 버튼에서 GitHub Releases의 최신 태그를 조회해 현재 버전과 비교합니다. 새 버전이 있으면 알림과 설정 탭에 표시하고, [다운로드]를 누르면 릴리스의 `AIUsageMonitor.exe`를 받아 SHA256SUMS(해당 항목 필수)로 검증한 뒤 앱을 종료하고 exe를 교체해 자동으로 다시 시작합니다(`UsageMonitorWpf/Shell/SelfUpdater.cs`). 이전 exe는 `.bak`으로 남았다가 다음 실행 때 지워지며, 교체에 실패하면 원래 exe로 되돌립니다. 설치 폴더에 쓰기 권한이 없으면 예전처럼 zip을 `다운로드` 폴더에 받아 직접 설치합니다(`UsageMonitorWpf/Shell/UpdateChecker.cs`).

## 기능

- **Collector Fallback 체인**: 계정마다 우선순위대로 실행하고, 필드 단위로 병합합니다. 각 필드는 먼저 값을 준 Collector가 소유합니다.
  - Claude: `Official`(OAuth usage endpoint) → `RateLimit`(비활성) → `Local`
  - Codex: `Official`(ChatGPT usage endpoint) → `SessionLog`(로컬 세션 로그의 rate_limits) → `Local`
  - GitHub Copilot: `Official`(`api.github.com/copilot_internal/user`) → `Local`
- **GitHub Copilot**: Copilot에는 5H/주간 창이 없고 월간 프리미엄 요청 한도만 있습니다. Copilot 카드는 5H 칸 대신 월간 사용률과 월간 리셋(매월 1일 00:00 UTC)을 주 지표로 보여주고, 프리미엄 요청/채팅/완성 사용 횟수(예: `Premium 44/300`)와 한도 초과 횟수를 함께 표시합니다. 예약 갱신과 5H 기반 속도/추정은 Copilot에 적용되지 않습니다.
- **Field-level Source / Confidence**: 5H 사용량, 5H 리셋, 주간 사용량, 주간 리셋, Plan마다 출처, 신뢰도, 갱신 시각을 기록합니다.
- **Multi Account**: 여러 계정을 동시에 모니터링합니다. 추가 계정은 각자의 CLI 설정 폴더(`CLAUDE_CONFIG_DIR` / `CODEX_HOME` / `COPILOT_HOME`)를 지정합니다. 계정별 활성화/비활성화가 가능하고, Accounts 탭에서 계정끼리 비교할 수 있습니다.
- **Credential 탐색**: 사용자 지정 경로 → 환경변수 → Windows CLI(`~/.claude`, `~/.codex`) → WSL(Deep)
  - Copilot: `~/.copilot/config.json`의 마지막 로그인 계정을 읽고, 토큰은 Copilot CLI가 저장한 Windows 자격 증명 관리자 항목에서 읽습니다. 기본 계정은 CLI와 같은 순서로 `COPILOT_GITHUB_TOKEN` / `GH_TOKEN` / `GITHUB_TOKEN` 환경변수를 먼저 봅니다.
- **Not signed in 처리**: 로그인이 없거나 만료된 Provider는 회색으로 표시하고 로그인 안내를 보여줍니다. 나머지 Provider는 정상 동작합니다.
- **Model breakdown / Extra Usage**: Claude Opus/Sonnet 주간 사용량, 유료 Extra usage, Codex credits(Dashboard 전용)
- **Usage History**: 로컬 `history.jsonl`에 30일 보관합니다(값이 바뀌었거나 5분이 지났을 때만 기록). 1H / 6H / 1D / 3D / 7D / 30D 기간을 선택할 수 있고, "시간 사용량"(5H 선 그래프)과 "누적 사용량"(5H 세션 경계마다 색칠된 블록으로 나눈 누적 그래프) 두 가지 보기를 전환할 수 있습니다. 범례에 마우스를 올리면 해당 계정만 강조됩니다.
- **Usage Velocity / Limit Forecast**: 최근 30분 변화량, 시간당 소비율, 5H Limit 도달 예상 시각. 공식 값이 아니므로 항상 `Estimated`로 표시합니다.
- **Reset Timeline / Reset History**: 현재 윈도우의 진행 과정과 최근 7일간 윈도우별 최대 사용량 및 Limit 도달 횟수
- **Threshold 알림**: 기본값은 70/80/90/95/100%이며 사용자가 지정할 수 있습니다. 같은 5H 윈도우 안에서는 새로 넘은 단계만 한 번 알립니다.
- **Provider Health / Collector Diagnostics**: 선택된 Source, 응답 시간, 연속 실패 수, Collector별 레벨, 최소 polling 주기, 마지막 실행, HTTP 상태, credential 위치(경로 라벨만)
- **Collector별 최소 Polling**: Official 60초, SessionLog 10초. 앱 갱신 주기가 더 짧아도 이 값을 지킵니다.

## 예약 갱신

지정한 시각에 **최소 요청 1회**를 보내 다음 5시간 사용 주기를 시작합니다. 실제 모델 요청이라 사용량이 아주 조금 소모되므로, 사용자가 직접 예약한 계정에만 동작합니다.

- 세 가지 상태를 구분합니다. **갱신 가능 시각**(언제부터 새 주기를 시작할 수 있는지), **알림**(이제 시작할 수 있다고 알려줌), **예약 갱신**(그 시각에 CLI를 최소 비용으로 자동 호출).
- 실행 시간은 갱신 가능 즉시 / 시간 지정(예: 20:00) / 갱신 가능 후 지연(+15분~4시간) 중에서 고릅니다. 반복은 한 번만 / 매 갱신 가능 시 / 지정 시간대만(예: 09:00~02:00, 새벽 제외) 중에서 고릅니다.
- 안전 조건: 예약 시각이 되어도 사용량을 다시 조회해, 현재 주기가 아직 남아 있으면 실행하지 않고 갱신 가능 시각으로 옮깁니다. 같은 계정과 같은 예약 시각으로는 한 번만 실행합니다.
- 실패하면 원인을 CLI 없음 / 로그인 만료 / 프로세스 실행 실패 / 응답 없음 / 네트워크 오류 / 예약 취소 중 하나로 구분합니다. 자동 재시도는 기본 1분 간격, 최대 3회이며, CLI 없음과 로그인 만료는 재시도하지 않습니다.
- PC가 절전 중이라 예약 시간을 놓치면 PC가 깨어난 즉시 실행(기본) / 다음 갱신까지 대기 / 실행하지 않음 중 하나를 따릅니다. 깨어난 뒤에도 갱신 가능 여부를 다시 확인합니다. 앱이 실행 중이어야 동작합니다.
- 실행 프로필(`AIUsage.Core/Refresh/RefreshRunner.cs`)은 일반 작업과 공유하지 않는 전용 러너입니다. 앱 데이터 폴더의 빈 `refresh-workspace`에서 실행하고, 요청 문구는 stdin으로 전달합니다.
  - Claude: `claude -p --safe-mode --strict-mcp-config --tools "" --disable-slash-commands --no-session-persistence --effort low --output-format json --model haiku`
  - Codex: `codex exec --ephemeral --ignore-user-config --skip-git-repo-check --sandbox read-only -C <빈 폴더> -c model_reasoning_effort=low --json -`
- 서비스별 설정: 최소 비용 또는 사용자 지정 모델, 기본 요청(`hi`) 또는 사용자 지정 요청(40자 이하 평문)을 고를 수 있습니다. 새 CLI는 `IRefreshAdapter`를 구현해 추가합니다.
- 대시보드 개요 카드에 다음 갱신 가능 시각과 남은 시간, 예약 상태, [🔔 알림] [⏱ 예약 갱신] [다시 시도] 버튼이 있습니다. "갱신" 탭에서는 예약 목록, 사전 알림, 자동 재시도, 놓친 예약 정책, 실행 로그를 봅니다.
- 테스트: 환경변수 `USAGE_MONITOR_REFRESH_DRYRUN=ok|fail`을 지정하면 실제 CLI를 호출하지 않고 명령만 기록합니다.

## 수집 레벨 (Settings > Collection level)

| 레벨 | 동작 |
|---|---|
| Safe | 로컬 캐시와 수동 스냅샷만 사용합니다. 로그인 파일을 읽거나 네트워크 요청을 하지 않습니다. |
| Standard (기본) | Claude Code/Codex/Copilot CLI 로그인을 **읽기 전용**으로 재사용해 공식 사용량 조회 엔드포인트만 호출합니다. |
| Deep | Standard 기능에 Codex 세션 로그 fallback과 WSL 로그인 탐색을 더합니다. |

원칙:

- No Backend · No Telemetry · No API Key · Credential Local Only
- 인증 정보는 로컬에서 읽어 해당 Provider의 공식 도메인(`api.anthropic.com`, `chatgpt.com`, `api.github.com`)으로만 보냅니다. state/history/log에는 저장하지 않고, 토큰 갱신(rotate)도 하지 않습니다. 로그인이 만료되면 CLI를 한 번 실행하라고 안내합니다.
- 조회 엔드포인트는 모델 호출이 아닌 상태 조회라서 사용량을 소모하지 않습니다(`TOKEN-FREE`).
- Copilot의 `copilot_internal/user`는 Copilot IDE 확장이 한도 표시에 쓰는 비공개 엔드포인트입니다. 공식 문서화된 API가 아니므로 GitHub가 바꾸면 동작하지 않을 수 있습니다.
- 모델 호출이 필요한 경로(Claude Messages API rate-limit header)는 체인에 표시만 하며 자동으로 실행하지 않습니다(`UNVERIFIED`).
- Codex 세션 로그는 `rate_limits`가 들어 있는 줄만 파싱합니다.
- Claude 사용량 조회가 429(요청 과다)로 거절되면 로그인 만료로 처리하지 않고 `Retry-After`(없으면 5분, 최대 30분)만큼 기다렸다가 다시 조회합니다.

## 데이터 저장 위치

PowerShell MVP는 `%APPDATA%\UsageMonitor`, WPF 버전은 `%APPDATA%\UsageMonitorWpf`에 저장합니다. 권한이 없으면 앱 폴더의 `.usage-monitor`를 사용합니다.

```text
state.json       설정, 계정, 마지막 값, collector 상태 (schema v3, v2에서 자동 마이그레이션)
history.jsonl    30일 히스토리 (기존 history.json은 자동 이전 후 history.json.migrated로 보관)
```

## 아직 하지 않은 것

- 작업표시줄 내부 임베딩(현재는 작업표시줄 바로 위에 도킹), Theme/Layout Editor
- 코드 서명(Authenticode). 자동 업데이트는 지원하지만 exe 자체에 서명은 하지 않으며, 무결성은 릴리스의 SHA256 체크섬으로만 검증합니다. 릴리스는 `v*` 태그를 push하면 GitHub Actions가 `tools\publish-release.ps1`로 빌드해 자동으로 게시합니다.
- Claude Desktop 앱 로그인 재사용(암호화 저장소)

## 비제휴 및 상표 고지

AI Usage Monitor는 독립적으로 개발된 프로젝트이며 OpenAI, Anthropic 또는 GitHub과 제휴, 후원, 승인 관계가 아닙니다.

OpenAI, ChatGPT, Codex, Anthropic, Claude, GitHub, Copilot 및 관련 명칭과 상표는 각 권리자의 소유입니다.

## License

Free for personal and internal business use. Commercial resale and commercial redistribution are prohibited.

Non-commercial modification and redistribution are allowed under the license terms. See the `LICENSE` file for full terms.

Third-party components and bundled fonts are listed in `THIRD-PARTY-NOTICES.md`.
