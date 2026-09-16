-- TruckHub V1 — auditoria de integridade antes da conexão real com Neon.
-- Esta migration não altera dados nem falha por registros antigos inconsistentes.
-- Ela cria uma função de diagnóstico para validar o estado do banco com segurança.

CREATE OR REPLACE FUNCTION truckhub_integrity_audit()
RETURNS TABLE (
  check_name TEXT,
  issue_count BIGINT,
  severity TEXT
)
LANGUAGE SQL
STABLE
AS $$
  SELECT 'users_without_license'::TEXT,
         COUNT(*)::BIGINT,
         CASE WHEN COUNT(*) = 0 THEN 'ok' ELSE 'error' END
  FROM users u
  LEFT JOIN licenses l ON l.user_id = u.id
  WHERE l.id IS NULL

  UNION ALL

  SELECT 'licenses_without_user'::TEXT,
         COUNT(*)::BIGINT,
         CASE WHEN COUNT(*) = 0 THEN 'ok' ELSE 'error' END
  FROM licenses l
  LEFT JOIN users u ON u.id = l.user_id
  WHERE u.id IS NULL

  UNION ALL

  SELECT 'active_devices_without_license'::TEXT,
         COUNT(*)::BIGINT,
         CASE WHEN COUNT(*) = 0 THEN 'ok' ELSE 'error' END
  FROM devices d
  LEFT JOIN licenses l ON l.id = d.license_id
  WHERE d.status = 'active' AND l.id IS NULL

  UNION ALL

  SELECT 'active_trips_invalid_finished_at'::TEXT,
         COUNT(*)::BIGINT,
         CASE WHEN COUNT(*) = 0 THEN 'ok' ELSE 'error' END
  FROM trips
  WHERE status = 'active' AND finished_at IS NOT NULL

  UNION ALL

  SELECT 'finished_trips_without_finished_at'::TEXT,
         COUNT(*)::BIGINT,
         CASE WHEN COUNT(*) = 0 THEN 'ok' ELSE 'error' END
  FROM trips
  WHERE status = 'finished' AND finished_at IS NULL

  UNION ALL

  SELECT 'negative_trip_distance'::TEXT,
         COUNT(*)::BIGINT,
         CASE WHEN COUNT(*) = 0 THEN 'ok' ELSE 'error' END
  FROM trips
  WHERE distance_km IS NOT NULL AND distance_km < 0

  UNION ALL

  SELECT 'negative_trip_fuel'::TEXT,
         COUNT(*)::BIGINT,
         CASE WHEN COUNT(*) = 0 THEN 'ok' ELSE 'error' END
  FROM trips
  WHERE fuel_used_l IS NOT NULL AND fuel_used_l < 0

  UNION ALL

  SELECT 'lifetime_licenses_with_expiration'::TEXT,
         COUNT(*)::BIGINT,
         CASE WHEN COUNT(*) = 0 THEN 'error' ELSE 'ok' END
  FROM licenses
  WHERE license_type = 'lifetime' AND expires_at IS NOT NULL

  UNION ALL

  SELECT 'trial_licenses_without_expiration'::TEXT,
         COUNT(*)::BIGINT,
         CASE WHEN COUNT(*) = 0 THEN 'error' ELSE 'ok' END
  FROM licenses
  WHERE license_type = 'trial' AND trial_expires_at IS NULL;
$$;

COMMENT ON FUNCTION truckhub_integrity_audit() IS
  'Auditoria somente leitura da integridade estrutural do banco TruckHub.';
