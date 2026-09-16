const API_BASE = window.TRUCKHUB_API_URL || 'http://localhost:8787';

function setMessage(message, type = 'error') {
  let box = document.querySelector('[data-auth-message]');
  if (!box) {
    box = document.createElement('div');
    box.dataset.authMessage = '';
    box.className = 'auth-message';
    const form = document.querySelector('form');
    form?.parentNode?.insertBefore(box, form);
  }
  box.hidden = false;
  box.textContent = message;
  box.dataset.type = type;
}

async function api(path, options = {}) {
  const response = await fetch(`${API_BASE}${path}`, {
    ...options,
    credentials: 'include',
    headers: { 'content-type': 'application/json', ...(options.headers || {}) },
  });
  const data = await response.json().catch(() => ({ ok: false, error: 'Resposta inválida da API.' }));
  if (!response.ok) throw new Error(data.error || 'Não foi possível concluir a operação.');
  return data;
}

function getSafeNext() {
  const value = new URLSearchParams(window.location.search).get('next');
  if (!value) return 'dashboard.html';
  const allowed = new Set(['dashboard.html', 'pagamento.html', 'pagamento-sucesso.html']);
  const target = value.split('#')[0].split('?')[0];
  if (!allowed.has(target)) return 'dashboard.html';
  const hash = value.includes('#') ? `#${value.split('#')[1]}` : '';
  return `${target}${hash}`;
}

const loginForm = document.querySelector('[data-login-form]');
if (loginForm) {
  loginForm.addEventListener('submit', async (event) => {
    event.preventDefault();
    const button = loginForm.querySelector('button[type="submit"]');
    const email = loginForm.querySelector('[name="email"]').value;
    const password = loginForm.querySelector('[name="password"]').value;
    button.disabled = true;
    button.dataset.originalText = button.dataset.originalText || button.textContent;
    button.textContent = 'Entrando...';
    try {
      await api('/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) });
      window.location.href = getSafeNext();
    } catch (error) {
      setMessage(error.message || 'E-mail ou senha inválidos.');
      button.disabled = false;
      button.textContent = button.dataset.originalText;
    }
  });
}

const registerForm = document.querySelector('[data-register-form]');
if (registerForm) {
  registerForm.addEventListener('submit', async (event) => {
    event.preventDefault();
    const button = registerForm.querySelector('button[type="submit"]');
    const name = registerForm.querySelector('[name="name"]').value;
    const email = registerForm.querySelector('[name="email"]').value;
    const password = registerForm.querySelector('[name="password"]').value;
    const confirm = registerForm.querySelector('[name="confirmPassword"]').value;
    if (password !== confirm) {
      setMessage('As senhas não coincidem.');
      return;
    }
    button.disabled = true;
    button.textContent = 'Criando conta...';
    try {
      const data = await api('/auth/register', { method: 'POST', body: JSON.stringify({ name, email, password }) });
      sessionStorage.setItem('truckhub_new_pin', data.pin);
      sessionStorage.setItem('truckhub_new_email', data.user.email);
      window.location.href = 'pin.html';
    } catch (error) {
      setMessage(error.message || 'Não foi possível criar sua conta.');
      button.disabled = false;
      button.textContent = 'Criar conta e começar →';
    }
  });
}
