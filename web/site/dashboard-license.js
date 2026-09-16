(() => {
  function modernizeDashboard() {
    const style = document.createElement('style');
    style.id = 'truckhub-dashboard-modern';
    style.textContent = `
      :root{--tp-gold:#ffc41f;--tp-bg:#02080f;--tp-card:#091827;--tp-card2:#0d2033;--tp-line:rgba(255,255,255,.09);--tp-text:#f6f8fb;--tp-muted:#91a0b2}
      body{font-size:14px!important}
      .license-banner,.license-mini{display:none!important}
      .dashboard{padding:42px 5%!important}
      .dash-top{margin-bottom:32px!important;align-items:center!important}
      .crumb{font-size:11px!important;letter-spacing:2.8px!important}
      .dash-top h1{font-size:42px!important;line-height:1.1!important;margin:12px 0 8px!important}
      .dash-top p{font-size:15px!important;color:var(--tp-muted)!important;line-height:1.5!important}
      .top-actions{gap:12px!important}
      .connection{font-size:11px!important;padding:13px 15px!important}
      .round-btn{width:44px;height:44px!important}
      .top-actions .btn{font-size:12px!important;padding:13px 17px!important}
      .stats-grid{gap:16px!important;margin-bottom:20px!important}
      .stat-card{min-height:154px!important;padding:22px!important;border-radius:14px!important}
      .stat-card>span{font-size:10px!important;letter-spacing:2px!important}
      .stat-card>strong{font-size:34px!important;margin:15px 0 4px!important}
      .stat-card>strong small{font-size:13px!important}
      .stat-card>small{font-size:11px!important}
      .stat-card em{font-size:10px!important;bottom:18px!important}
      .main-grid,.lower-grid{gap:18px!important;margin-bottom:18px!important}
      .panel{padding:26px!important;border-radius:15px!important}
      .panel-head{align-items:center!important}
      .panel-head span{font-size:10px!important;letter-spacing:2px!important}
      .panel-head h2{font-size:21px!important;margin-top:8px!important}
      .panel-head>a,.link-button{font-size:11px!important}
      .empty-trip{min-height:245px!important;margin-top:22px!important;padding:25px!important}
      .empty-trip strong{font-size:16px!important}
      .empty-trip p{font-size:12px!important;line-height:1.7!important;max-width:520px!important}
      .empty-trip .btn,.garage-empty .btn{font-size:11px!important;padding:13px 18px!important}
      .quick-grid{gap:12px!important;margin-top:20px!important}
      .quick-grid a{padding:19px!important;border-radius:11px!important}
      .quick-grid b{font-size:24px!important;margin-bottom:13px!important}
      .quick-grid strong{font-size:12px!important}
      .quick-grid small{font-size:10px!important;margin-top:5px!important}
      .level-info{font-size:11px!important}.progress-panel>p{font-size:12px!important;line-height:1.7!important}
      .finance-panel{margin-top:18px!important}
      .finance-panel>.muted{font-size:12px!important;line-height:1.7!important}
      .finance-summary{gap:12px!important;margin-top:20px!important}.finance-kpi{padding:18px!important;border-radius:10px!important}
      .finance-kpi span,.finance-metrics span{font-size:9px!important}.finance-kpi strong{font-size:24px!important}.finance-kpi small{font-size:9px!important}
      .finance-metrics{gap:12px!important}.finance-metrics>div{padding:15px!important}.finance-metrics strong{font-size:15px!important}
      .finance-box{padding:18px!important}.finance-box h3{font-size:11px!important}
      .hidden-section{scroll-margin-top:24px}
      .dash-footer{font-size:10px!important;margin-top:30px!important;padding-top:22px!important}
      .sidebar{width:275px!important;padding:25px 18px!important}.dashboard{width:calc(100% - 275px)!important;margin-left:275px!important}
      .side-brand strong{font-size:21px!important}.side-brand small{font-size:8px!important}
      .profile-mini{padding:15px!important}.profile-mini strong{font-size:12px!important}.profile-mini small{font-size:10px!important}
      .side-nav a,.settings,.logout{font-size:12px!important;padding:13px 13px!important}.side-nav{gap:5px!important}
      @media(max-width:1050px){.sidebar{width:225px!important}.dashboard{width:calc(100% - 225px)!important;margin-left:225px!important}.dash-top h1{font-size:34px!important}}
      @media(max-width:760px){.sidebar{width:100%!important}.dashboard{width:100%!important;margin-left:0!important;padding:28px 5%!important}.dash-top{display:block!important}.dash-top h1{font-size:32px!important}.dash-top p{font-size:13px!important}.top-actions{margin-top:20px!important}.top-actions .btn{flex:1}.stats-grid{grid-template-columns:1fr 1fr!important}.stat-card{min-height:135px!important}.quick-grid{grid-template-columns:1fr 1fr!important}}
      @media(max-width:520px){.stats-grid,.quick-grid{grid-template-columns:1fr!important}.panel{padding:20px!important}.stat-card{min-height:125px!important}}
    `;
    document.head.appendChild(style);

    document.querySelectorAll('.license-banner,.license-mini').forEach(el => el.remove());
    document.querySelectorAll('.side-nav a[href="#licenca"]').forEach(el => el.remove());

    const downloadButton = document.querySelector('.top-actions a.btn[href="bordo.html"]');
    if (downloadButton) {
      downloadButton.href = 'download.html';
      downloadButton.textContent = 'Baixar App Desktop ↓';
    }

    document.querySelectorAll('a[href="index.html#preco"],a[href="pagamento.html"]').forEach(el => {
      if (el.closest('.license-banner,.license-mini,#licenca')) el.remove();
    });

    document.querySelectorAll('[data-license-status],[data-license-detail]').forEach(el => {
      if (el.closest('.license-banner,.license-mini,#licenca')) el.closest('.license-banner,.license-mini,#licenca')?.remove();
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', modernizeDashboard);
  else modernizeDashboard();
})();
