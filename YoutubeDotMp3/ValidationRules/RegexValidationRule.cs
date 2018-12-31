using System.Globalization;
using System.Text.RegularExpressions;
using YoutubeDotMp3.ValidationRules.Base;

namespace YoutubeDotMp3.ValidationRules
{
    public class RegexValidationRule : SimpleValidationRuleBase<string>
    {
        public Regex Regex { get; set; }
        protected override bool IsValid(string value, CultureInfo cultureInfo) => Regex == null || Regex.IsMatch(value ?? string.Empty);
    }
}