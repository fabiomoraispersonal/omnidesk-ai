namespace omniDesk.Api.Infrastructure.Validators;

public static class CnpjValidator
{
    public static bool IsValidCnpj(string cnpj)
    {
        var digits = cnpj.Replace(".", "").Replace("/", "").Replace("-", "");

        if (digits.Length != 14) return false;
        if (digits.Distinct().Count() == 1) return false;

        var multipliers1 = new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var multipliers2 = new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (digits[i] - '0') * multipliers1[i];

        var remainder = sum % 11;
        var firstDigit = remainder < 2 ? 0 : 11 - remainder;

        if (firstDigit != (digits[12] - '0')) return false;

        sum = 0;
        for (var i = 0; i < 13; i++)
            sum += (digits[i] - '0') * multipliers2[i];

        remainder = sum % 11;
        var secondDigit = remainder < 2 ? 0 : 11 - remainder;

        return secondDigit == (digits[13] - '0');
    }
}
