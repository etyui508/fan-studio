const status = document.querySelector('#api-status');
const dot = document.querySelector('#api-dot');
const isChinese = document.documentElement.lang.toLowerCase().startsWith('zh');
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

const progress = document.createElement('div');
progress.id = 'scroll-progress';
document.body.appendChild(progress);

const nav = document.querySelector('.nav');
let scrollQueued = false;
function updateScrollMotion() {
  const max = document.documentElement.scrollHeight - window.innerHeight;
  progress.style.width = (max > 0 ? Math.min(100, window.scrollY / max * 100) : 0) + '%';
  nav.classList.toggle('scrolled', window.scrollY > 18);
  scrollQueued = false;
}
window.addEventListener('scroll', () => {
  if (scrollQueued) return;
  scrollQueued = true;
  requestAnimationFrame(updateScrollMotion);
}, { passive: true });
updateScrollMotion();

if (!reducedMotion && 'IntersectionObserver' in window) {
  const targets = [
    ...document.querySelectorAll('.statement > div'),
    ...document.querySelectorAll('.section-head'),
    ...document.querySelectorAll('.grid article'),
    ...document.querySelectorAll('.workflow li'),
    ...document.querySelectorAll('.download'),
    ...document.querySelectorAll('.api-card')
  ];
  targets.forEach(el => el.classList.add('reveal-target'));
  const observer = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (!entry.isIntersecting) return;
      entry.target.classList.add('is-visible');
      observer.unobserve(entry.target);
    });
  }, { threshold: .14, rootMargin: '0px 0px -40px 0px' });
  targets.forEach(el => observer.observe(el));

  const counters = document.querySelectorAll('.statement .number');
  const counterObserver = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (!entry.isIntersecting) return;
      const el = entry.target;
      const raw = el.textContent.trim();
      const target = Number.parseFloat(raw);
      const decimals = raw.includes('.') ? 1 : 0;
      if (!Number.isFinite(target)) return;
      const start = performance.now();
      const duration = 900;
      function tick(now) {
        const p = Math.min(1, (now - start) / duration);
        const eased = 1 - Math.pow(1 - p, 3);
        el.textContent = (target * eased).toFixed(decimals);
        if (p < 1) requestAnimationFrame(tick);
      }
      requestAnimationFrame(tick);
      counterObserver.unobserve(el);
    });
  }, { threshold: .7 });
  counters.forEach(el => counterObserver.observe(el));
}

if (!reducedMotion && window.matchMedia('(pointer: fine)').matches) {
  const visual = document.querySelector('.hero-visual');
  const screenshot = document.querySelector('.screenshot');
  if (visual && screenshot) {
    visual.addEventListener('pointermove', event => {
      const box = visual.getBoundingClientRect();
      const x = (event.clientX - box.left) / box.width - .5;
      const y = (event.clientY - box.top) / box.height - .5;
      screenshot.style.setProperty('--tilt-x', (-y * 3.5).toFixed(2) + 'deg');
      screenshot.style.setProperty('--tilt-y', (x * 4.5).toFixed(2) + 'deg');
      screenshot.style.setProperty('--parallax-x', (x * 8).toFixed(1) + 'px');
      screenshot.style.setProperty('--parallax-y', (y * 5).toFixed(1) + 'px');
    });
    visual.addEventListener('pointerleave', () => {
      ['--tilt-x', '--tilt-y', '--parallax-x', '--parallax-y'].forEach(name => {
        screenshot.style.setProperty(name, name.includes('deg') ? '0deg' : '0px');
      });
    });
  }
}

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
    status.textContent = isChinese ? 'API 暂不可用' : 'API unavailable';
  });
