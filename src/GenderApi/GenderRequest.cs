namespace GenderApi
{
    /// <summary>The kind of input sent as <c>type</c> on the wire.</summary>
    public enum GenderInputType
    {
        /// <summary>A person's name (<c>"name"</c>).</summary>
        Name,

        /// <summary>An email address (<c>"email"</c>).</summary>
        Email,

        /// <summary>A username or nickname (<c>"username"</c>).</summary>
        Username,
    }

    /// <summary>
    /// The AI mode sent as <c>options.ai_mode</c>. When omitted, the server applies its
    /// default: <c>fallback</c> for single requests and <c>off</c> for batch items.
    /// </summary>
    public enum AiMode
    {
        /// <summary>Dataset only (<c>"off"</c>).</summary>
        Off,

        /// <summary>Dataset first, AI only when the dataset has no answer (<c>"fallback"</c>, 1 credit total).</summary>
        Fallback,

        /// <summary>Always use AI (<c>"always"</c>, 2 credits).</summary>
        Always,
    }

    /// <summary>
    /// One gender inference request. Used for <c>POST /gender</c> and as a batch item for
    /// <c>POST /gender/batch</c>. Wire field names are <c>type</c>, <c>value</c>, <c>country</c>,
    /// <c>id</c>, <c>forceToGenderize</c> and <c>options.ai_mode</c>.
    /// </summary>
    public sealed class GenderRequest
    {
        /// <summary>Creates an empty request; set <see cref="Type"/> and <see cref="Value"/>.</summary>
        public GenderRequest()
        {
            Value = string.Empty;
        }

        /// <summary>Creates a request for the given input type and value.</summary>
        public GenderRequest(GenderInputType type, string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null)
        {
            Type = type;
            Value = value;
            Country = country;
            AiMode = aiMode;
            ForceToGenderize = forceToGenderize;
            Id = id;
        }

        /// <summary>Input type (<c>type</c>).</summary>
        public GenderInputType Type { get; set; }

        /// <summary>Input value (<c>value</c>): 1–254 characters, not blank, no control characters.</summary>
        public string Value { get; set; }

        /// <summary>Optional ISO 3166-1 alpha-2 country code in uppercase (<c>country</c>). Omit when unknown.</summary>
        public string? Country { get; set; }

        /// <summary>Optional AI mode (<c>options.ai_mode</c>). Null lets the server apply its default.</summary>
        public AiMode? AiMode { get; set; }

        /// <summary>
        /// Optional <c>forceToGenderize</c>: dataset first (1 credit), then nickname-aware AI
        /// (2 credits total). Cannot be combined with <see cref="GenderApi.AiMode.Off"/> or
        /// <see cref="GenderApi.AiMode.Always"/>.
        /// </summary>
        public bool? ForceToGenderize { get; set; }

        /// <summary>Optional caller-chosen item id (<c>id</c>), 1–64 characters; unique within a batch.</summary>
        public string? Id { get; set; }

        /// <summary>Creates a <c>name</c> request.</summary>
        public static GenderRequest ForName(string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null)
            => new GenderRequest(GenderInputType.Name, value, country, aiMode, forceToGenderize, id);

        /// <summary>Creates an <c>email</c> request.</summary>
        public static GenderRequest ForEmail(string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null)
            => new GenderRequest(GenderInputType.Email, value, country, aiMode, forceToGenderize, id);

        /// <summary>Creates a <c>username</c> request.</summary>
        public static GenderRequest ForUsername(string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null)
            => new GenderRequest(GenderInputType.Username, value, country, aiMode, forceToGenderize, id);
    }
}
