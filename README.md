# Usage Monitor

Claude Code와 Codex의 사용량, 리셋 시간, 데이터 출처, 신뢰도를 보는 Windows 데스크톱 앱입니다. 목표는 단순 사용량 표시기가 아니라 **AI Coding Usage Control Center**입니다. 평소에는 작업표시줄 칩 몇 픽셀만 쓰고, 필요할 때 값의 출처, 사용 패턴, Reset Timeline까지 확인합니다.

## 실행

WPF 버전은 `Start Usage Monitor WPF.cmd`를 더블클릭하거나 아래 명령으로 실행하세요.

```powershell
.\.dotnet\dotnet.exe run --project .\UsageMonitorWpf\UsageMonitorWpf.csproj
```

PowerShell MVP도 남겨두었습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\UsageMonitor.ps1
```

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
- 폰트: Spoqa Han Sans Neo를 내장했습니다(SIL OFL 1.1, `UsageMonitorWpf/Fonts`). subset Regular 파일의 메타데이터가 Bold로 표시되어 있어 Regular와 Medium/Bold를 별도 패밀리로 나눠 사용합니다.
- 다크 테마: 메뉴, 툴팁, 슬라이더, 체크박스, 스크롤바, 트레이 메뉴, 창 제목 표시줄까지 테마를 따릅니다.
- Windows 시작 시 자동 실행: 설정 탭 맨 위 또는 트레이 메뉴에서 켭니다. 현재 사용자 시작 프로그램(`HKCU\...\Run`, 관리자 권한 불필요)에 `UsageMonitorWpf.exe --startup`으로 등록합니다. 로그인 시에는 대시보드를 띄우지 않고 트레이와 칩(미니 모드면 위젯)으로 조용히 시작합니다. 실행 파일 위치가 바뀌면 다음 실행 때 등록 경로를 자동으로 갱신하며, 작업 관리자에서 끈 상태도 그대로 반영됩니다.
- 아이콘: exe, 창 제목 표시줄, 트레이, 알림, 대시보드 및 위젯 헤더에 같은 앱 아이콘을 씁니다. `tools/make-icons.ps1 -Source <원본 이미지>`로 `UsageMonitorWpf/Assets/AppIcon.ico/.png`를 다시 만들 수 있습니다. 40px 이상은 원본 아트워크를 사용하고, 32px 이하(트레이 등)는 선명하도록 같은 디자인을 벡터로 다시 그립니다.
- 한국어/English: UI, 트레이 메뉴, 상태 메시지, 진단, 알림 전체를 번역했으며 언어를 바꾸면 즉시 반영됩니다(`Core/Loc.cs`).

## 기능

- **Collector Fallback 체인**: 계정마다 우선순위대로 실행하고, 필드 단위로 병합합니다. 각 필드는 먼저 값을 준 Collector가 소유합니다.
  - Claude: `Official`(OAuth usage endpoint) → `RateLimit`(비활성) → `Local`
  - Codex: `Official`(ChatGPT usage endpoint) → `SessionLog`(로컬 세션 로그의 rate_limits) → `Local`
- **Field-level Source / Confidence**: 5H 사용량, 5H 리셋, 주간 사용량, 주간 리셋, Plan마다 출처, 신뢰도, 갱신 시각을 기록합니다.
- **Multi Account**: 여러 계정을 동시에 모니터링합니다. 추가 계정은 각자의 CLI 설정 폴더(`CLAUDE_CONFIG_DIR` / `CODEX_HOME`)를 지정합니다. 계정별 활성화/비활성화가 가능하고, Accounts 탭에서 계정끼리 비교할 수 있습니다.
- **Credential 탐색**: 사용자 지정 경로 → 환경변수 → Windows CLI(`~/.claude`, `~/.codex`) → WSL(Deep)
- **Not signed in 처리**: 로그인이 없거나 만료된 Provider는 회색으로 표시하고 로그인 안내를 보여줍니다. 나머지 Provider는 정상 동작합니다.
- **Model breakdown / Extra Usage**: Claude Opus/Sonnet 주간 사용량, 유료 Extra usage, Codex credits(Dashboard 전용)
- **Usage History**: 로컬 `history.jsonl`에 30일 보관합니다(값이 바뀌었거나 5분이 지났을 때만 기록). 1H / 6H / 1D / 7D / 30D 그래프를 제공합니다.
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
- 실행 프로필(`Refresh/RefreshRunner.cs`)은 일반 작업과 공유하지 않는 전용 러너입니다. 앱 데이터 폴더의 빈 `refresh-workspace`에서 실행하고, 요청 문구는 stdin으로 전달합니다.
  - Claude: `claude -p --safe-mode --strict-mcp-config --tools "" --disable-slash-commands --no-session-persistence --effort low --output-format json --model haiku`
  - Codex: `codex exec --ephemeral --ignore-user-config --skip-git-repo-check --sandbox read-only -C <빈 폴더> -c model_reasoning_effort=low --json -`
- 서비스별 설정: 최소 비용 또는 사용자 지정 모델, 기본 요청(`hi`) 또는 사용자 지정 요청(40자 이하 평문)을 고를 수 있습니다. 새 CLI는 `IRefreshAdapter`를 구현해 추가합니다.
- 대시보드 개요 카드에 다음 갱신 가능 시각과 남은 시간, 예약 상태, [🔔 알림] [⏱ 예약 갱신] [다시 시도] 버튼이 있습니다. "갱신" 탭에서는 예약 목록, 사전 알림, 자동 재시도, 놓친 예약 정책, 실행 로그를 봅니다.
- 테스트: 환경변수 `USAGE_MONITOR_REFRESH_DRYRUN=ok|fail`을 지정하면 실제 CLI를 호출하지 않고 명령만 기록합니다.

## 수집 레벨 (Settings > Collection level)

| 레벨 | 동작 |
|---|---|
| Safe | 로컬 캐시와 수동 스냅샷만 사용합니다. 로그인 파일을 읽거나 네트워크 요청을 하지 않습니다. |
| Standard (기본) | Claude Code/Codex CLI 로그인을 **읽기 전용**으로 재사용해 공식 사용량 조회 엔드포인트만 호출합니다. |
| Deep | Standard 기능에 Codex 세션 로그 fallback과 WSL 로그인 탐색을 더합니다. |

원칙:

- No Backend · No Telemetry · No API Key · Credential Local Only
- 인증 정보는 로컬에서 읽어 해당 Provider의 공식 도메인(`api.anthropic.com`, `chatgpt.com`)으로만 보냅니다. state/history/log에는 저장하지 않고, 토큰 갱신(rotate)도 하지 않습니다. 로그인이 만료되면 CLI를 한 번 실행하라고 안내합니다.
- 조회 엔드포인트는 모델 호출이 아닌 상태 조회라서 사용량을 소모하지 않습니다(`TOKEN-FREE`).
- 모델 호출이 필요한 경로(Claude Messages API rate-limit header)는 체인에 표시만 하며 자동으로 실행하지 않습니다(`UNVERIFIED`).
- Codex 세션 로그는 `rate_limits`가 들어 있는 줄만 파싱합니다.

## 데이터 저장 위치

PowerShell MVP는 `%APPDATA%\UsageMonitor`, WPF 버전은 `%APPDATA%\UsageMonitorWpf`에 저장합니다. 권한이 없으면 앱 폴더의 `.usage-monitor`를 사용합니다.

```text
state.json       설정, 계정, 마지막 값, collector 상태 (schema v3, v2에서 자동 마이그레이션)
history.jsonl    30일 히스토리 (기존 history.json은 자동 이전 후 history.json.migrated로 보관)
```

## 아직 하지 않은 것

- 작업표시줄 내부 임베딩(현재는 작업표시줄 바로 위에 도킹), Theme/Layout Editor
- 릴리스 SHA256/서명 검증 설치기와 자동 업데이트(현재 배포 파이프라인 없음)
- Claude Desktop 앱 로그인 재사용(암호화 저장소)
