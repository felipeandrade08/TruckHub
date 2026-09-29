-- TransPoli — unifica acesso administrativo com a conta TransPoli.
-- O criador da empresa é o proprietário administrativo. Outros membros podem
-- receber role=admin pela Central; credenciais legadas da Diretoria permanecem
-- apenas para compatibilidade durante a transição.
UPDATE company_members cm
SET role='admin'
FROM companies co
WHERE co.id=cm.company_id
  AND co.created_by_user_id=cm.user_id
  AND cm.status='active'
  AND cm.role<>'admin';

CREATE INDEX IF NOT EXISTS idx_company_members_admin_access
  ON company_members(user_id,company_id,role,status);
