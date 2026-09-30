using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GenderApi.Internal
{
    /// <summary>
    /// Cheap, certain client-side checks that mirror the OpenAPI schema. The API performs the
    /// authoritative validation (email syntax, ISO country membership, trial batch limits).
    /// </summary>
    internal static class Validation
    {
        internal const int MaxValueLength = 254;
        internal const int MaxIdLength = 64;
        internal const int MaxBatchItems = 50;

        private static readonly Regex CountryPattern = new Regex("^[A-Z]{2}$", RegexOptions.CultureInvariant);
        private static readonly Regex PhonePattern = new Regex(@"^\+?[0-9 ()\-]+$", RegexOptions.CultureInvariant);

        internal static void ValidateItem(GenderRequest? item, string paramName)
        {
            if (item == null)
            {
                throw new GenderApiValidationException("A request item is required.", paramName);
            }

            if (item.Type != GenderInputType.Name && item.Type != GenderInputType.Email && item.Type != GenderInputType.Username)
            {
                throw new GenderApiValidationException("type must be name, email or username.", paramName + ".Type");
            }

            string? value = item.Value;
            if (value == null || value.Trim().Length == 0)
            {
                throw new GenderApiValidationException("value must not be empty.", paramName + ".Value");
            }

            if (value.Length > MaxValueLength)
            {
                throw new GenderApiValidationException("value must contain at most 254 characters.", paramName + ".Value");
            }

            if (HasControlCharacter(value))
            {
                throw new GenderApiValidationException("value must not contain control characters.", paramName + ".Value");
            }

            if (item.Country != null)
            {
                ValidateCountry(item.Country, paramName + ".Country");
            }

            if (item.AiMode.HasValue && item.AiMode.Value != AiMode.Off && item.AiMode.Value != AiMode.Fallback && item.AiMode.Value != AiMode.Always)
            {
                throw new GenderApiValidationException("ai_mode must be off, fallback or always.", paramName + ".AiMode");
            }

            if (item.ForceToGenderize == true && item.AiMode.HasValue && item.AiMode.Value != AiMode.Fallback)
            {
                throw new GenderApiValidationException("forceToGenderize cannot be combined with ai_mode off or always.", paramName + ".ForceToGenderize");
            }

            if (item.Id != null && (item.Id.Length < 1 || item.Id.Length > MaxIdLength))
            {
                throw new GenderApiValidationException("id must contain 1-64 characters.", paramName + ".Id");
            }
        }

        internal static List<GenderRequest> ValidateBatch(IEnumerable<GenderRequest>? items, string paramName)
        {
            if (items == null)
            {
                throw new GenderApiValidationException("items is required.", paramName);
            }

            var list = new List<GenderRequest>(items);
            if (list.Count < 1 || list.Count > MaxBatchItems)
            {
                throw new GenderApiValidationException("A batch must contain 1-50 items; split larger jobs yourself.", paramName);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                ValidateItem(list[i], paramName + "[" + i + "]");
                string? id = list[i].Id;
                if (id != null && !ids.Add(id))
                {
                    throw new GenderApiValidationException("Batch item ids must be unique.", paramName + "[" + i + "].Id");
                }
            }

            return list;
        }

        internal static void ValidatePhone(string? number, string? country)
        {
            if (number == null || number.Length < 3 || number.Length > 32 || !PhonePattern.IsMatch(number))
            {
                throw new GenderApiValidationException("number must contain 3-32 characters: digits, spaces, parentheses, hyphens and an optional leading +.", nameof(number));
            }

            if (country != null)
            {
                ValidateCountry(country, nameof(country));
            }
        }

        internal static void ValidateCountry(string country, string paramName)
        {
            if (!CountryPattern.IsMatch(country))
            {
                throw new GenderApiValidationException("country must be an uppercase ISO 3166-1 alpha-2 code; omit it when unknown.", paramName);
            }
        }

        internal static bool HasControlCharacter(string value)
        {
            foreach (char c in value)
            {
                if (c < 0x20 || c == 0x7f)
                {
                    return true;
                }
            }

            return false;
        }

        internal static string? NormalizeApiKey(string? apiKey)
        {
            if (apiKey == null)
            {
                return null;
            }

            string trimmed = apiKey.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            foreach (char c in trimmed)
            {
                if (c <= 0x20 || c >= 0x7f)
                {
                    // Never echo the key in the message.
                    throw new GenderApiValidationException("The API key contains invalid characters.", "apiKey");
                }
            }

            return trimmed;
        }

        internal static Uri NormalizeBaseUrl(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl!.Trim(), UriKind.Absolute, out Uri? uri))
            {
                throw new GenderApiValidationException("BaseUrl must be an absolute URL.", "BaseUrl");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new GenderApiValidationException("BaseUrl must not contain credentials, a query or a fragment.", "BaseUrl");
            }

            bool https = uri.Scheme == Uri.UriSchemeHttps;
            bool localHttp = uri.Scheme == Uri.UriSchemeHttp && IsLocalHost(uri.Host);
            if (!https && !localHttp)
            {
                throw new GenderApiValidationException("BaseUrl must use HTTPS (plain HTTP is allowed only for localhost, 127.0.0.1 or [::1]).", "BaseUrl");
            }

            string text = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return new Uri(text, UriKind.Absolute);
        }

        private static bool IsLocalHost(string host)
        {
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || host == "127.0.0.1"
                || host == "[::1]"
                || host == "::1";
        }
    }
}
