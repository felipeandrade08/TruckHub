-- Ranking dos motoristas TransPoli.
-- O ranking é derivado das viagens finalizadas e do livro-caixa de fretes;
-- não existe uma segunda fonte de quilometragem ou pagamento.
CREATE INDEX IF NOT EXISTS idx_company_members_ranking
  ON company_members(company_id,status,role,user_id);

CREATE INDEX IF NOT EXISTS idx_trips_ranking_driver_period
  ON trips(user_id,status,finished_at);

CREATE INDEX IF NOT EXISTS idx_economy_ledger_trip_income
  ON economy_ledger(trip_id,entry_type)
  WHERE entry_type='trip_income';
