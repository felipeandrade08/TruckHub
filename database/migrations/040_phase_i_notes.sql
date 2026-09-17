-- FASE I — Notas + histórico
-- Notas são pessoais do motorista e ficam vinculadas ao usuário autenticado.

CREATE TABLE IF NOT EXISTS driver_notes (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  title VARCHAR(120) NOT NULL DEFAULT 'Nota',
  content TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_driver_notes_user_updated
  ON driver_notes(user_id, updated_at DESC);
