import { FormControl } from '@angular/forms';
import { cpfValidator } from './cpf.validator';

describe('cpfValidator', () => {
  const validate = (value: string) =>
    cpfValidator()(new FormControl(value));

  it('accepts a valid CPF', () => {
    expect(validate('52998224725')).toBeNull();
  });

  it('accepts a valid CPF with mask formatting', () => {
    expect(validate('529.982.247-25')).toBeNull();
  });

  it('rejects all-zeros', () => {
    expect(validate('00000000000')).toEqual({ cpf: true });
  });

  it('rejects all-same-digit sequences', () => {
    expect(validate('11111111111')).toEqual({ cpf: true });
    expect(validate('99999999999')).toEqual({ cpf: true });
  });

  it('rejects a CPF with wrong first check digit', () => {
    expect(validate('52998224715')).toEqual({ cpf: true });
  });

  it('rejects a CPF with wrong second check digit', () => {
    expect(validate('52998224724')).toEqual({ cpf: true });
  });

  it('rejects fewer than 11 digits', () => {
    expect(validate('5299822472')).toEqual({ cpf: true });
  });

  it('rejects more than 11 digits', () => {
    expect(validate('529982247250')).toEqual({ cpf: true });
  });

  it('rejects empty string', () => {
    expect(validate('')).toEqual({ cpf: true });
  });

  it('handles null value gracefully', () => {
    expect(validate(null as unknown as string)).toEqual({ cpf: true });
  });
});
