(() => {
  const loginForm = document.querySelector('[data-login-form]');
  if (!loginForm) return;
  const apiBase = window.TRANSPOLI_API_URL || 'https://truckhub.felipe-pessoall2026.workers.dev';
  const nextValue = new URLSearchParams(window.location.search).get('next');
  const allowed = new Set(['dashboard.html', 'pagamento.html', 'pagamento-sucesso.html']);
  const target = nextValue ? nextValue.split('#')[0].split('?')[0] : 'dashboard.html';
  const next = allowed.has(target) ? `${target}${nextValue?.includes('#') ? `#${nextValue.split('#')[1]}` : ''}` : 'dashboard.html';

  fetch(`${apiBase}/me`, { credentials: 'include', cache: 'no-store' })
    .then(async (response) => {
      if (!response.ok) return null;
      const data = await response.json().catch(() => null);
      return data?.user ? data : null;
    })
    .then((data) => {
      if (data?.user) window.location.replace(next);
    })
    .catch(() => { /* sem sessão: permanece no login */ });
})();
