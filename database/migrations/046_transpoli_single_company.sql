-- TransPoli é uma instalação centralizada: somente uma empresa pode existir neste banco.
-- O índice usa uma expressão constante para impedir uma segunda empresa mesmo em concorrência.
CREATE UNIQUE INDEX IF NOT EXISTS uq_transpoli_single_company
  ON companies ((1));
