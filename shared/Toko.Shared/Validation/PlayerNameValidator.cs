using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Toko.Shared.Validation
{
    public static partial class PlayerNameValidator
    {
        private const int MinLength = 1;
        private const int MaxLength = 20;

        [GeneratedRegex(@"^[\w\s\-_.,!?@#$%&*()+=<>{}[\]/\\|:;""'`~]*$")]
        private static partial Regex AllowedCharsRegex();

        public static ValidationResult ValidatePlayerName(string? playerName)
        {
            if (string.IsNullOrEmpty(playerName))
                return new ValidationResult(false, "Player name is required.");

            if (playerName.Length < MinLength || playerName.Length > MaxLength)
                return new ValidationResult(false, $"Player name must be {MinLength}-{MaxLength} characters.");

            if (!AllowedCharsRegex().IsMatch(playerName))
                return new ValidationResult(false, "Player name contains invalid characters.");

            if (ContainsControlCharacters(playerName))
                return new ValidationResult(false, "Player name contains forbidden characters.");

            return new ValidationResult(true, string.Empty);
        }

        private static bool ContainsControlCharacters(string input)
        {
            foreach (char c in input)
            {
                var category = char.GetUnicodeCategory(c);
                if (category == System.Globalization.UnicodeCategory.Control ||
                    category == System.Globalization.UnicodeCategory.Format ||
                    category == System.Globalization.UnicodeCategory.OtherNotAssigned ||
                    category == System.Globalization.UnicodeCategory.PrivateUse ||
                    category == System.Globalization.UnicodeCategory.Surrogate)
                {
                    return true;
                }

                if (c >= 0x202A && c <= 0x202E)
                    return true;
            }
            return false;
        }

        public record ValidationResult(bool IsValid, string ErrorMessage);
    }

    public partial class PlayerNameAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            var result = PlayerNameValidator.ValidatePlayerName(value as string);
            return result.IsValid;
        }

        public override string FormatErrorMessage(string name)
        {
            return $"{name} must be 1-20 characters and contain only letters, numbers, and common symbols.";
        }
    }
}
