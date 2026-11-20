// 해기 상황판 데이터: public/ 안의 파일을 그대로 주고, .json은 앱에서 읽을 수 있게 열어 둔다
export default {
  async fetch(req, env) {
    const res = await env.ASSETS.fetch(req);
    if (new URL(req.url).pathname.endsWith('.json')) {
      const h = new Headers(res.headers);
      h.set('Access-Control-Allow-Origin', '*');
      h.set('Cache-Control', 'no-cache');
      return new Response(res.body, {status: res.status, headers: h});
    }
    return res;
  },
};
