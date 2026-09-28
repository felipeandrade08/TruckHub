-- TransPoli Central da Diretoria — migração de PIN legado para senha administrativa separada.
-- Mantém pin_hash temporariamente para compatibilidade de dados, mas novas autenticações usam password_hash.
ALTER TABLE company_directors
  ADD COLUMN IF NOT EXISTS password_hash TEXT;

ALTER TABLE company_directors
  ADD COLUMN IF NOT EXISTS password_set_at TIMESTAMPTZ;

-- A conta que criou a empresa é a autoridade de bootstrap/recuperação.
-- Ela não precisa receber cargo administrativo em company_members.
UPDATE company_members cm
SET role='driver'
FROM companies co
WHERE co.id=cm.company_id
  AND co.created_by_user_id=cm.user_id
  AND cm.role='director';
