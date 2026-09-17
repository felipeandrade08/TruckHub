-- TransPoli V1.0.13 — tarifas dinâmicas de carga.
-- A tarifa base de cada categoria fica entre R$ 4,00 e R$ 5,80/km.
-- O aplicativo/API aplica uma variação determinística a cada 20 minutos,
-- limitada ao intervalo final de R$ 4,00 a R$ 6,00/km.

UPDATE cargo_rates SET rate_brl_km = 4.00 WHERE cargo_key = 'default';
UPDATE cargo_rates SET rate_brl_km = 4.20 WHERE cargo_key = 'milho';
UPDATE cargo_rates SET rate_brl_km = 4.40 WHERE cargo_key = 'soja';
UPDATE cargo_rates SET rate_brl_km = 4.60 WHERE cargo_key = 'carvao';
UPDATE cargo_rates SET rate_brl_km = 4.80 WHERE cargo_key = 'veiculos';
UPDATE cargo_rates SET rate_brl_km = 5.20 WHERE cargo_key = 'pesada';
UPDATE cargo_rates SET rate_brl_km = 5.60 WHERE cargo_key = 'especial';
UPDATE cargo_rates SET rate_brl_km = 5.00 WHERE cargo_key = 'refrigerada';
UPDATE cargo_rates SET rate_brl_km = 5.40 WHERE cargo_key = 'perigosa';

INSERT INTO cargo_rates(cargo_key,display_name,rate_brl_km) VALUES
 ('construcao','Construção',4.60),
 ('agricola','Agrícola',4.20),
 ('madeira','Madeira',4.40),
 ('minerais','Minerais',4.80),
 ('eletronicos','Eletrônicos',5.00),
 ('industrial','Industrial',5.20),
 ('logistica','Logística',4.00)
ON CONFLICT(cargo_key) DO UPDATE SET rate_brl_km=EXCLUDED.rate_brl_km,active=TRUE,updated_at=NOW();
