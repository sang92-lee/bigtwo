// 브라우저끼리 바로 연결 (PeerJS, 무료 공개 서버). 방장 id = "bigtwo-방코드"
// C#으로는 SendMessage('BigTwo', 'OnNet', JSON) 하나로 알린다
mergeInto(LibraryManager.library, {
  NetInit: function () {
    if (window.BT) return;
    var BT = window.BT = { conns: {}, calls: {}, audios: {}, an: {}, peers: [], stream: null, peer: null };
    BT.emit = function (o) {
      var f = (typeof SendMessage === 'function') ? SendMessage : Module.SendMessage;
      f('BigTwo', 'OnNet', JSON.stringify(o));
    };
    BT.load = function (cb) {
      if (window.Peer) return cb();
      var s = document.createElement('script');
      s.src = 'https://unpkg.com/peerjs@1.5.4/dist/peerjs.min.js';
      s.onload = cb;
      s.onerror = function () { BT.emit({ t: 'error', msg: 'load' }); };
      document.head.appendChild(s);
    };
    BT.wire = function (c) {
      BT.conns[c.peer] = c;
      c.on('data', function (d) { BT.emit({ t: 'data', from: c.peer, msg: String(d) }); });
      c.on('close', function () { delete BT.conns[c.peer]; BT.emit({ t: 'leave', from: c.peer }); });
    };
    BT.attach = function (call) {
      call.on('stream', function (s) {
        var a = BT.audios[call.peer] || new Audio();
        a.autoplay = true; a.srcObject = s; a.play().catch(function () {});
        BT.audios[call.peer] = a;
        try {
          BT.ctx = BT.ctx || new (window.AudioContext || window.webkitAudioContext)();
          var an = BT.ctx.createAnalyser(); an.fftSize = 256;
          BT.ctx.createMediaStreamSource(s).connect(an);
          BT.an[call.peer] = an;
        } catch (e) {}
      });
      call.on('close', function () { delete BT.calls[call.peer]; });
    };
    BT.callAll = function () {
      if (!BT.stream || !BT.peer) return;
      BT.peers.forEach(function (id) {
        if (BT.calls[id]) return;
        var call = BT.peer.call(id, BT.stream);
        if (!call) return;
        BT.calls[id] = call; BT.attach(call);
      });
    };
    BT.onPeer = function (p) {
      BT.peer = p;
      p.on('call', function (call) { call.answer(BT.stream || undefined); BT.attach(call); });
      p.on('error', function (e) { BT.emit({ t: 'error', msg: e.type || 'error' }); });
    };
    // 말하는 사람 표시용 소리 크기
    setInterval(function () {
      var ids = [], vs = [], buf = new Uint8Array(128);
      for (var id in BT.an) {
        BT.an[id].getByteTimeDomainData(buf);
        var sum = 0; for (var i = 0; i < buf.length; i++) { var x = (buf[i] - 128) / 128; sum += x * x; }
        ids.push(id); vs.push(Math.sqrt(sum / buf.length));
      }
      if (ids.length) BT.emit({ t: 'lv', ids: ids, vs: vs });
    }, 200);
  },

  NetHost: function (codePtr) {
    var code = UTF8ToString(codePtr), BT = window.BT;
    BT.load(function () {
      var p = new Peer('bigtwo-' + code);
      BT.onPeer(p);
      p.on('open', function (id) { BT.emit({ t: 'ready', id: id }); });
      p.on('connection', function (c) { BT.wire(c); });
    });
  },

  NetJoin: function (codePtr) {
    var code = UTF8ToString(codePtr), BT = window.BT;
    BT.load(function () {
      var p = new Peer();
      BT.onPeer(p);
      p.on('open', function (id) {
        var c = p.connect('bigtwo-' + code, { reliable: true });
        BT.wire(c);
        c.on('open', function () { BT.emit({ t: 'ready', id: id }); });
      });
    });
  },

  NetSend: function (toPtr, msgPtr) {
    var c = window.BT && window.BT.conns[UTF8ToString(toPtr)];
    if (c && c.open) c.send(UTF8ToString(msgPtr));
  },

  NetSetPeers: function (csvPtr) {
    var BT = window.BT; if (!BT) return;
    var s = UTF8ToString(csvPtr);
    BT.peers = s ? s.split(',') : [];
    BT.callAll();
  },

  NetMic: function (on) {
    var BT = window.BT; if (!BT) return;
    if (BT.ctx && BT.ctx.state === 'suspended') BT.ctx.resume();
    if (on) {
      navigator.mediaDevices.getUserMedia({ audio: true }).then(function (s) {
        BT.stream = s; BT.callAll(); BT.emit({ t: 'mic', on: true });
      }).catch(function () { BT.emit({ t: 'mic', on: false, msg: 'denied' }); });
    } else {
      if (BT.stream) BT.stream.getTracks().forEach(function (t) { t.stop(); });
      BT.stream = null;
      for (var id in BT.calls) BT.calls[id].close();
      BT.calls = {};
      BT.emit({ t: 'mic', on: false });
    }
  },

  NetLeave: function () {
    var BT = window.BT; if (!BT) return;
    if (BT.stream) BT.stream.getTracks().forEach(function (t) { t.stop(); });
    for (var id in BT.audios) BT.audios[id].srcObject = null;
    if (BT.peer) BT.peer.destroy();
    BT.conns = {}; BT.calls = {}; BT.audios = {}; BT.an = {}; BT.peers = []; BT.stream = null; BT.peer = null;
  },

  NetReload: function () { location.reload(); },

  NetRoomParam: function () {
    var s = new URLSearchParams(location.search).get('room') || '';
    var n = lengthBytesUTF8(s) + 1, b = _malloc(n); stringToUTF8(s, b, n); return b;
  },

  NetLink: function (codePtr) {
    var s = location.origin + location.pathname + '?room=' + UTF8ToString(codePtr);
    var n = lengthBytesUTF8(s) + 1, b = _malloc(n); stringToUTF8(s, b, n); return b;
  },

  NetCopy: function (textPtr) {
    var t = UTF8ToString(textPtr);
    if (navigator.clipboard) navigator.clipboard.writeText(t).catch(function () { window.prompt('링크 복사', t); });
    else window.prompt('링크 복사', t);
  },

  // 이름·방 코드 입력창 (prompt()는 앱 안 브라우저에서 막힐 수 있어서 직접 그린다)
  NetAsk: function (keyPtr, titlePtr, defPtr) {
    var key = UTF8ToString(keyPtr), BT = window.BT, title = UTF8ToString(titlePtr), def = UTF8ToString(defPtr);
    try {
    var old = document.getElementById('bt-ask'); if (old) old.remove();
    var d = document.createElement('div'); d.id = 'bt-ask';
    d.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.6);display:flex;align-items:center;justify-content:center;z-index:9999;font-family:sans-serif';
    d.innerHTML = '<div style="background:#1a1412;border:1px solid #8a3a24;border-radius:12px;padding:20px;width:min(320px,86vw);color:#eee">' +
      '<div class="t" style="margin-bottom:12px;font-size:17px"></div>' +
      '<input maxlength="8" style="width:100%;box-sizing:border-box;font-size:20px;padding:10px;border-radius:8px;border:1px solid #555;background:#0d0b0a;color:#fff">' +
      '<div style="display:flex;gap:8px;margin-top:14px"><button style="flex:1;padding:10px;font-size:16px;border-radius:8px;border:0;background:#333;color:#ccc">취소</button>' +
      '<button style="flex:1;padding:10px;font-size:16px;border-radius:8px;border:0;background:#c8613f;color:#fff">확인</button></div></div>';
    document.body.appendChild(d);
    d.querySelector('.t').textContent = title;
    var inp = d.querySelector('input'); inp.value = def;
    var bs = d.querySelectorAll('button');
    function done(v) { d.remove(); BT.emit({ t: 'ask', id: key, msg: v }); }
    bs[0].onclick = function () { done(''); };
    bs[1].onclick = function () { done(inp.value.trim()); };
    inp.onkeydown = function (e) { e.stopPropagation(); if (e.key === 'Enter') done(inp.value.trim()); };
    setTimeout(function () { inp.focus(); }, 50);
    } catch (e) { var v = window.prompt ? (function(){ try { return window.prompt(title, def); } catch (x) { return ''; } })() : ''; BT.emit({ t: 'ask', id: key, msg: v || '' }); }
  }
});
