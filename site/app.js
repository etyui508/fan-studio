const status = document.querySelector('#api-status');
const dot = document.querySelector('#api-dot');

fetch('https://fan-api.bony-doorframe-shortly.top/v1/health', { cache: 'no-store' })
  .then(r => {
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return r.json();
  })
  .then(data => {
    status.textContent = 'API ' + (data.version || 'online');
    dot.classList.add('ok');
  })
  .catch(() => {
    status.textContent = 'API 暂不可用';
  });
