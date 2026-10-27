#!/bin/zsh
# 빌드 결과(Builds/WebGL)를 GitHub sang92-lee/bigtwo 에 올린다 → Cloudflare가 자동 배포
# 사용: ./deploy.sh "변경 내용"   (먼저 Unity 메뉴 Tools → 웹 빌드)
set -e
cd "$(dirname "$0")"
MSG=${1:-"웹 빌드 갱신"}
REPO=~/claude-projects/bigtwo-repo
AUTH="Authorization: basic $(printf 'x-access-token:%s' "$(cat ~/.github_token)" | base64)"

[ -d $REPO/.git ] || git -c http.extraHeader="$AUTH" clone -q https://github.com/sang92-lee/bigtwo.git $REPO
git -C $REPO -c http.extraHeader="$AUTH" pull -q

# 게임 파일 (public → Cloudflare 정적 파일)
rm -rf $REPO/public && cp -R Builds/WebGL $REPO/public
sed -i '' -e 's|<title>.*</title>|<title>빅 투</title>|' \
  -e 's|style.width = "960px"|style.width = "min(96vw, 1280px)"|' \
  -e 's|style.height = "600px"|style.height = "min(86vh, 800px)"|' $REPO/public/index.html
# 올릴 때마다 파일 주소에 버전을 붙여서 폰이 옛 빌드를 기억하지 않게
STAMP=$(date +%s)
sed -i '' -E "s#(\.unityweb|\.loader\.js)\"#\1?v=$STAMP\"#g" $REPO/public/index.html

# 홈 화면에 추가(앱처럼 설치): 아이콘·manifest·서비스 워커
cp web/* $REPO/public/
# 자동 업데이트: 열 때 서버의 최신 번호와 비교해서 다르면 스스로 새로 불러옴
echo $STAMP > $REPO/public/version.txt
export STAMP
python3 - $REPO/public/index.html <<'P'
import sys
f=sys.argv[1]; s=open(f).read()
head = ('<link rel="manifest" href="manifest.webmanifest">'
        '<meta name="theme-color" content="#090809">'
        '<link rel="apple-touch-icon" href="icon-192.png">'
        '<meta name="apple-mobile-web-app-capable" content="yes">'
        '<meta name="apple-mobile-web-app-title" content="빅 투">')
# 카톡 안 브라우저면 바로 폰 기본 브라우저로 다시 연다 (설치·마이크가 거기서만 됨)
import os
stamp = os.environ.get("STAMP", "0")
upd = ("<script>(function(){var mine='" + stamp + "';"
       "fetch('version.txt?t='+Date.now(),{cache:'no-store'}).then(function(r){return r.text()}).then(function(v){v=v.trim();"
       "if(v&&v!==mine){var q=new URLSearchParams(location.search);q.set('v',v);location.replace(location.pathname+'?'+q.toString());}}).catch(function(){});})();</script>")
head = upd + ("<script>if(/KAKAOTALK/i.test(navigator.userAgent)){location.href='kakaotalk://web/openExternal?url='+encodeURIComponent(location.href);}</script>") + head
s = s.replace('<link rel="shortcut icon" href="TemplateData/favicon.ico">', '<link rel="icon" href="icon-192.png">' + head)
s = s.replace('</body>', "<script>if('serviceWorker' in navigator) navigator.serviceWorker.register('sw.js');"
              "addEventListener('beforeinstallprompt',function(e){e.preventDefault();window.BTinstall=e;});</script></body>")
open(f,'w').write(s)
P

# 소스
mkdir -p $REPO/unity
rsync -a --delete --exclude '*.meta' --exclude 'Screenshots' Assets/Scripts Assets/Editor Assets/Plugins web $REPO/unity/
cp Packages/manifest.json $REPO/unity/manifest.json
cp README.md $REPO/README.md
cp deploy.sh $REPO/unity/deploy.sh
cat > $REPO/wrangler.toml <<'T'
name = "bigtwo"
compatibility_date = "2026-09-01"

# Unity WebGL 빌드 파일만 그대로 보여주는 정적 사이트
[assets]
directory = "./public"
T

cd $REPO
git add -A
git -c user.name="sang92-lee" -c user.email="dltkdrndl92@gmail.com" commit -q -m "$MSG

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" || { echo "바뀐 것 없음"; exit 0; }
git -c http.extraHeader="$AUTH" push -q
echo "올림: https://github.com/sang92-lee/bigtwo"
