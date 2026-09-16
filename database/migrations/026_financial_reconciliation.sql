-- TruckHub V1 — reconciliação financeira segura.
-- Detecta pagamentos Mercado Pago aprovados sem licença vitalícia ativa.
-- A função é idempotente: executar novamente não duplica licença nem cria registros.

CREATE OR REPLACE FUNCTION truckhub_reconcile_approved_payment(p_payment_id UUID)
RETURNS TABLE(payment_id UUID, user_id UUID, license_id UUID, repaired BOOLEAN)
LANGUAGE plpgsql
AS $$
DECLARE
  v_payment payments%ROWTYPE;
  v_license licenses%ROWTYPE;
BEGIN
  SELECT * INTO v_payment
  FROM payments
  WHERE id = p_payment_id
    AND provider = 'mercado_pago'
    AND status = 'approved'
    AND external_id IS NOT NULL
    AND external_reference IS NOT NULL
    AND amount > 0
    AND currency = 'BRL'
  FOR UPDATE;

  IF NOT FOUND THEN
    RETURN;
  END IF;

  SELECT * INTO v_license
  FROM licenses
  WHERE user_id = v_payment.user_id
  FOR UPDATE;

  IF NOT FOUND THEN
    RAISE EXCEPTION 'reconcile_license_not_found';
  END IF;

  IF v_license.license_type = 'lifetime'
     AND v_license.status = 'active'
     AND v_license.expires_at IS NULL THEN
    RETURN QUERY SELECT v_payment.id, v_payment.user_id, v_license.id, FALSE;
    RETURN;
  END IF;

  UPDATE licenses
  SET license_type = 'lifetime',
      status = 'active',
      activated_at = COALESCE(activated_at, NOW()),
      expires_at = NULL,
      updated_at = NOW()
  WHERE id = v_license.id
  RETURNING * INTO v_license;

  RETURN QUERY SELECT v_payment.id, v_payment.user_id, v_license.id, TRUE;
END;
$$;

CREATE INDEX IF NOT EXISTS idx_payments_reconciliation
  ON payments(provider, status, created_at)
  WHERE provider = 'mercado_pago' AND status = 'approved';
