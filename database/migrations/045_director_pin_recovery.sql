-- Recuperação segura do PIN da Diretoria.
CREATE TABLE IF NOT EXISTS company_director_pin_resets (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  director_id UUID NOT NULL REFERENCES company_directors(id) ON DELETE CASCADE,
  token_hash TEXT NOT NULL UNIQUE,
  expires_at TIMESTAMPTZ NOT NULL,
  used_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_director_pin_resets_active
  ON company_director_pin_resets(director_id,expires_at)
  WHERE used_at IS NULL;
