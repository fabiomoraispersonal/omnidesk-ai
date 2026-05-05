/**
 * Contract: Shared Validators
 *
 * Both Angular projects (omniDesk.Admin, omniDesk.Crm) MUST implement
 * validators conforming to these signatures. The implementations live in
 * src/<project>/src/app/shared/validators/
 */

import { AbstractControl, ValidatorFn, ValidationErrors } from '@angular/forms';

/**
 * Returns a ValidatorFn that validates a CNPJ control value.
 *
 * Contract:
 * - Input value may contain formatting characters (dots, slashes, hyphens) —
 *   the validator strips non-digits before checking.
 * - Returns { cnpj: true } for:
 *     - Fewer or more than 14 digits after stripping
 *     - All-identical-digit sequences (e.g., "00000000000000")
 *     - Incorrect first or second check digit
 * - Returns null for a valid CNPJ.
 */
export declare function cnpjValidator(): ValidatorFn;

/**
 * Returns a ValidatorFn that validates a CPF control value.
 *
 * Contract:
 * - Input value may contain formatting characters (dots, hyphens) —
 *   the validator strips non-digits before checking.
 * - Returns { cpf: true } for:
 *     - Fewer or more than 11 digits after stripping
 *     - All-identical-digit sequences (e.g., "00000000000")
 *     - Incorrect first or second check digit
 * - Returns null for a valid CPF.
 */
export declare function cpfValidator(): ValidatorFn;

/**
 * Standard user-facing error messages keyed by Angular validator error name.
 * Every component that displays form errors MUST use this map.
 * All values are in pt-BR.
 */
export declare const FORM_ERRORS: Record<string, string>;
