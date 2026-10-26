# 빅 투 (Big Two) — Unity

어두운 방의 원탁에 앉아 1인칭으로 하는 빅투 카드게임. 지금은 봇과 함께 혼자 하는 버전.

- 플레이: https://bigtwo.dltkdrndl92.workers.dev (GitHub에 올리면 Cloudflare가 자동 배포)
- 인원 2~4명 (2명은 마주 보기, 3명은 둥글게), 카드 1팩/2팩(조커 포함) 선택
- 서열: 3 < … < A < 2 < 조커, 무늬 ♦ < ♣ < ♥ < ♠
- 조합: 싱글, 페어, 트리플, 5장(스트레이트 < 플러시 < 풀하우스 < 포카드 < 스트레이트 플러시 < 파이브카드)
- 9장씩 나눠 시작, 남은 카드는 더미. 패스할 때마다 더미에서 1장 먹기
- 내 턴 20초, 넘기면 자동 패스. 낼 수 있는 카드만 밝게 표시, 추천 버튼
- 나머지가 모두 패스하면 마지막에 낸 사람이 새로 시작
- 1등이 나오면 판 끝. 점수 = 남은 카드 수 × (남은 2나 조커 한 장마다 2배), 낮을수록 좋음

## 파일
- `Assets/Scripts/BigTwoRules.cs` — 카드, 조합 판정, 봇, 점수
- `Assets/Scripts/BigTwoGame.cs` — 장면(원탁·조명·실루엣)과 화면(OnGUI)을 코드로 만듦
- `Assets/Editor/WebBuild.cs` — 메뉴 Tools → 웹 빌드
- `Assets/Editor/McpAutoStart.cs` — 에디터가 켜지면 Unity MCP 브리지(stdio) 자동 시작

## 배포
Unity 메뉴 Tools → 웹 빌드 → `./deploy.sh "변경 내용"` (GitHub에 올리면 Cloudflare가 자동 배포)

3D 모델: Poly Haven CC0 (`Assets/Resources/PolyHaven/CREDITS.txt`)

## 다음
- 온라인 방(방 코드·링크로 접속, 대기실에서 시작) + 마이크: WebGL에서 PeerJS(WebRTC) jslib 연동
