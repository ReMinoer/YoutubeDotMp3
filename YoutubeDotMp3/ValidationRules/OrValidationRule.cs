using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Markup;
using YoutubeDotMp3.ValidationRules.Base;

namespace YoutubeDotMp3.ValidationRules
{
    [ContentProperty(nameof(Rules))]
    public class OrValidationRule : SimpleValidationRuleBase
    {
        public ObservableCollection<ValidationRule> Rules { get; } = new ObservableCollection<ValidationRule>();
        protected override bool IsValid(object value, CultureInfo cultureInfo) => Rules.Select(x => x.Validate(value, cultureInfo)).FirstOrDefault(x => x.IsValid) != null;
    }
}