import { FormControl } from '@angular/forms';
import { cnpjValidator } from './cnpj.validator';

describe('cnpjValidator', () => {
  const validate = (value: string) =>
    cnpjValidator()(new FormControl(value));

  it('accepts a valid CNPJ', () => {
    expect(validate('11222333000181')).toBeNull();
  });

  it('accepts a valid CNPJ with mask formatting', () => {
    expect(validate('11.222.333/0001-81')).toBeNull();
  });

  it('rejects all-zeros', () => {
    expect(validate('00000000000000')).toEqual({ cnpj: true });
  });

  it('rejects all-same-digit sequences', () => {
    expect(validate('11111111111111')).toEqual({ cnpj: true });
    expect(validate('99999999999999')).toEqual({ cnpj: true });
  });

  it('rejects a CNPJ with wrong first check digit', () => {
    expect(validate('11222333000182')).toEqual({ cnpj: true });
  });

  it('rejects a CNPJ with wrong second check digit', () => {
    expect(validate('11222333000180')).toEqual({ cnpj: true });
  });

  it('rejects fewer than 14 digits', () => {
    expect(validate('1122233300018')).toEqual({ cnpj: true });
  });

  it('rejects more than 14 digits', () => {
    expect(validate('112223330001810')).toEqual({ cnpj: true });
  });

  it('returns null for empty string (let required validator handle it)', () => {
    expect(validate('')).toEqual({ cnpj: true });
  });

  it('handles null value gracefully', () => {
    expect(validate(null as unknown as string)).toEqual({ cnpj: true });
  });
});
