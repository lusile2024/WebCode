/**
 * WebCode PWA Service Worker
 * 提供离线缓存和后台同步功能
 */

const CACHE_NAME = 'webcode-pwa-v0.2.4';
const STATIC_CACHE_NAME = 'webcode-static-v0.2.4';
const DYNAMIC_CACHE_NAME = 'webcode-dynamic-v0.2.4';

// 需要预缓存的静态资源
const STATIC_ASSETS = [
  '/',
  '/m/code-assistant',
  '/mobile/code-assistant',
  '/index.html',
  '/manifest.json',
  '/css/output.css',
  '/css/site.css',
  '/css/mobile-responsive.css',
  '/css/prism-tomorrow.min.css',
  '/js/main.js',
  '/js/code-assistant-helper.js',
  '/js/textarea-resize.js',
  '/js/localization-helper.js',
  '/js/prism.min.js',
  '/images/logo.png',
  '/images/icons/icon-192x192.png',
  '/images/icons/icon-512x512.png',
  '/favicon.ico'
];

// 需要网络优先的API路径
const NETWORK_FIRST_PATHS = [
  '/api/',
  '/_blazor',
  '/_framework',
  '/Resources/Localization/'
];

// 安装事件：预缓存静态资源
self.addEventListener('install', (event) => {
  console.log('[ServiceWorker] 安装中...');
  
  event.waitUntil(
    caches.open(STATIC_CACHE_NAME)
      .then((cache) => {
        console.log('[ServiceWorker] 预缓存静态资源');
        // 使用Promise.allSettled来避免单个资源失败导致整体失败
        return Promise.allSettled(
          STATIC_ASSETS.map(url => 
            cache.add(url).catch(err => {
              console.warn(`[ServiceWorker] 无法缓存: ${url}`, err);
            })
          )
        );
      })
      .then(() => {
        console.log('[ServiceWorker] 静态资源缓存完成');
        return self.skipWaiting();
      })
      .catch((error) => {
        console.error('[ServiceWorker] 安装失败:', error);
      })
  );
});

// 激活事件：清理旧缓存
self.addEventListener('activate', (event) => {
  console.log('[ServiceWorker] 激活中...');
  
  event.waitUntil(
    caches.keys()
      .then((cacheNames) => {
        return Promise.all(
          cacheNames
            .filter((cacheName) => {
              return cacheName !== STATIC_CACHE_NAME && 
                     cacheName !== DYNAMIC_CACHE_NAME &&
                     cacheName.startsWith('webcode-');
            })
            .map((cacheName) => {
              console.log('[ServiceWorker] 删除旧缓存:', cacheName);
              return caches.delete(cacheName);
            })
        );
      })
      .then(() => {
        console.log('[ServiceWorker] 激活完成');
        return self.clients.claim();
      })
  );
});

// 判断是否是需要网络优先的请求
function isNetworkFirstRequest(url) {
  return NETWORK_FIRST_PATHS.some(path => url.pathname.includes(path));
}

// 判断是否是可缓存的请求
function isCacheableRequest(request) {
  const url = new URL(request.url);
  
  // 只缓存GET请求
  if (request.method !== 'GET') return false;
  
  // 不缓存跨域请求
  if (url.origin !== location.origin) return false;
  
  // 不缓存API请求
  if (url.pathname.startsWith('/api/')) return false;
  
  // 不缓存Blazor框架请求
  if (url.pathname.startsWith('/_blazor') || url.pathname.startsWith('/_framework')) return false;
  
  return true;
}

// 网络优先策略
async function networkFirst(request) {
  try {
    const response = await fetch(request);
    if (response.ok && request.mode !== 'navigate' && isCacheableRequest(request)) {
      const cache = await caches.open(DYNAMIC_CACHE_NAME);
      await cache.put(request, response.clone());
    }
    return response;
  } catch (error) {
    const cachedResponse = await caches.match(request);
    if (cachedResponse) {
      return cachedResponse;
    }
    throw error;
  }
}

// 缓存优先策略（带网络回退）
async function cacheFirst(request) {
  const cachedResponse = await caches.match(request);
  if (cachedResponse) {
    return cachedResponse;
  }
  
  try {
    const response = await fetch(request);
    
    // 缓存成功的响应
    if (response.ok && isCacheableRequest(request)) {
      const cache = await caches.open(DYNAMIC_CACHE_NAME);
      cache.put(request, response.clone());
    }
    
    return response;
  } catch (error) {
    // 如果是导航请求，返回离线页面
    if (request.mode === 'navigate') {
      const offlineResponse = await caches.match('/index.html');
      if (offlineResponse) {
        return offlineResponse;
      }
    }
    throw error;
  }
}

// 过期刷新策略（适用于静态资源）
async function staleWhileRevalidate(request) {
  const cache = await caches.open(STATIC_CACHE_NAME);
  const cachedResponse = await cache.match(request);
  
  const fetchPromise = fetch(request).then((response) => {
    if (response.ok) {
      cache.put(request, response.clone());
    }
    return response;
  }).catch(() => cachedResponse);
  
  return cachedResponse || fetchPromise;
}

// 拦截请求
self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  
  // 跳过非同源请求
  if (url.origin !== location.origin) {
    return;
  }
  
  // 网络优先的请求
  if (isNetworkFirstRequest(url)) {
    event.respondWith(networkFirst(event.request));
    return;
  }
  
  // 导航请求使用网络优先
  if (event.request.mode === 'navigate') {
    event.respondWith(networkFirst(event.request));
    return;
  }
  
  // 静态资源使用缓存优先
  event.respondWith(cacheFirst(event.request));
});

// 接收来自页面的消息
self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
  
  if (event.data && event.data.type === 'GET_VERSION') {
    event.ports[0].postMessage({ version: CACHE_NAME });
  }
});

// 推送通知
self.addEventListener('push', (event) => {
  if (!event.data) return;
  
  try {
    const data = event.data.json();
    const options = {
      body: data.body || '您有新消息',
      icon: '/images/icons/icon-192x192.png',
      badge: '/images/icons/icon-72x72.png',
      vibrate: [100, 50, 100],
      data: {
        url: data.url || '/m/code-assistant'
      },
      actions: [
        {
          action: 'open',
          title: '打开'
        },
        {
          action: 'close',
          title: '关闭'
        }
      ]
    };
    
    event.waitUntil(
      self.registration.showNotification(data.title || 'WebCode', options)
    );
  } catch (error) {
    console.error('[ServiceWorker] 推送通知解析失败:', error);
  }
});

// 通知点击事件
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  
  if (event.action === 'close') {
    return;
  }
  
  const url = event.notification.data?.url || '/m/code-assistant';
  
  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true })
      .then((clientList) => {
        // 查找已打开的窗口
        for (const client of clientList) {
          if (client.url.includes('/m/code-assistant') && 'focus' in client) {
            return client.focus();
          }
        }
        // 否则打开新窗口
        if (clients.openWindow) {
          return clients.openWindow(url);
        }
      })
  );
});

// 后台同步
self.addEventListener('sync', (event) => {
  if (event.tag === 'sync-sessions') {
    event.waitUntil(syncSessions());
  }
});

async function syncSessions() {
  console.log('[ServiceWorker] 后台同步会话数据...');
  // 这里可以实现会话数据的后台同步逻辑
}

console.log('[ServiceWorker] 脚本加载完成');
