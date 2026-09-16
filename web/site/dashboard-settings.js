(() => {
  function showError(selector, message) {
    const el = document.querySelector(selector);
    if (!el) return;
    el.textContent = message;
    el.hidden = false;
  }
  function hideError(selector) {
    const el = document.querySelector(selector);
    if (el) el.hidden = true;
  }
  function showSuccess(selector, message) {
    const el = document.querySelector(selector);
    if (!el) return;
    el.textContent = message;
    el.hidden = false;
    setTimeout(() => { el.hidden = true; }, 6000);
  }

  // --- Trocar senha ---
  const passwordForm = document.querySelector('[data-password-form]');
  if (passwordForm) {
    passwordForm.addEventListener('submit', async (event) => {
      event.preventDefault();
      hideError('[data-password-error]');
      const button = passwordForm.querySelector('button[type="submit"]');
      const currentPassword = passwordForm.elements.currentPassword.value;
      const newPassword = passwordForm.elements.newPassword.value;
      button.disabled = true;
      button.textContent = 'Salvando...';
      try {
        await api('/me/password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) });
        passwordForm.reset();
        showSuccess('[data-password-success]', 'Senha atualizada com sucesso.');
      } catch (error) {
        showError('[data-password-error]', error.message || 'Não foi possível trocar a senha.');
      } finally {
        button.disabled = false;
        button.textContent = 'Salvar nova senha';
      }
    });
  }

  // --- Gerar novo PIN ---
  const pinForm = document.querySelector('[data-pin-form]');
  if (pinForm) {
    pinForm.addEventListener('submit', async (event) => {
      event.preventDefault();
      hideError('[data-pin-error]');
      const button = pinForm.querySelector('button[type="submit"]');
      const password = pinForm.elements.password.value;
      button.disabled = true;
      button.textContent = 'Gerando...';
      try {
        const data = await api('/me/pin/regenerate', { method: 'POST', body: JSON.stringify({ password }) });
        pinForm.reset();
        const box = document.querySelector('[data-pin-result]');
        const value = document.querySelector('[data-pin-value]');
        if (box && value) { value.textContent = data.pin; box.hidden = false; }
      } catch (error) {
        showError('[data-pin-error]', error.message || 'Não foi possível gerar um novo PIN.');
      } finally {
        button.disabled = false;
        button.textContent = 'Gerar novo PIN';
      }
    });
  }

  // --- Computador de bordo vinculado ---
  async function loadDevice() {
    const info = document.querySelector('[data-device-info]');
    if (!info) return;
    try {
      const data = await api('/me/device');
      if (!data.device || data.device.status !== 'active') {
        info.innerHTML = '<span>Nenhum computador ativado no momento.</span>';
        const btn = document.querySelector('[data-release-device]');
        if (btn) btn.disabled = true;
        return;
      }
      const name = data.device.device_name || 'Computador sem nome';
      const seen = data.device.last_seen_at ? new Date(data.device.last_seen_at).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : '—';
      info.innerHTML = `<div><strong>${name}</strong><small>Última atividade: ${seen}</small></div>`;
    } catch (error) {
      info.innerHTML = '<span>Não foi possível carregar o dispositivo.</span>';
    }
  }
  loadDevice();

  const releaseButton = document.querySelector('[data-release-device]');
  if (releaseButton) {
    releaseButton.addEventListener('click', async () => {
      hideError('[data-device-error]');
      if (!confirm('Liberar o computador atual? Você precisará ativar o TransPoli novamente com seu e-mail e PIN.')) return;
      releaseButton.disabled = true;
      releaseButton.textContent = 'Liberando...';
      try {
        await api('/me/device/release', { method: 'POST', body: JSON.stringify({}) });
        showSuccess('[data-device-success]', 'Computador liberado. Você já pode ativar o TransPoli em outro dispositivo.');
        await loadDevice();
      } catch (error) {
        showError('[data-device-error]', error.message || 'Não foi possível liberar o dispositivo.');
      } finally {
        releaseButton.disabled = false;
        releaseButton.textContent = 'Liberar computador atual';
      }
    });
  }

  // --- Sair de todas as sessões ---
  const logoutAllButton = document.querySelector('[data-logout-everywhere]');
  if (logoutAllButton) {
    logoutAllButton.addEventListener('click', async () => {
      if (!confirm('Isso vai encerrar sua sessão neste e em qualquer outro navegador. Continuar?')) return;
      logoutAllButton.disabled = true;
      try {
        await api('/me/sessions/revoke-all', { method: 'POST' });
      } catch (error) {
        // mesmo se falhar no servidor, seguimos para o login
      } finally {
        location.href = 'login.html';
      }
    });
  }
})();
