import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

export function cpfValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: string = (control.value ?? '').replace(/\D/g, '');

    if (value.length !== 11) return { cpf: true };
    if (/^(\d)\1{10}$/.test(value)) return { cpf: true };

    let sum = 0;
    for (let i = 0; i < 9; i++) {
      sum += parseInt(value[i], 10) * (10 - i);
    }
    let digit = sum % 11 < 2 ? 0 : 11 - (sum % 11);
    if (digit !== parseInt(value[9], 10)) return { cpf: true };

    sum = 0;
    for (let i = 0; i < 10; i++) {
      sum += parseInt(value[i], 10) * (11 - i);
    }
    digit = sum % 11 < 2 ? 0 : 11 - (sum % 11);
    if (digit !== parseInt(value[10], 10)) return { cpf: true };

    return null;
  };
}
