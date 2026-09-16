// TransPoli Web — configuração central da API.
// Em produção, usa a API oficial. Para desenvolvimento local,
// defina window.TRANSPOLI_API_URL antes deste arquivo.
window.TRANSPOLI_API_URL = window.TRANSPOLI_API_URL || 'https://truckhub.felipe-pessoall2026.workers.dev';
window.TRANSPOLI_CONFIG = Object.freeze({
  apiUrl: window.TRANSPOLI_API_URL,
});
