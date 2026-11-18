using PaymentService.Application.Common.Exceptions;
using PaymentService.Application.DTOs;
using PaymentService.Domain.Entities;

namespace PaymentService.Application.Common;

/// <summary>
/// اعتبارسنجی تسهیم درخواستی (درصدی یا مبلغی) و تبدیل به سهم‌های قطعی به ریال.
/// خروجی همیشه مبلغ است تا تشخیص نوع تسهیم در پاسارگاد (بالا/پایین ۱۰۰) مبهم نشود.
/// </summary>
public static class PaymentSplitCalculator
{
    public const int MaxShares = 10;          // سقف پاسارگاد
    public const long MinShareInRials = 101;  // مقدار ≤ ۱۰۰ توسط پاسارگاد درصد تلقی می‌شود

    public static IReadOnlyList<PaymentSplitShare> Normalize(
        long totalAmountInRials, SplitValueType? type, IReadOnlyList<PaymentSplit> splits)
    {
        var errors = new List<string>();

        if (type is null)
            errors.Add("نوع تسهیم (SplitType) مشخص نشده است");

        if (splits.Count < 2 || splits.Count > MaxShares)
            errors.Add($"تعداد حساب‌های تسهیم باید بین ۲ تا {MaxShares} باشد");

        var shebas = new List<string>();
        foreach (var split in splits)
        {
            var sheba = NormalizeSheba(split.Sheba);
            if (sheba is null)
                errors.Add($"شبا نامعتبر است: {split.Sheba}");
            else if (shebas.Contains(sheba))
                errors.Add($"شبا تکراری است: {split.Sheba}");
            else
                shebas.Add(sheba);

            if (split.Value <= 0)
                errors.Add("سهم هر حساب باید بزرگتر از صفر باشد");
        }

        if (errors.Count > 0)
            throw Fail(errors);

        var amounts = new long[splits.Count];

        if (type == SplitValueType.Percent)
        {
            if (splits.Sum(s => s.Value) != 100m)
                errors.Add("مجموع درصدها باید دقیقاً ۱۰۰ باشد");
            else
            {
                long assigned = 0;
                for (var i = 0; i < splits.Count - 1; i++)
                {
                    amounts[i] = (long)Math.Floor(totalAmountInRials * splits[i].Value / 100m);
                    assigned += amounts[i];
                }
                amounts[^1] = totalAmountInRials - assigned;   // باقی‌مانده‌ی گرد کردن به آخرین حساب
            }
        }
        else
        {
            if (splits.Any(s => s.Value != Math.Truncate(s.Value)))
                errors.Add("در تسهیم مبلغی، سهم‌ها باید عدد صحیح (ریال) باشند");
            else
            {
                for (var i = 0; i < splits.Count; i++)
                    amounts[i] = (long)splits[i].Value;

                if (amounts.Sum() != totalAmountInRials)
                    errors.Add("مجموع سهم‌ها باید دقیقاً برابر مبلغ پرداخت باشد");
            }
        }

        if (errors.Count == 0 && amounts.Any(a => a < MinShareInRials))
            errors.Add($"سهم هر حساب باید حداقل {MinShareInRials} ریال باشد");

        if (errors.Count > 0)
            throw Fail(errors);

        return shebas
            .Select((sheba, i) => new PaymentSplitShare(sheba, amounts[i]))
            .ToList();
    }

    /// <summary>شبا را بدون IR برمی‌گرداند؛ اگر طول یا checksum (mod 97) درست نبود null.</summary>
    public static string? NormalizeSheba(string? raw)
    {
        var s = raw?.Trim().Replace(" ", "").ToUpperInvariant();
        if (string.IsNullOrEmpty(s)) return null;

        if (s.StartsWith("IR", StringComparison.Ordinal)) s = s[2..];
        if (s.Length != 24 || !s.All(char.IsAsciiDigit)) return null;

        // ISO 13616: چهار نویسه‌ی اول (IR + رقم کنترل) به انتها می‌رود؛ IR = 1827
        var rearranged = s[2..] + "1827" + s[..2];
        var remainder = 0;
        foreach (var c in rearranged)
            remainder = (remainder * 10 + (c - '0')) % 97;

        return remainder == 1 ? s : null;
    }

    private static ValidationException Fail(List<string> errors)
        => new(new Dictionary<string, string[]> { ["Splits"] = errors.ToArray() });
}