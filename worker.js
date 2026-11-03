// 정적 파일은 그대로, /api/yt?q= 만 유튜브 검색 결과를 골라서 돌려준다 (코노용)
export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    if (url.pathname !== '/api/yt') return env.ASSETS.fetch(req);
    const q = (url.searchParams.get('q') || '').slice(0, 80);
    if (!q) return json([]);
    const r = await fetch('https://www.youtube.com/results?hl=ko&gl=KR&search_query=' + encodeURIComponent(q), {
      headers: { 'Accept-Language': 'ko-KR,ko;q=0.9', 'User-Agent': 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36' }
    });
    const html = await r.text();
    const m = html.match(/var ytInitialData = (\{.*?\});<\/script>/s);
    const out = [];
    if (m) {
      const walk = o => {
        if (!o || typeof o !== 'object' || out.length >= 12) return;
        if (o.videoRenderer && o.videoRenderer.videoId) {
          const v = o.videoRenderer;
          out.push({ id: v.videoId, title: v.title?.runs?.[0]?.text || '', ch: v.ownerText?.runs?.[0]?.text || '', len: v.lengthText?.simpleText || '' });
          return;
        }
        for (const k in o) walk(o[k]);
      };
      walk(JSON.parse(m[1]));
    }
    return json(out);
  }
};
const json = d => new Response(JSON.stringify(d), { headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'public, max-age=3600' } });
