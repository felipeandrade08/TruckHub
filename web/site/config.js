// TransPoli Web — configuração central da API.
// O endpoint público atual é o Worker da Cloudflare. Um valor externo não deve
// sobrescrever o endpoint de produção por engano, pois isso quebra o painel
// quando um domínio personalizado de API não está configurado no DNS.
const DEFAULT_API_URL = 'https://truckhub.felipe-pessoall2026.workers.dev';
window.TRANSPOLI_API_URL = DEFAULT_API_URL;
window.TRANSPOLI_CONFIG = Object.freeze({
  apiUrl: DEFAULT_API_URL,
});
