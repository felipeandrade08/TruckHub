-- TruckHub agora é um projeto interno e gratuito da TransPoli.
-- Não há mais cobrança: converte todas as licenças existentes (trial ou
-- pagas) para vitalícia/ativa, sem data de expiração.

UPDATE licenses
SET license_type = 'lifetime',
    status = 'active',
    expires_at = NULL,
    activated_at = COALESCE(activated_at, NOW()),
    updated_at = NOW()
WHERE license_type <> 'lifetime' OR status <> 'active' OR expires_at IS NOT NULL;
