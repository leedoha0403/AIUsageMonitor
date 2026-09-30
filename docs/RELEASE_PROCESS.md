# 릴리스 프로세스

AI Usage Monitor(`leedoha0403/AIUsageMonitor`)의 커밋 → 푸시 → 릴리스 노트 → 릴리스 배포 절차입니다.
GitHub Release는 `v*` 태그를 push하면 CI가 자동으로 만들므로, 사람이 하는 일은 아래 1~7단계입니다.

## 1. 작업 상태 확인

```powershell
git status --short
git log --oneline -5
```

- 커밋되지 않은 변경이 이번 릴리스에 넣을 작업인지 확인합니다. 의도하지 않은 로컬 파일(`dist/`, `.claude/settings.local.json` 등)은 포함하지 않습니다.
- 마지막 태그 이후 `main`에 쌓인 커밋은 모두 이번 릴리스에 포함됩니다.

## 2. 버전 결정과 변경

- 수정·소규모 조정은 **패치**(0.9.1 → 0.9.2), 의미 있는 기능은 **마이너**(0.9.x → 0.10.0)를 올립니다. 버전을 직접 지정받으면 그대로 따릅니다.
- `UsageMonitorWpf/UsageMonitorWpf.csproj`의 네 필드를 함께 올립니다.

```xml
<Version>X.Y.Z</Version>
<AssemblyVersion>X.Y.Z.0</AssemblyVersion>
<FileVersion>X.Y.Z.0</FileVersion>
<InformationalVersion>X.Y.Z-internal</InformationalVersion>
```

> 빌드 시 .NET이 `InformationalVersion`에 `+<커밋 sha>`를 붙입니다. 버전을 파싱하는 코드(`UpdateChecker`)는 첫 `-` 또는 `+` 앞까지만 사용해야 합니다.

## 3. 빌드·테스트 확인

```powershell
.\.dotnet\dotnet.exe build .\AIUsage.sln -c Debug
.\.dotnet\dotnet.exe test .\tests\AIUsage.Tests
```

경고 0개, 오류 0개, 테스트 전부 통과여야 다음 단계로 갑니다.

## 4. 릴리스 노트 작성

`release-notes/vX.Y.Z.md`를 만듭니다. CI가 이 파일을 GitHub Release 본문으로 씁니다(없으면 자동 생성 노트). 직전 노트(`release-notes/v0.9.2.md`)를 복사해 아래 구조로 씁니다.

```markdown
## vX.Y.Z

### 수정          ← 사용자에게 보이는 변경 (없으면 "### 추가" 등으로 대체)
### 내부 변경      ← 리팩터링·테스트 등 (선택)
### 설치 · 업데이트
### 첨부 파일      ← AIUsageMonitor.exe / AIUsageMonitor-vX.Y.Z-win-x64.zip / SHA256SUMS.txt
```

- `AIUsageMonitor.exe`는 자동 업데이트가 이름으로 찾으므로 첨부 파일명을 바꾸지 않습니다.
- zip은 0.8.0 이하 클라이언트가 `.zip` 자산을 찾기 때문에 계속 게시합니다.

## 5. 커밋·태그·푸시

파일을 지정해서 스테이징합니다(`git add -A` 금지).

```powershell
git add -u
git add <새로 만든 파일들> release-notes\vX.Y.Z.md
git commit -m "X.Y.Z: 변경 요약 한 줄 (한국어)"
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin main
git push origin vX.Y.Z
```

- 커밋 메시지는 저장소 기존 스타일(한국어 한 줄 요약)을 따릅니다.
- 태그 push가 릴리스 CI를 시작하므로, 커밋이 모두 푸시된 뒤에 태그를 올립니다.

## 6. 위키 Changelog 갱신

위키는 별도 저장소(`https://github.com/leedoha0403/AIUsageMonitor.wiki.git`, 브랜치 `master`)입니다. 프로젝트 밖의 임시 폴더에 clone합니다.

1. `Home.md`의 `# 📜 Changelog` 바로 아래(최신순)에 `## vX.Y.Z` 항목을 추가합니다.
2. 이번 릴리스로 끝나는 항목이 있으면 `# 🗺️ Roadmap` 체크리스트를 갱신합니다.
3. 커밋 후 푸시합니다.

```bash
git commit -am "vX.Y.Z: 변경 요약"
git push origin master
```

## 7. README·문서 동기화

사용자에게 보이는 기능, 실행 파일 이름, 옵션이 바뀌었다면 `README.md`도 같은 작업에서 고칩니다. 릴리스 태그 이후에 문서만 고친 커밋은 해당 릴리스 빌드에 포함되지 않습니다.

## 8. 릴리스 배포 (자동)

태그 push 후 `.github/workflows/release.yml`이 Windows 러너에서 `tools/publish-release.ps1`을 실행해 아래를 GitHub Release에 첨부합니다.

- `AIUsageMonitor.exe`
- `AIUsageMonitor-vX.Y.Z-win-x64.zip`
- `SHA256SUMS.txt`

확인 방법:

```bash
curl -s https://api.github.com/repos/leedoha0403/AIUsageMonitor/actions/runs?per_page=1
curl -s https://api.github.com/repos/leedoha0403/AIUsageMonitor/releases/latest
```

- 실행의 `conclusion`이 `success`이고, 릴리스에 자산 3개가 있는지 봅니다.
- 앱의 자동 업데이트(`SelfUpdater`)는 `SHA256SUMS.txt`에 `AIUsageMonitor.exe` 항목이 있어야 검증을 통과합니다.

### 로컬 사전 점검 (선택)

CI와 같은 패키징을 로컬에서 미리 확인할 때만 씁니다. 결과물(`dist/`)은 커밋하지 않습니다.

```powershell
.\tools\publish-release.ps1 -Version X.Y.Z
(Get-Item "dist\release\X.Y.Z\win-x64\AIUsageMonitor.exe").VersionInfo.ProductVersion
```

## 9. ModuleDock Host 배포 (위젯)

위젯(`AIUsage.Widget`)을 ModuleDock Host에 반영하려면 다음을 실행하고 Host를 재시작합니다. GitHub 릴리스와는 별개입니다.

```powershell
.\tools\deploy-to-host.ps1
# 다른 Host 경로: .\tools\deploy-to-host.ps1 -HostDir <경로> -Configuration Debug
```

## 체크리스트

- [ ] 변경 사항 확인, 불필요한 파일 제외
- [ ] csproj 버전 4개 필드 갱신
- [ ] 빌드 경고/오류 0, 테스트 통과
- [ ] `release-notes/vX.Y.Z.md` 작성
- [ ] 커밋 → `git tag` → `main`·태그 푸시
- [ ] 위키 Changelog 갱신·푸시
- [ ] README 동기화
- [ ] Actions 성공, Release 자산 3개 확인
- [ ] (필요 시) `deploy-to-host.ps1`로 Host 배포
