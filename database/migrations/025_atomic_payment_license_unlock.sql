-- TruckHub V1 — confirmação financeira atômica.
-- O pagamento aprovado e a licença vitalícia são alterados na mesma transação PostgreSQL.
-- Se a licença não puder ser vinculada, toda a operação é revertida.

CREATE OR REPLACE FUNCTION truckhub_approve_payment_and_unlock_license(
  p_payment_id UUID,
  p_user_id UUID,
  p_external_id VARCHAR(180),
  p_external_reference VARCHAR(255),
  p_preference_id VARCHAR(180),
  p_event_id VARCHAR(180),
  p_provider_status VARCHAR(30),
  p_paid_at TIMESTAMPTZ,
  p_raw_event JSONB,
  p_configured_price NUMERIC
)
RETURNS TABLE(payment_id UUID, user_id UUID, license_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
  v_payment payments%ROWTYPE;
  v_license licenses%ROWTYPE;
BEGIN
  IF p_payment_id IS NULL OR p_user_id IS NULL OR p_external_id IS NULL
     OR p_external_reference IS NULL OR p_external_reference = ''
     OR p_external_id = '' OR p_configured_price IS NULL OR p_configured_price <= 0 THEN
    RAISE EXCEPTION 'atomic_unlock_invalid_arguments';
  END IF;

  SELECT * INTO v_payment
  FROM payments
  WHERE id = p_payment_id
    AND user_id = p_user_id
    AND provider = 'mercado_pago'
    AND external_reference = p_external_reference
    AND amount = p_configured_price
    AND currency = 'BRL'
    AND (external_id IS NULL OR external_id = p_external_id)
    AND (preference_id IS NULL OR (p_preference_id IS NOT NULL AND preference_id = p_preference_id))
  FOR UPDATE;

  IF NOT FOUND THEN
    RAISE EXCEPTION 'atomic_unlock_payment_identity_mismatch';
  END IF;

  UPDATE payments
  SET external_id = p_external_id,
      preference_id = COALESCE(preference_id, p_preference_id),
      provider_status = p_provider_status,
      status = 'approved',
      status_detail = NULL,
      paid_at = COALESCE(paid_at, p_paid_at),
      webhook_event_id = p_event_id,
      processed_at = NOW(),
      raw_event = p_raw_event,
      updated_at = NOW()
  WHERE id = v_payment.id
  RETURNING * INTO v_payment;

  SELECT * INTO v_license
  FROM licenses
  WHERE user_id = p_user_id
  FOR UPDATE;

  IF NOT FOUND THEN
    RAISE EXCEPTION 'atomic_unlock_license_not_found';
  END IF;

  UPDATE licenses
  SET license_type = 'lifetime',
      status = 'active',
      activated_at = COALESCE(activated_at, NOW()),
      expires_at = NULL,
      updated_at = NOW()
  WHERE id = v_license.id
  RETURNING * INTO v_license;

  RETURN QUERY SELECT v_payment.id, v_payment.user_id, v_license.id;
END;
$$;
