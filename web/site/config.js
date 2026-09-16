// TruckHub Web — configuração central da API.
// Em produção, usa a API oficial. Para desenvolvimento local,
// defina window.TRUCKHUB_API_URL antes deste arquivo.
window.TRUCKHUB_API_URL = window.TRUCKHUB_API_URL || 'https://truckhub.felipe-pessoall2026.workers.dev';
window.TRUCKHUB_CONFIG = Object.freeze({
  apiUrl: window.TRUCKHUB_API_URL,
});
