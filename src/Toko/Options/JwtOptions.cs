using System.ComponentModel.DataAnnotations;

namespace Toko.Options
{
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        [Required]
        [MinLength(32)]
        public string Key { get; init; } = default!;

        public string? Issuer { get; init; }

        public string? Audience { get; init; }
    }
}
