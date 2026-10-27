// 홈 화면 앱으로 설치되게 하는 최소 서비스 워커. 캐시는 안 함 (항상 최신 버전)
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => {});
