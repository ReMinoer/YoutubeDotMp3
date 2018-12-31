using System.Globalization;
using System.Windows.Controls;

namespace YoutubeDotMp3.ValidationRules.Base
{
    public abstract class SimpleValidationRuleBase : ValidationRule
    {
        public string ErrorMessage { get; set; }

        protected abstract bool IsValid(object value, CultureInfo cultureInfo);

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            return IsValid(value, cultureInfo) ? ValidationResult.ValidResult : new ValidationResult(false, ErrorMessage);
        }
    }

    public abstract class SimpleValidationRuleBase<TValue> : ValidationRule
    {
        public string ErrorMessage { get; set; }

        protected abstract bool IsValid(TValue value, CultureInfo cultureInfo);

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            return IsValid((TValue)value, cultureInfo) ? ValidationResult.ValidResult : new ValidationResult(false, ErrorMessage);
        }
    }
}